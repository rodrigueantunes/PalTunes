namespace PalTunes.Core.Engine;

/// <summary>Rectangle d'un plan de couche (coin bas-gauche, dimensions selon X et Y), en mm.</summary>
public readonly record struct Rect2(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Top => Y + H;
}

/// <summary>Plan de couche 2D : rectangles (ou enveloppes de cercles) dans un repère local partant de (0, 0).</summary>
public sealed class LayerPattern
{
    public required string Kind { get; init; }
    public required List<Rect2> Items { get; init; }
    public bool Circles { get; init; }
    public int Count => Items.Count;
    public double UsedX => Items.Count == 0 ? 0 : Items.Max(r => r.Right);
    public double UsedY => Items.Count == 0 ? 0 : Items.Max(r => r.Top);

    /// <summary>Nombre de changements d'orientation (pavés tournés de 90° par rapport au premier).</summary>
    public int RotatedCount(double a) => Items.Count(r => Math.Abs(r.W - a) > 0.01);
}

public sealed class RectLayerResult
{
    public required LayerPattern Best { get; init; }
    public required LayerPattern Grid { get; init; }
    public int UpperBound { get; init; }
    public int GuillotineCount { get; init; }
    public int PinwheelCount { get; init; }
}

/// <summary>
/// Plans de couche pour rectangles identiques a × b (rotation 90° autorisée) dans X × Y (étude §4.3–4.4) :
/// grille simple, guillotine optimale par programmation dynamique sur les points de discrétisation
/// (Herz 1972, Christofides &amp; Whitlock 1977, Beasley 1985), moulinet non-guillotine à 5 blocs
/// (Smith &amp; De Cani 1980, Bischoff &amp; Dowsland 1982, structure G4 de Scheithauer &amp; Terno 1996), récursif quand
/// la taille le permet — chaque bloc peut lui-même être un moulinet (Morabito &amp; Morales 1998) —, et borne d'aire sur
/// dimensions efficaces (Barnes 1979, Dowsland 1987).
/// Calcul en entiers (dixièmes de mm) pour des égalités exactes.
/// </summary>
public static class RectLayerSolver
{
    private const double Unit = 10;

    /// <summary>Au-delà, le moulinet (O(n⁴)) n'est pas énuméré : les plans guillotine sont alors quasi optimaux.</summary>
    private const long PinwheelBudget = 30_000_000;

    /// <summary>
    /// Moulinet récursif (5 blocs dans chaque sous-rectangle) : coût de l'ordre de nx³·ny³ / 9 opérations ; au-delà,
    /// seul le moulinet du plan entier est cherché.
    /// </summary>
    private const double RecursiveBudget = 250_000_000;

    /// <summary>Moulinets récursifs activés (diagnostic : comparaison avec le seul moulinet du plan entier, avant 0.1.5).</summary>
    public static bool RecursivePinwheels { get; set; } = true;

