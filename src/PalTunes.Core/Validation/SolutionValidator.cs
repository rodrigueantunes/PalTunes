using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.Core.Validation;

/// <summary>
/// Contrôle indépendant d'une solution (étude §7.3) : recalcule inclusion, chevauchements, hauteur, poids et supports
/// à partir des seuls placements, sans réutiliser l'état du moteur.
/// </summary>
public static class SolutionValidator
{
    public static List<string> Validate(Solution s, PackagingConstraints c, bool checkSupport)
    {
        var issues = new List<string>();
        var b = s.Base;
        var minX = c.CornerInset - c.OverhangLength - Geometry.Eps;
        var minY = c.CornerInset - c.OverhangWidth - Geometry.Eps;
        var maxX = b.Length + c.OverhangLength - c.CornerInset + Geometry.Eps;
        var maxY = b.Width + c.OverhangWidth - c.CornerInset + Geometry.Eps;
        var maxZ = c.MaxTotalHeight - b.PalletHeight - c.CapHeight + Geometry.Eps;
        var maxWeight = c.MaxLoadWeight > 0 ? c.MaxLoadWeight : b.DynamicCapacity;

        foreach (var unit in s.Units)
        {
            var label = s.Units.Count > 1 ? $"Unité {unit.Index} : " : "";
            var outside = unit.Items.Count(p => p.X < minX || p.Y < minY || p.Z < -Geometry.Eps || p.MaxX > maxX || p.MaxY > maxY);
            if (outside > 0)
            {
                issues.Add($"{label}{outside} produit(s) hors de la surface utile (base + débords).");
            }

            var tooHigh = unit.Items.Count(p => p.MaxZ > maxZ);
            if (tooHigh > 0)
            {
                issues.Add($"{label}{tooHigh} produit(s) au-dessus de la hauteur maximale ({c.MaxTotalHeight:0} mm palette comprise).");
            }

            var weight = unit.Items.Sum(p => p.Weight);
            if (maxWeight > 0 && weight > maxWeight + 1e-6)
            {
                issues.Add($"{label}poids de la charge {weight:0.#} kg > maximum {maxWeight:0.#} kg.");
            }

            if (b.DynamicCapacity > 0 && weight > b.DynamicCapacity + 1e-6)
            {
                issues.Add($"{label}poids {weight:0.#} kg > charge dynamique de la base ({b.DynamicCapacity:0} kg pour {b.PhysicalCount} palette(s)).");
            }

            var overlaps = CountOverlaps(unit.Items);
            if (overlaps > 0)
            {
                issues.Add($"{label}{overlaps} chevauchement(s) entre produits.");
            }

            if (checkSupport)
            {
                var minSupport = c.MinSupportPercent / 100 - 1e-6;
                var tops = new TopIndex(unit.Items);
                var unsupported = unit.Items.Count(p => SupportRatio(p, tops) < minSupport);
                if (unsupported > 0)
                {
                    issues.Add($"{label}{unsupported} produit(s) avec un taux de support < {c.MinSupportPercent:0} %.");
                }
            }
        }

        return issues;
    }

    private static int CountOverlaps(List<Placement> items)
    {
        // Grille 3D : seuls les produits voisins sont comparés, chaque paire une seule fois.
        var grid = new PlacementGrid(items, useZ: true);
        var count = 0;
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var j in grid.Near(items[i]))
            {
                if (j > i && Geometry.Intersects(items[i], items[j]))
                {
                    count++;
                }
            }
        }

        return count;
    }

    internal static double SupportRatio(Placement p, TopIndex tops)
    {
        if (p.Z <= Geometry.Eps || p.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY)
        {
            return 1;
        }

        var area = p.DX * p.DY;
        var covered = tops.Below(p).Sum(q => Geometry.FootprintOverlap(p, q));
        return Math.Min(1, covered / area);
    }

    internal static double SupportRatio(Placement p, List<Placement> items)
    {
        if (p.Z <= Geometry.Eps || p.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY)
        {
            return 1;
        }

        var area = p.DX * p.DY;
        var covered = items.Where(q => !ReferenceEquals(p, q) && Math.Abs(q.MaxZ - p.Z) <= Geometry.Eps)
            .Sum(q => Geometry.FootprintOverlap(p, q));
        return Math.Min(1, covered / area);
    }
}
