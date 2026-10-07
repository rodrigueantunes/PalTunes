using System.Runtime.CompilerServices;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace PalTunes.App.Views.Controls;

/// <summary>
/// Cadrage des vues 3D (Z vers le haut) : la sphère englobant le modèle est ajustée au champ de vision
/// horizontal et vertical du viewport, sans dépendre d'un calcul différé. Chaque vue cadrée est ensuite gardée :
/// la caméra ne peut pas entrer dans la sphère du modèle (zoom avant arrêté avant la déformation de perspective),
/// ni s'éloigner au point de le perdre ; champ de vision fixe, plans de découpe recalculés à chaque mouvement.
/// </summary>
public static class CameraHelper
{
    private const double FieldOfView = 30;

    /// <summary>Distance minimale caméra – centre du modèle, en rayons de la sphère englobante (cadrage ≈ 4,6).</summary>
    private const double MinDistance = 1.3;

    /// <summary>Distance maximale, en multiple de la distance de cadrage.</summary>
    private const double MaxDistance = 4;

    private sealed class Guard
    {
        public Point3D Center;
        public double Radius;
        public double Fit;
        public bool Busy;
    }

    private static readonly ConditionalWeakTable<HelixViewport3D, Guard> Guards = new();

    public static void Iso(HelixViewport3D viewport, Model3D? model) => Look(viewport, model, new Vector3D(-0.75, -0.9, 0.6), new Vector3D(0, 0, 1));

    /// <summary>Recadre en conservant la direction de vue actuelle (après redimensionnement).</summary>
    public static void Refit(HelixViewport3D viewport, Model3D? model)
    {
        if (viewport.Camera is ProjectionCamera camera && camera.LookDirection.Length > 0)
        {
            var from = -camera.LookDirection;
            Look(viewport, model, from, camera.UpDirection);
        }
    }

    public static void Look(HelixViewport3D viewport, Model3D? model, Vector3D fromDirection, Vector3D up) => Look(viewport, model, fromDirection, up, animate: false);

    /// <summary>
    /// Cadrage avec transition fluide (≈ 0,35 s) : la caméra tourne autour du modèle jusqu'à la vue demandée. Un clic,
    /// la molette ou un nouveau cadrage interrompent la transition (pas d'attente, pas de saccade).
    /// </summary>
    public static void LookSmooth(HelixViewport3D viewport, Model3D? model, Vector3D fromDirection, Vector3D up) =>
        Look(viewport, model, fromDirection, up, animate: Motion.Enabled);

    public static void IsoSmooth(HelixViewport3D viewport, Model3D? model) => LookSmooth(viewport, model, new Vector3D(-0.75, -0.9, 0.6), new Vector3D(0, 0, 1));

    /// <summary>Recadrage fluide en gardant la direction de vue.</summary>
    public static void RefitSmooth(HelixViewport3D viewport, Model3D? model)
    {
        if (viewport.Camera is ProjectionCamera camera && camera.LookDirection.Length > 0)
        {
            LookSmooth(viewport, model, -camera.LookDirection, camera.UpDirection);
        }
    }

    private sealed class Flight
    {
        public required PerspectiveCamera Camera;
        public required Guard Guard;
        public Point3D FromPosition, FromTarget, ToPosition, Center;
        public Vector3D FromUp, ToUp;
        public double Distance;
        public DateTime Start;
        public EventHandler? Tick;
    }

    private static readonly ConditionalWeakTable<HelixViewport3D, Flight> Flights = new();

    /// <summary>Arrête une transition de caméra en cours (geste de l'utilisateur).</summary>
    public static void Stop(HelixViewport3D viewport)
    {
        if (Flights.TryGetValue(viewport, out var flight) && flight.Tick != null)
        {
            System.Windows.Media.CompositionTarget.Rendering -= flight.Tick;
            flight.Tick = null;
            Flights.Remove(viewport);
        }
    }

    private static void Look(HelixViewport3D viewport, Model3D? model, Vector3D fromDirection, Vector3D up, bool animate)
    {
        if (viewport.Camera is not PerspectiveCamera camera || model == null)
        {
            return;
        }

        Stop(viewport);

        var b = Services.Scene3DBuilder.BoundsOf(model);
        if (b.IsEmpty)
        {
            return;
        }

        var center = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
        var radius = Math.Max(0.02, Math.Sqrt(b.SizeX * b.SizeX + b.SizeY * b.SizeY + b.SizeZ * b.SizeZ) / 2);
        var w = Math.Max(1, viewport.ActualWidth);
        var h = Math.Max(1, viewport.ActualHeight);
        var halfH = FieldOfView / 2 * Math.PI / 180;
        var halfV = Math.Atan(Math.Tan(halfH) * h / w);
        var distance = radius / Math.Sin(Math.Min(halfH, halfV)) * 1.18;
        fromDirection.Normalize();
        var position = center + fromDirection * distance;

        var guard = GuardOf(viewport);
        guard.Center = center;
        guard.Radius = radius;
        guard.Fit = distance;
        if (animate && camera.LookDirection.Length > 1e-9 && (camera.Position - position).Length > 1e-6)
        {
            Fly(viewport, camera, guard, center, position, up, distance);
            return;
        }

        guard.Busy = true;
        try
        {
            camera.FieldOfView = FieldOfView;
            camera.Position = position;
            camera.LookDirection = center - position;
            camera.UpDirection = up;
            SetPlanes(camera, guard, distance);
        }
        finally
        {
            guard.Busy = false;
        }
    }

