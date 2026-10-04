namespace PalTunes.Core.Engine;

/// <summary>
/// Cercles identiques de diamètre D dans un rectangle X × Y (étude §4.5) : maille carrée et maille hexagonale
/// (rangées décalées de D/2, pas 0,866 D), dans les deux sens. Les enveloppes carrées D × D sont renvoyées.
/// </summary>
public static class CircleLayerSolver
{
    private static readonly double Pitch = Math.Sqrt(3) / 2;

    public sealed record Result(LayerPattern Best, LayerPattern Square, int UpperBound);

    public static Result Solve(double X, double Y, double diameter, double gap = 0)
    {
        var d = diameter + gap;
        var xx = X + gap;
        var yy = Y + gap;
        var empty = new LayerPattern { Kind = "Aucun", Items = [], Circles = true };
        if (diameter <= 0 || d > xx + 1e-6 || d > yy + 1e-6)
        {
            return new Result(empty, empty, 0);
        }

        var square = Square(xx, yy, d, diameter);
        var hexRowsX = Hex(xx, yy, d, diameter, transpose: false);
        var hexRowsY = Hex(yy, xx, d, diameter, transpose: true);
        var best = new[] { square, hexRowsX, hexRowsY }.OrderByDescending(p => p.Count).ThenBy(p => p.Kind == "Carré" ? 0 : 1).First();
        // Borne : densité hexagonale maximale π/(2√3) appliquée à la surface (Thue / Fejes Tóth).
        var bound = (int)Math.Floor(xx * yy * (Math.PI / (2 * Math.Sqrt(3))) / (Math.PI * d * d / 4) + 1e-9);
        return new Result(best, square, Math.Max(bound, best.Count));
    }

    private static LayerPattern Square(double X, double Y, double d, double diameter)
    {
        var nx = (int)Math.Floor(X / d + 1e-9);
        var ny = (int)Math.Floor(Y / d + 1e-9);
        var items = new List<Rect2>();
        for (var i = 0; i < nx; i++)
        {
            for (var j = 0; j < ny; j++)
            {
                items.Add(new Rect2(i * d, j * d, diameter, diameter));
            }
        }

        return new LayerPattern { Kind = "Carré", Items = items, Circles = true };
    }

    /// <summary>Rangées parallèles à l'axe « long » (longueur <paramref name="along"/>), empilées selon « across ».</summary>
    private static LayerPattern Hex(double along, double across, double d, double diameter, bool transpose)
    {
        var n1 = (int)Math.Floor(along / d + 1e-9);
        var n2 = (int)Math.Floor((along - d / 2) / d + 1e-9);
        var rows = 1 + (int)Math.Floor((across - d) / (d * Pitch) + 1e-9);
        var items = new List<Rect2>();
        for (var r = 0; r < rows; r++)
        {
            var offset = r % 2 == 1;
            var n = offset ? n2 : n1;
            for (var i = 0; i < n; i++)
            {
                var u = i * d + (offset ? d / 2 : 0);
                var v = r * d * Pitch;
                items.Add(transpose ? new Rect2(v, u, diameter, diameter) : new Rect2(u, v, diameter, diameter));
            }
        }

        return new LayerPattern { Kind = "Quinconce", Items = items, Circles = true };
    }
}