    public static RectLayerResult Solve(double containerX, double containerY, double a, double b, double gap = 0)
    {
        // Jeu entre produits : on agrandit produits et contenant du jeu (le dernier produit n'a pas de jeu extérieur).
        var X = (int)Math.Floor((containerX + gap) * Unit + 1e-6);
        var Y = (int)Math.Floor((containerY + gap) * Unit + 1e-6);
        var ia = (int)Math.Round((a + gap) * Unit);
        var ib = (int)Math.Round((b + gap) * Unit);
        var empty = new LayerPattern { Kind = "Aucun", Items = [] };
        if (X <= 0 || Y <= 0 || ia <= 0 || ib <= 0 || (Math.Min(ia, ib) > Math.Max(X, Y)) ||
            !((ia <= X && ib <= Y) || (ib <= X && ia <= Y)))
        {
            return new RectLayerResult { Best = empty, Grid = empty };
        }

        var scale = 1 / Unit;
        LayerPattern ToPattern(string kind, List<(int x, int y, int w, int h)> cells) => new()
        {
            Kind = kind,
            Items = cells.Select(c => new Rect2(c.x * scale, c.y * scale, c.w * scale - gap, c.h * scale - gap)).ToList()
        };

        // Très petits produits : la programmation dynamique (O(n³) sur les points de discrétisation) devient prohibitive
        // alors que deux blocs d'orientations opposées sont à une rangée près de l'optimum.
        var nx = RasterCount(X, ia, ib);
        var ny = RasterCount(Y, ia, ib);
        if ((double)nx * ny * (nx + ny) > DpBudget)
        {
            var cells = TwoBlocks(X, Y, ia, ib);
            var bound = (int)((long)X * Y / ((long)ia * ib));
            var plan = ToPattern("Blocs", cells);
            var simple = ToPattern("Grille", Grid(X, Y, ia, ib));
            return new RectLayerResult { Best = plan.Count >= simple.Count ? plan : simple, Grid = simple, UpperBound = bound, GuillotineCount = plan.Count };
        }

        var solver = new Instance(X, Y, ia, ib);
        solver.Run();

        var grid = ToPattern("Grille", solver.BestGrid());
        var table = ToPattern(solver.HasPinwheel ? "Moulinet" : solver.GuillotineIsGrid ? "Grille" : "Guillotine", solver.BuildGuillotine());
        var best = table;
        if (solver.PinwheelValue > table.Count)
        {
            best = ToPattern("Moulinet", solver.BuildPinwheel());
        }

        return new RectLayerResult
        {
            Best = best,
            Grid = grid,
            UpperBound = solver.UpperBound,
            GuillotineCount = solver.GuillotineValue,
            PinwheelCount = solver.HasPinwheel ? table.Count : Math.Max(solver.PinwheelValue, 0)
        };
    }

    /// <summary>Au-delà (opérations de la programmation dynamique), plan à deux blocs (très petits produits).</summary>
    private const double DpBudget = 400_000_000;

    /// <summary>Nombre de points de discrétisation i·a + j·b ≤ L.</summary>
    private static int RasterCount(int length, int a, int b)
    {
        var reach = new bool[length + 1];
        reach[0] = true;
        var count = 0;
        for (var v = 0; v <= length; v++)
        {
            if (!reach[v])
            {
                continue;
            }

            count++;
            if (v + a <= length)
            {
                reach[v + a] = true;
            }

            if (v + b <= length)
            {
                reach[v + b] = true;
            }
        }

        return count;
    }

    /// <summary>Grille simple, meilleure des deux orientations.</summary>
    private static List<(int x, int y, int w, int h)> Grid(int X, int Y, int a, int b)
    {
        var (w, h) = (X / a) * (Y / b) >= (X / b) * (Y / a) ? (a, b) : (b, a);
        var cells = new List<(int, int, int, int)>();
        for (var i = 0; i + w <= X; i += w)
        {
            for (var j = 0; j + h <= Y; j += h)
            {
                cells.Add((i, j, w, h));
            }
        }

        return cells;
    }

    /// <summary>
    /// Deux blocs d'orientations opposées, coupés selon X ou selon Y à la meilleure position (plans de type « bloc »
    /// de Smith &amp; De Cani) : à une rangée près de l'optimum pour de très petits produits.
    /// </summary>
    private static List<(int x, int y, int w, int h)> TwoBlocks(int X, int Y, int a, int b)
    {
        static int Fit(int w, int h, int p, int q) => (w / p) * (h / q);
        var best = (count: -1, alongX: true, cut: 0, w1: a, h1: b);
        foreach (var (p, q) in new[] { (a, b), (b, a) })
        {
            for (var cut = 0; cut <= X; cut += p)
            {
                var n = Fit(cut, Y, p, q) + Fit(X - cut, Y, q, p);
                if (n > best.count)
                {
                    best = (n, true, cut, p, q);
                }
            }

            for (var cut = 0; cut <= Y; cut += q)
            {
                var n = Fit(X, cut, p, q) + Fit(X, Y - cut, q, p);
                if (n > best.count)
                {
                    best = (n, false, cut, p, q);
                }
            }
        }

        var cells = new List<(int, int, int, int)>();
        void Block(int x0, int y0, int w, int h, int p, int q)
        {
            for (var i = 0; i + p <= w; i += p)
            {
                for (var j = 0; j + q <= h; j += q)
                {
                    cells.Add((x0 + i, y0 + j, p, q));
                }
            }
        }

        var (_, alongX, c, w1, h1) = best;
        if (alongX)
        {
            Block(0, 0, c, Y, w1, h1);
            Block(c, 0, X - c, Y, h1, w1);
        }
        else
        {
            Block(0, 0, X, c, w1, h1);
            Block(0, c, X, Y - c, h1, w1);
        }

        return cells;
    }

