namespace PalTunes.Core.Engine;

/// <summary>
/// Cercles identiques de diamètre D dans un rectangle X × Y (étude §4.5) : suite de rangées, chacune alignée
/// (⌊L/D⌋ cercles) ou décalée d'un demi-diamètre (⌊(L − D/2)/D⌋), deux rangées consécutives distantes de D (même
/// décalage, maille carrée) ou de D·√3/2 (décalages opposés, cercles imbriqués, quinconce). La meilleure suite couvre
/// la maille carrée, la quinconce et leurs mélanges (quelques rangées carrées dans une quinconce gagnent souvent une
/// rangée), dans les deux sens. Les enveloppes carrées D × D sont renvoyées.
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
        var rowsX = Rows(xx, yy, d, diameter, transpose: false);
        var rowsY = Rows(yy, xx, d, diameter, transpose: true);
        var best = new[] { square, rowsX, rowsY }.OrderByDescending(p => p.Count).ThenBy(p => p.Kind == "Carré" ? 0 : p.Kind == "Quinconce" ? 1 : 2).First();
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

    /// <summary>
    /// Rangées parallèles à « along », empilées selon « across » : kd pas carrés (D) et kp pas imbriqués (D·√3/2) avec
    /// D + kd·D + kp·p ≤ across. Les pas imbriqués vont par paires (décalée puis de nouveau alignée), le dernier seul
    /// laisse une rangée décalée : ⌈kp/2⌉ rangées décalées au plus court. Meilleure combinaison (kd, kp).
    /// </summary>
    private static LayerPattern Rows(double along, double across, double d, double diameter, bool transpose)
    {
        var n0 = (int)Math.Floor(along / d + 1e-9);
        var n1 = (int)Math.Floor((along - d / 2) / d + 1e-9);
        var p = d * Pitch;
        var (bestCount, bestKd, bestKp) = (-1, 0, 0);
        for (var kp = 0; d + kp * p <= across + 1e-9; kp++)
        {
            var kd = (int)Math.Floor((across - d - kp * p) / d + 1e-9);
            var shifted = (kp + 1) / 2;
            var count = (1 + kd + kp - shifted) * n0 + shifted * n1;
            if (count > bestCount)
            {
                (bestCount, bestKd, bestKp) = (count, kd, kp);
            }
        }

        // Suite des rangées : paires imbriquées (décalée, alignée), puis pas carrés, puis le dernier pas imbriqué impair.
        var steps = new List<(double step, bool toggle)>();
        for (var i = 0; i < bestKp / 2; i++)
        {
            steps.Add((p, true));
            steps.Add((p, true));
        }

        for (var i = 0; i < bestKd; i++)
        {
            steps.Add((d, false));
        }

        if (bestKp % 2 == 1)
        {
            steps.Add((p, true));
        }

        var items = new List<Rect2>();
        var v = 0.0;
        var offset = false;
        void Row()
        {
            var n = offset ? n1 : n0;
            for (var i = 0; i < n; i++)
            {
                var u = i * d + (offset ? d / 2 : 0);
                items.Add(transpose ? new Rect2(v, u, diameter, diameter) : new Rect2(u, v, diameter, diameter));
            }
        }

        Row();
        foreach (var (step, toggle) in steps)
        {
            v += step;
            offset ^= toggle;
            Row();
        }

        var kind = bestKp == 0 ? "Carré" : bestKd == 0 ? "Quinconce" : "Mixte";
        return new LayerPattern { Kind = kind, Items = items, Circles = true };
    }
}
