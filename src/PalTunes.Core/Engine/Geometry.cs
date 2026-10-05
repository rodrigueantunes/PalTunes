using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

internal static class Geometry
{
    public const double Eps = 0.5;

    public static double OverlapLength(double a0, double a1, double b0, double b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    /// <summary>Surface de recouvrement des empreintes au sol de deux placements.</summary>
    public static double FootprintOverlap(Placement a, Placement b) =>
        OverlapLength(a.X, a.MaxX, b.X, b.MaxX) * OverlapLength(a.Y, a.MaxY, b.Y, b.MaxY);

    /// <summary>
    /// Surface d'appui réelle : intersection des deux disques pour deux cylindres debout (leurs carrés englobants se
    /// recouvrent en quinconce sans contact réel), recouvrement des empreintes sinon.
    /// </summary>
    public static double ContactArea(Placement top, Placement below)
    {
        if (top.Shape != ShapeKind.CylinderZ || below.Shape != ShapeKind.CylinderZ)
        {
            return FootprintOverlap(top, below);
        }

        var r1 = Math.Min(top.DX, top.DY) / 2;
        var r2 = Math.Min(below.DX, below.DY) / 2;
        var dx = top.X + top.DX / 2 - (below.X + below.DX / 2);
        var dy = top.Y + top.DY / 2 - (below.Y + below.DY / 2);
        var d = Math.Sqrt(dx * dx + dy * dy);
        if (d >= r1 + r2)
        {
            return 0;
        }

        if (d <= Math.Abs(r1 - r2))
        {
            var r = Math.Min(r1, r2);
            return Math.PI * r * r;
        }

        var a1 = r1 * r1 * Math.Acos(Math.Clamp((d * d + r1 * r1 - r2 * r2) / (2 * d * r1), -1, 1));
        var a2 = r2 * r2 * Math.Acos(Math.Clamp((d * d + r2 * r2 - r1 * r1) / (2 * d * r2), -1, 1));
        var k = 0.5 * Math.Sqrt(Math.Max(0, (-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2)));
        return a1 + a2 - k;
    }

    /// <summary>
    /// Part de l'empreinte du produit du dessus portée par celui du dessous, exprimée en surface d'empreinte
    /// rectangulaire (rapportée à DX × DY) : disques pour deux cylindres debout, recouvrement des empreintes sinon.
    /// Même règle pour le moteur, le contrôle indépendant et les indicateurs.
    /// </summary>
    public static double SupportShare(Placement top, Placement below) =>
        top.Shape == ShapeKind.CylinderZ && below.Shape == ShapeKind.CylinderZ
            ? ContactArea(top, below) * 4 / Math.PI
            : FootprintOverlap(top, below);

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