    private sealed class Instance(int X, int Y, int a, int b)
    {
        private int[] _rx = [];
        private int[] _ry = [];
        private int[] _normX = [];
        private int[] _normY = [];
        private int[,] _f = new int[0, 0];

        // Choix : 0 = grille a×b, 1 = grille b×a, 2 = coupe verticale, 3 = coupe horizontale, 4 = moulinet ;
        // _cut = indice de coupe ; _pins = moulinet retenu pour un sous-rectangle.
        private byte[,] _kind = new byte[0, 0];
        private int[,] _cut = new int[0, 0];
        private readonly Dictionary<(int, int), (int x1, int x2, int y1, int y2, bool chiral)> _pins = [];

        public int PinwheelValue { get; private set; } = -1;
        private (int x1, int x2, int y1, int y2, bool chiral) _pin;
        public int UpperBound { get; private set; }
        public bool GuillotineIsGrid => _kind[_rx.Length - 1, _ry.Length - 1] <= 1;

        /// <summary>Meilleur plan guillotine du plan entier (avant moulinets récursifs).</summary>
        public int GuillotineValue { get; private set; }

        /// <summary>Le plan retenu contient un moulinet (à un niveau quelconque).</summary>
        public bool HasPinwheel { get; private set; }

        public int Value => _f[_rx.Length - 1, _ry.Length - 1];

        public void Run()
        {
            (_rx, _normX) = Raster(X);
            (_ry, _normY) = Raster(Y);
            var nx = _rx.Length;
            var ny = _ry.Length;
            _f = new int[nx, ny];
            _kind = new byte[nx, ny];
            _cut = new int[nx, ny];

            // 1re passe : guillotine optimale. Si elle n'atteint pas la borne, 2e passe avec les moulinets dans chaque
            // sous-rectangle (récursifs : les blocs d'un moulinet sont eux-mêmes guillotine ou moulinet).
            Pass(withPinwheels: false);
            UpperBound = (int)((long)_rx[nx - 1] * _ry[ny - 1] / ((long)a * b));
            GuillotineValue = _f[nx - 1, ny - 1];
            if (GuillotineValue >= UpperBound)
            {
                return;
            }

            if (RecursivePinwheels && Math.Pow(nx, 3) * Math.Pow(ny, 3) / 9 <= RecursiveBudget)
            {
                var (f, kind, cut) = ((int[,])_f.Clone(), (byte[,])_kind.Clone(), (int[,])_cut.Clone());
                Pass(withPinwheels: true);
                if (_f[nx - 1, ny - 1] > GuillotineValue)
                {
                    HasPinwheel = true;
                    return;
                }

                // Pas mieux : le plan guillotine (plus simple à poser) est gardé.
                (_f, _kind, _cut) = (f, kind, cut);
                _pins.Clear();
                return;
            }

            if ((long)nx * nx * ny * ny / 2 <= PinwheelBudget)
            {
                SearchPinwheel(GuillotineValue);
            }
        }

        private void Pass(bool withPinwheels)
        {
            var nx = _rx.Length;
            var ny = _ry.Length;
            for (var i = 0; i < nx; i++)
            {
                for (var j = 0; j < ny; j++)
                {
                    var w = _rx[i];
                    var h = _ry[j];
                    var g1 = (w / a) * (h / b);
                    var g2 = (w / b) * (h / a);
                    var best = g1;
                    byte kind = 0;
                    var cut = 0;
                    if (g2 > best)
                    {
                        best = g2;
                        kind = 1;
                    }

                    for (var k = 1; k < i && _rx[k] * 2 <= w; k++)
                    {
                        var v = _f[k, j] + _f[_normX[w - _rx[k]], j];
                        if (v > best)
                        {
                            best = v;
                            kind = 2;
                            cut = k;
                        }
                    }

                    for (var k = 1; k < j && _ry[k] * 2 <= h; k++)
                    {
                        var v = _f[i, k] + _f[i, _normY[h - _ry[k]]];
                        if (v > best)
                        {
                            best = v;
                            kind = 3;
                            cut = k;
                        }
                    }

                    _f[i, j] = best;
                    _kind[i, j] = kind;
                    _cut[i, j] = cut;

                    // Moulinet de ce sous-rectangle : ses 5 blocs sont plus étroits (déjà calculés, moulinets compris).
                    if (withPinwheels && i >= 2 && j >= 2 && best < (int)((long)w * h / ((long)a * b)))
                    {
                        var pin = BestPinwheel(i, j, best);
                        if (pin.Value > best)
                        {
                            _f[i, j] = pin.Value;
                            _kind[i, j] = 4;
                            _pins[(i, j)] = pin.Cut;
                        }
                    }
                }
            }
        }

