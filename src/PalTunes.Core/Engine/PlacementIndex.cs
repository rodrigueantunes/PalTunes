using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

/// <summary>
/// Index spatial des produits placés (grille régulière) : chaque produit n'est comparé qu'à ses voisins, au lieu de
/// tous les autres. Indispensable pour les petits produits (bagues, bouchons…) : des dizaines de milliers par palette.
/// </summary>
public sealed class PlacementGrid
{
    private readonly double _cellXY;
    private readonly double _cellZ;
    private readonly bool _useZ;
    private readonly Dictionary<(int, int, int), List<int>> _cells = new();
    private readonly IReadOnlyList<Placement> _items;

    /// <param name="useZ">true : grille 3D (chevauchements) ; false : grille du plan XY (appuis d'un même niveau).</param>
    public PlacementGrid(IReadOnlyList<Placement> items, bool useZ)
    {
        _items = items;
        _useZ = useZ;
        _cellXY = CellSize(items.Select(p => Math.Max(p.DX, p.DY)));
        _cellZ = useZ ? CellSize(items.Select(p => p.DZ)) : 1;
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var key in Keys(items[i]))
            {
                if (!_cells.TryGetValue(key, out var list))
                {
                    _cells[key] = list = [];
                }

                list.Add(i);
            }
        }
    }

    /// <summary>Taille de maille : dimension médiane des produits (au moins 1 mm).</summary>
    private static double CellSize(IEnumerable<double> sizes)
    {
        var sorted = sizes.Where(s => s > 0).OrderBy(s => s).ToList();
        return sorted.Count == 0 ? 100 : Math.Max(1, sorted[sorted.Count / 2]);
    }

    private IEnumerable<(int, int, int)> Keys(Placement p)
    {
        int X(double v) => (int)Math.Floor(v / _cellXY);
        var x0 = X(p.X + Geometry.Eps);
        var x1 = X(p.MaxX - Geometry.Eps);
        var y0 = X(p.Y + Geometry.Eps);
        var y1 = X(p.MaxY - Geometry.Eps);
        var z0 = _useZ ? (int)Math.Floor((p.Z + Geometry.Eps) / _cellZ) : 0;
        var z1 = _useZ ? (int)Math.Floor((p.MaxZ - Geometry.Eps) / _cellZ) : 0;
        for (var x = x0; x <= Math.Max(x0, x1); x++)
        {
            for (var y = y0; y <= Math.Max(y0, y1); y++)
            {
                for (var z = z0; z <= Math.Max(z0, z1); z++)
                {
                    yield return (x, y, z);
                }
            }
        }
    }

    /// <summary>Indices des produits qui partagent une maille avec <paramref name="p"/> (sans doublon).</summary>
    public IEnumerable<int> Near(Placement p)
    {
        var seen = new HashSet<int>();
        foreach (var key in Keys(p))
        {
            if (_cells.TryGetValue(key, out var list))
            {
                foreach (var i in list)
                {
                    if (seen.Add(i))
                    {
                        yield return i;
                    }
                }
            }
        }
    }

    public Placement this[int index] => _items[index];
}

/// <summary>Produits regroupés par cote de dessus (à 1 mm près), avec une grille XY par niveau : qui porte qui.</summary>
public sealed class TopIndex
{
    private readonly Dictionary<long, PlacementGrid> _levels;

    public TopIndex(IEnumerable<Placement> items) =>
        _levels = items.GroupBy(p => (long)Math.Round(p.MaxZ))
            .ToDictionary(g => g.Key, g => new PlacementGrid(g.ToList(), useZ: false));

    /// <summary>Produits dont le dessus est à la cote de la base de <paramref name="p"/> et dont l'emprise peut la recouvrir.</summary>
    public IEnumerable<Placement> Below(Placement p)
    {
        var z = (long)Math.Round(p.Z);
        for (var key = z - 1; key <= z + 1; key++)
        {
            if (!_levels.TryGetValue(key, out var grid))
            {
                continue;
            }

            foreach (var i in grid.Near(p))
            {
                var q = grid[i];
                if (!ReferenceEquals(p, q) && Math.Abs(q.MaxZ - p.Z) <= Geometry.Eps)
                {
                    yield return q;
                }
            }
        }
    }
}
