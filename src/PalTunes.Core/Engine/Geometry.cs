using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

internal static class Geometry
{
    public const double Eps = 0.5;

    public static double OverlapLength(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    /// <summary>Surface de recouvrement des empreintes au sol de deux placements.</summary>
    public static double FootprintOverlap(Placement a, Placement b) =>
        OverlapLength(a.X, a.MaxX, b.X, b.MaxX) * OverlapLength(a.Y, a.MaxY, b.Y, b.MaxY);

    public static double FootprintArea(Placement p) => p.Shape == ShapeKind.CylinderZ
        ? Math.PI * p.DX * p.DY / 4
        : p.DX * p.DY;

    public static double Volume(Placement p) => p.Shape switch
    {
        ShapeKind.CylinderZ => Math.PI * p.DX * p.DY / 4 * p.DZ,
        ShapeKind.CylinderX => Math.PI * p.DY * p.DZ / 4 * p.DX,
        ShapeKind.CylinderY => Math.PI * p.DX * p.DZ / 4 * p.DY,
        _ => p.DX * p.DY * p.DZ
    };

    /// <summary>
    /// Chevauchement réel de deux placements : enveloppes pour les pavés, distance entre axes pour deux cylindres
    /// de même axe (les enveloppes de cylindres en quinconce se recouvrent légitimement).
    /// </summary>
    public static bool Intersects(Placement a, Placement b)
    {
        var ox = OverlapLength(a.X, a.MaxX, b.X, b.MaxX);
        var oy = OverlapLength(a.Y, a.MaxY, b.Y, b.MaxY);
        var oz = OverlapLength(a.Z, a.MaxZ, b.Z, b.MaxZ);
        if (ox <= Eps || oy <= Eps || oz <= Eps)
        {
            return false;
        }

        if (a.Shape == b.Shape && a.Shape != ShapeKind.Box)
        {
            var (u1, v1, r1) = Section(a);
            var (u2, v2, r2) = Section(b);
            var dist = Math.Sqrt((u1 - u2) * (u1 - u2) + (v1 - v2) * (v1 - v2));
            return dist < r1 + r2 - Eps;
        }

        return true;
    }

    /// <summary>Centre et rayon de la section circulaire d'un cylindre dans le plan perpendiculaire à son axe.</summary>
    private static (double u, double v, double r) Section(Placement p) => p.Shape switch
    {
        ShapeKind.CylinderZ => (p.X + p.DX / 2, p.Y + p.DY / 2, Math.Min(p.DX, p.DY) / 2),
        ShapeKind.CylinderX => (p.Y + p.DY / 2, p.Z + p.DZ / 2, Math.Min(p.DY, p.DZ) / 2),
        _ => (p.X + p.DX / 2, p.Z + p.DZ / 2, Math.Min(p.DX, p.DZ) / 2)
    };
}