        private bool UsesPinwheel(int i, int j)
        {
            if (_f[i, j] == 0)
            {
                return false;
            }

            var w = _rx[i];
            var h = _ry[j];
            return _kind[i, j] switch
            {
                4 => true,
                2 => UsesPinwheel(_cut[i, j], j) || UsesPinwheel(_normX[w - _rx[_cut[i, j]]], j),
                3 => UsesPinwheel(i, _cut[i, j]) || UsesPinwheel(i, _normY[h - _ry[_cut[i, j]]]),
                _ => false
            };
        }

        /// <summary>Meilleur moulinet à 5 blocs du sous-rectangle (i, j), blocs évalués par la table (récursivité).</summary>
        private (int Value, (int x1, int x2, int y1, int y2, bool chiral) Cut) BestPinwheel(int i, int j, int floor)
        {
            var w = _rx[i];
            var h = _ry[j];
            var bound = (int)((long)w * h / ((long)a * b));
            var best = floor;
            (int, int, int, int, bool) cut = default;
            for (var i1 = 1; i1 < i; i1++)
            {
                var x1 = _rx[i1];
                for (var i2 = 1; i2 < i; i2++)
                {
                    if (i2 == i1)
                    {
                        continue;
                    }

                    var x2 = _rx[i2];
                    var chiral = x1 < x2;
                    for (var j1 = 1; j1 < j; j1++)
                    {
                        var y1 = _ry[j1];
                        for (var j2 = 1; j2 < j; j2++)
                        {
                            var y2 = _ry[j2];
                            if (j2 == j1 || (chiral ? y2 >= y1 : y1 >= y2))
                            {
                                continue;
                            }

                            var v = F(x1, y1) + F(w - x1, y2) + F(w - x2, h - y2) + F(x2, h - y1) + F(Math.Abs(x2 - x1), Math.Abs(y1 - y2));
                            if (v > best)
                            {
                                best = v;
                                cut = (x1, x2, y1, y2, chiral);
                                if (best >= bound)
                                {
                                    return (best, cut);
                                }
                            }
                        }
                    }
                }
            }

            return (best, cut);
        }

        /// <summary>Points de discrétisation i·a + j·b ≤ L, et table « plus grand point ≤ v ».</summary>
        private (int[] points, int[] norm) Raster(int length)
        {
            var reach = new bool[length + 1];
            reach[0] = true;
            for (var v = 0; v <= length; v++)
            {
                if (!reach[v])
                {
                    continue;
                }

                if (v + a <= length)
                {
                    reach[v + a] = true;
                }

                if (v + b <= length)
                {
                    reach[v + b] = true;
                }
            }

            var points = new List<int>();
            var norm = new int[length + 1];
            for (var v = 0; v <= length; v++)
            {
                if (reach[v])
                {
                    points.Add(v);
                }

                norm[v] = points.Count - 1;
            }

            return (points.ToArray(), norm);
        }

        private int F(int w, int h) => w <= 0 || h <= 0 ? 0 : _f[_normX[w], _normY[h]];

