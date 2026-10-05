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

    public static void Look(HelixViewport3D viewport, Model3D? model, Vector3D fromDirection, Vector3D up)
    {
        if (viewport.Camera is not PerspectiveCamera camera || model == null)
        {
            return;
        }

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