    /// <summary>
    /// Transition : la direction de vue tourne (interpolation sphérique), la distance et le point visé glissent vers le
    /// cadrage, l'orientation se redresse ; une étape par image affichée, rien n'est calculé à l'avance.
    /// </summary>
    private static void Fly(HelixViewport3D viewport, PerspectiveCamera camera, Guard guard, Point3D center, Point3D position, Vector3D up, double distance)
    {
        const double seconds = 0.36;
        var flight = new Flight
        {
            Camera = camera, Guard = guard, FromPosition = camera.Position, FromTarget = camera.Position + camera.LookDirection,
            ToPosition = position, Center = center, FromUp = camera.UpDirection, ToUp = up, Distance = distance, Start = DateTime.UtcNow
        };
        var fromDir = flight.FromPosition - center;
        var toDir = position - center;
        var fromLen = fromDir.Length;
        fromDir.Normalize();
        toDir.Normalize();
        var angle = Math.Acos(Math.Clamp(Vector3D.DotProduct(fromDir, toDir), -1, 1));

        flight.Tick = (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - flight.Start).TotalSeconds / seconds);
            var k = t * t * (3 - 2 * t); // départ et arrivée en douceur
            Vector3D dir;
            if (angle < 1e-4)
            {
                dir = toDir;
            }
            else if (Math.PI - angle < 1e-3)
            {
                // Demi-tour : passage par le dessus.
                var mid = new Vector3D(0, 0, 1);
                dir = k < 0.5 ? Slerp(fromDir, mid, k * 2) : Slerp(mid, toDir, (k - 0.5) * 2);
            }
            else
            {
                dir = Slerp(fromDir, toDir, k);
            }

            var length = fromLen + (distance - fromLen) * k;
            var pos = center + dir * length;
            var target = flight.FromTarget + (center - flight.FromTarget) * k;
            var upNow = flight.FromUp * (1 - k) + flight.ToUp * k;
            if (upNow.Length < 1e-6)
            {
                upNow = flight.ToUp;
            }

            guard.Busy = true;
            try
            {
                camera.Position = pos;
                camera.LookDirection = target - pos;
                camera.UpDirection = upNow;
                SetPlanes(camera, guard, length);
            }
            finally
            {
                guard.Busy = false;
            }

            if (t >= 1)
            {
                Stop(viewport);
            }
        };
        Flights.AddOrUpdate(viewport, flight);
        System.Windows.Media.CompositionTarget.Rendering += flight.Tick;
    }

    private static Vector3D Slerp(Vector3D a, Vector3D b, double t)
    {
        var dot = Math.Clamp(Vector3D.DotProduct(a, b), -1, 1);
        var theta = Math.Acos(dot) * t;
        var rel = b - a * dot;
        if (rel.Length < 1e-9)
        {
            return a;
        }

        rel.Normalize();
        return a * Math.Cos(theta) + rel * Math.Sin(theta);
    }

    private static Guard GuardOf(HelixViewport3D viewport)
    {
        if (!Guards.TryGetValue(viewport, out var guard))
        {
            guard = new Guard();
            Guards.Add(viewport, guard);
            viewport.IsChangeFieldOfViewEnabled = false;
            viewport.CameraChanged += (_, _) => Clamp(viewport, guard);
        }

        return guard;
    }

    /// <summary>Ramène la caméra entre la distance minimale et maximale du modèle, en gardant la direction de visée.</summary>
    private static void Clamp(HelixViewport3D viewport, Guard guard)
    {
        if (guard.Busy || guard.Radius <= 0 || viewport.Camera is not PerspectiveCamera camera)
        {
            return;
        }

        guard.Busy = true;
        try
        {
            var offset = camera.Position - guard.Center;
            var d = offset.Length;
            var min = guard.Radius * MinDistance;
            var max = Math.Max(min * 1.5, guard.Fit * MaxDistance);
            var target = Math.Clamp(d, min, max);
            if (Math.Abs(target - d) > 1e-9)
            {
                var look = camera.LookDirection;
                if (look.Length < 1e-12)
                {
                    look = guard.Center - camera.Position;
                }

                look.Normalize();
                if (d < 1e-9)
                {
                    offset = -look;
                }

                offset.Normalize();
                camera.Position = guard.Center + offset * target;

                // Cible (pivot de rotation) devant la caméra, à la profondeur du centre du modèle.
                var reach = Vector3D.DotProduct(guard.Center - camera.Position, look);
                camera.LookDirection = look * Math.Max(reach, guard.Radius * 0.5);
            }

            if (Math.Abs(camera.FieldOfView - FieldOfView) > 1e-6)
            {
                camera.FieldOfView = FieldOfView;
            }

            SetPlanes(camera, guard, target);
        }
        finally
        {
            guard.Busy = false;
        }
    }

    /// <summary>Plans de découpe autour de la sphère du modèle : précision de profondeur maximale, rien de coupé.</summary>
    private static void SetPlanes(PerspectiveCamera camera, Guard guard, double distance)
    {
        var near = Math.Max(0.001, (distance - guard.Radius) * 0.5);
        var far = distance + guard.Radius * 3;
        if (Math.Abs(camera.NearPlaneDistance - near) > 1e-9)
        {
            camera.NearPlaneDistance = near;
        }

        if (Math.Abs(camera.FarPlaneDistance - far) > 1e-9)
        {
            camera.FarPlaneDistance = far;
        }
    }
}