        private void SearchPinwheel(int guillotine)
        {
            var best = guillotine;
            var nx = _rx.Length;
            var ny = _ry.Length;
            for (var i1 = 1; i1 < nx - 1; i1++)
            {
                var x1 = _rx[i1];
                for (var i2 = 1; i2 < nx - 1; i2++)
                {
                    if (i2 == i1)
                    {
                        continue;
                    }

                    var x2 = _rx[i2];
                    for (var j1 = 1; j1 < ny - 1; j1++)
                    {
                        var y1 = _ry[j1];
                        for (var j2 = 1; j2 < ny - 1; j2++)
                        {
                            if (j2 == j1)
                            {
                                continue;
                            }

                            var y2 = _ry[j2];
                            // Chiralité 1 : x1 < x2 et y2 < y1 ; chiralité 2 : x2 < x1 et y1 < y2.
                            var chiral = x1 < x2;
                            if (chiral ? y2 >= y1 : y1 >= y2)
                            {
                                continue;
                            }

                            var v = F(x1, y1) + F(X - x1, y2) + F(X - x2, Y - y2) + F(x2, Y - y1)
                                    + F(Math.Abs(x2 - x1), Math.Abs(y1 - y2));
                            if (v > best)
                            {
                                best = v;
                                _pin = (x1, x2, y1, y2, chiral);
                                PinwheelValue = v;
                            }
                        }
                    }
                }
            }
        }

        public List<(int x, int y, int w, int h)> BestGrid()
        {
            var g1 = (X / a) * (Y / b);
            var g2 = (X / b) * (Y / a);
            var cells = new List<(int, int, int, int)>();
            Fill(cells, 0, 0, X, Y, g1 >= g2 ? 0 : 1);
            return cells;
        }

        public List<(int x, int y, int w, int h)> BuildGuillotine()
        {
            var cells = new List<(int, int, int, int)>();
            Build(cells, _rx.Length - 1, _ry.Length - 1, 0, 0);
            return cells;
        }

        public List<(int x, int y, int w, int h)> BuildPinwheel()
        {
            var cells = new List<(int, int, int, int)>();
            var (x1, x2, y1, y2, chiral) = _pin;
            Block(cells, 0, 0, x1, y1);
            Block(cells, x1, 0, X - x1, y2);
            Block(cells, x2, y2, X - x2, Y - y2);
            Block(cells, 0, y1, x2, Y - y1);
            if (chiral)
            {
                Block(cells, x1, y2, x2 - x1, y1 - y2);
            }
            else
            {
                Block(cells, x2, y1, x1 - x2, y2 - y1);
            }

            return cells;
        }

        private void Block(List<(int, int, int, int)> cells, int ox, int oy, int w, int h)
        {
            if (w > 0 && h > 0)
            {
                Build(cells, _normX[w], _normY[h], ox, oy);
            }
        }

        private void Build(List<(int, int, int, int)> cells, int i, int j, int ox, int oy)
        {
            if (_f[i, j] == 0)
            {
                return;
            }

            var w = _rx[i];
            var h = _ry[j];
            switch (_kind[i, j])
            {
                case 0:
                case 1:
                    Fill(cells, ox, oy, w, h, _kind[i, j]);
                    break;
                case 2:
                    var k = _cut[i, j];
                    Build(cells, k, j, ox, oy);
                    Build(cells, _normX[w - _rx[k]], j, ox + _rx[k], oy);
                    break;
                case 4:
                    var (x1, x2, y1, y2, chiral) = _pins[(i, j)];
                    Block(cells, ox, oy, x1, y1);
                    Block(cells, ox + x1, oy, w - x1, y2);
                    Block(cells, ox + x2, oy + y2, w - x2, h - y2);
                    Block(cells, ox, oy + y1, x2, h - y1);
                    if (chiral)
                    {
                        Block(cells, ox + x1, oy + y2, x2 - x1, y1 - y2);
                    }
                    else
                    {
                        Block(cells, ox + x2, oy + y1, x1 - x2, y2 - y1);
                    }

                    break;
                default:
                    var m = _cut[i, j];
                    Build(cells, i, m, ox, oy);
                    Build(cells, i, _normY[h - _ry[m]], ox, oy + _ry[m]);
                    break;
            }
        }

        private void Fill(List<(int, int, int, int)> cells, int ox, int oy, int w, int h, int orientation)
        {
            var (cw, ch) = orientation == 0 ? (a, b) : (b, a);
            for (var x = 0; x + cw <= w; x += cw)
            {
                for (var y = 0; y + ch <= h; y += ch)
                {
                    cells.Add((ox + x, oy + y, cw, ch));
                }
            }
        }
    }
}
