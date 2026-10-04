using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace PalTunes.App.Views.Controls;

/// <summary>
/// Cadrage des vues 3D (Z vers le haut) : la sphère englobant le modèle est ajustée au champ de vision
/// horizontal et vertical du viewport, sans dépendre d'un calcul différé.
/// </summary>
public static class CameraHelper
{
    private const double FieldOfView = 30;

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
        if (viewport.Camera is not PerspectiveCamera camera || model == null || model.Bounds.IsEmpty)
        {
            return;
        }

        var b = model.Bounds;
        var center = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
        var radius = Math.Max(0.02, Math.Sqrt(b.SizeX * b.SizeX + b.SizeY * b.SizeY + b.SizeZ * b.SizeZ) / 2);
        var w = Math.Max(1, viewport.ActualWidth);
        var h = Math.Max(1, viewport.ActualHeight);
        var halfH = FieldOfView / 2 * Math.PI / 180;
        var halfV = Math.Atan(Math.Tan(halfH) * h / w);
        var distance = radius / Math.Sin(Math.Min(halfH, halfV)) * 1.18;
        fromDirection.Normalize();
        var position = center + fromDirection * distance;
        camera.FieldOfView = FieldOfView;
        camera.Position = position;
        camera.LookDirection = center - position;
        camera.UpDirection = up;
        camera.NearPlaneDistance = Math.Max(0.001, distance - radius * 2);
        camera.FarPlaneDistance = distance + radius * 4;
    }
}
