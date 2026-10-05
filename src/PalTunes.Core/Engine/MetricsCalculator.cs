using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

/// <summary>Indicateurs d'une unité de charge (étude §6.1, §10.5), recalculés depuis la géométrie seule.</summary>
public static class MetricsCalculator
{
    public static UnitMetrics Compute(LoadUnit unit, BaseInfo baseInfo, PackagingConstraints c)
    {
        var m = new UnitMetrics();
        var items = unit.Items;
        m.ItemCount = items.Count;
        var usableL = baseInfo.Length + 2 * (c.OverhangLength - c.CornerInset);
        var usableW = baseInfo.Width + 2 * (c.OverhangWidth - c.CornerInset);
        if (items.Count == 0)
        {
            m.EnclosureLength = baseInfo.Length;
            m.EnclosureWidth = baseInfo.Width;
            m.EnclosureHeight = baseInfo.PalletHeight;
            m.TotalWeight = baseInfo.Tare;
            return m;
        }

        m.MinX = items.Min(p => p.X);
        m.MinY = items.Min(p => p.Y);
        m.MaxX = items.Max(p => p.MaxX);
        m.MaxY = items.Max(p => p.MaxY);
        m.LoadLength = m.MaxX - m.MinX;
        m.LoadWidth = m.MaxY - m.MinY;
        m.LoadHeight = items.Max(p => p.MaxZ);
        // Cornières et film entourent la charge : ils s'ajoutent à l'encombrement de chaque côté (§10.5).
        var film = c.FilmThickness;
        var t = c.Corners ? c.CornerThickness : 0;
        var (f0, g0, f1, g1) = unit.CornerBounds(m);
        var x0 = Math.Min(0, Math.Min(m.MinX - film, c.Corners ? f0 - t - film : double.MaxValue));
        var x1 = Math.Max(baseInfo.Length, Math.Max(m.MaxX + film, c.Corners ? f1 + t + film : double.MinValue));
        var y0 = Math.Min(0, Math.Min(m.MinY - film, c.Corners ? g0 - t - film : double.MaxValue));
        var y1 = Math.Max(baseInfo.Width, Math.Max(m.MaxY + film, c.Corners ? g1 + t + film : double.MinValue));
        m.EnclosureLength = x1 - x0;
        m.EnclosureWidth = y1 - y0;
        m.EnclosureHeight = baseInfo.PalletHeight + m.LoadHeight + c.CapHeight;
        m.LoadWeight = items.Sum(p => p.Weight);
        m.SlipSheetCount = unit.Layers.Count(l => l.SlipSheetBelow);
        m.AccessoriesWeight = m.SlipSheetCount * c.SlipSheetWeight + (c.CapHeight > 0 ? c.CapWeight : 0) + (c.Corners ? 4 * c.CornerWeight : 0);
        m.TotalWeight = m.LoadWeight + m.AccessoriesWeight + baseInfo.Tare;

        var volume = items.Sum(Geometry.Volume);
        var usableArea = Math.Max(1, usableL * usableW);
        m.FillRate = m.LoadHeight > 0 ? Math.Min(100, volume / (usableArea * m.LoadHeight) * 100) : 0;
        m.AreaRate = Math.Min(100, items.Where(p => p.Z <= Geometry.Eps).Sum(Geometry.FootprintArea) / usableArea * 100);

        // Supports : produits dont le dessus est exactement à la cote de la base du produit (§6.1).
        var tops = new TopIndex(items);
        double supportSum = 0, supportMin = 1;
        int above = 0, interlocked = 0;
        foreach (var p in items)
        {
            double ratio;
            if (p.Z <= Geometry.Eps || p.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY)
            {
                ratio = 1;
            }
            else
            {
                above++;
                var area = p.DX * p.DY;
                double covered = 0;
                var supporters = 0;
                foreach (var q in tops.Below(p))
                {
                    var o = Geometry.SupportShare(p, q);
                    if (o > 0)
                    {
                        covered += o;
                        if (o > area * 0.05)
                        {
                            supporters++;
                        }
                    }
                }

                ratio = Math.Min(1, covered / area);
                if (supporters >= 2)
                {
                    interlocked++;
                }
            }

            supportSum += ratio;
            supportMin = Math.Min(supportMin, ratio);
        }

        m.SupportAvg = supportSum / items.Count * 100;
        m.SupportMin = supportMin * 100;
        m.Interlock = above > 0 ? interlocked * 100.0 / above : 0;

        var totalWeight = Math.Max(1e-9, m.LoadWeight);
        var weighted = m.LoadWeight > 0;
        double W(Placement p) => weighted ? p.Weight : 1;
        var norm = weighted ? totalWeight : items.Count;
        var cx = items.Sum(p => (p.X + p.DX / 2) * W(p)) / norm;
        var cy = items.Sum(p => (p.Y + p.DY / 2) * W(p)) / norm;
        var cz = items.Sum(p => (p.Z + p.DZ / 2) * W(p)) / norm;
        m.CogOffsetX = Math.Abs(cx - baseInfo.Length / 2) / (baseInfo.Length / 2) * 100;
        m.CogOffsetY = Math.Abs(cy - baseInfo.Width / 2) / (baseInfo.Width / 2) * 100;
        m.CogHeight = baseInfo.PalletHeight + cz;
        m.Slenderness = m.EnclosureHeight / Math.Max(1, Math.Min(m.EnclosureLength, m.EnclosureWidth));
        m.Homogeneity = ComputeHomogeneity(items);
        return m;
    }

    /// <summary>Part des produits ayant un voisin du même article (face commune), ou seuls de leur article (§11.3).</summary>
    private static double ComputeHomogeneity(List<Placement> items)
    {
        var groups = items.GroupBy(p => p.ArticleId).ToList();
        if (groups.Count <= 1)
        {
            return 100;
        }

        var ok = 0;
        foreach (var g in groups)
        {
            var list = g.ToList();
            if (list.Count == 1)
            {
                ok++;
                continue;
            }

            // Voisins par la grille spatiale (produit élargi du jeu de contact) : linéaire même avec des milliers de produits.
            var grid = new PlacementGrid(list, useZ: true);
            foreach (var p in list)
            {
                var probe = new Placement { X = p.X - 2, Y = p.Y - 2, Z = p.Z - 2, DX = p.DX + 4, DY = p.DY + 4, DZ = p.DZ + 4 };
                if (grid.Near(probe).Any(i => !ReferenceEquals(p, grid[i]) && Touch(p, grid[i])))
                {
                    ok++;
                }
            }
        }

        return ok * 100.0 / items.Count;
    }

    private static bool Touch(Placement a, Placement b)
    {
        const double t = 2;
        var ox = Geometry.OverlapLength(a.X, a.MaxX, b.X, b.MaxX);
        var oy = Geometry.OverlapLength(a.Y, a.MaxY, b.Y, b.MaxY);
        var oz = Geometry.OverlapLength(a.Z, a.MaxZ, b.Z, b.MaxZ);
        var gx = Math.Max(a.X, b.X) - Math.Min(a.MaxX, b.MaxX);
        var gy = Math.Max(a.Y, b.Y) - Math.Min(a.MaxY, b.MaxY);
        var gz = Math.Max(a.Z, b.Z) - Math.Min(a.MaxZ, b.MaxZ);
        return (gx <= t && gx >= -t && oy > 0 && oz > 0) || (gy <= t && gy >= -t && ox > 0 && oz > 0) || (gz <= t && gz >= -t && ox > 0 && oy > 0);
    }
}
