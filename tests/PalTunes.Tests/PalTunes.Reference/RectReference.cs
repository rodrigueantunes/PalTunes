using Google.OrTools.Sat;
using Google.OrTools.Util;

namespace PalTunes.Reference;

/// <summary>
/// Référence exacte des plans de couche de produits rectangulaires identiques (pallet loading problem) : un plan à
/// <c>n</c> produits existe-t-il dans X × Y (rotation de 90° autorisée) ? Modèle CP-SAT : positions sur les points de
/// discrétisation (combinaisons i·a + j·b, normalisation de Herz : toute solution peut y être ramenée), intervalles à
/// taille selon l'orientation, NoOverlap2D, symétries cassées par ordre lexicographique.
/// </summary>
public static class RectReference
{
    public enum Verdict { Feasible, Infeasible, Unknown }

    public static int[] Raster(int length, int a, int b)
    {
        var reach = new bool[length + 1];
        reach[0] = true;
        for (var v = 0; v <= length; v++)
        {
            if (!reach[v])
            {
                continue;
            }

            if (v + a <= length) reach[v + a] = true;
            if (v + b <= length) reach[v + b] = true;
        }

        return Enumerable.Range(0, length + 1).Where(v => reach[v]).ToArray();
    }

    /// <summary>Borne de Barnes (dimensions efficaces).</summary>
    public static int Barnes(int X, int Y, int a, int b)
    {
        var rx = Raster(X, a, b).Max();
        var ry = Raster(Y, a, b).Max();
        return (int)((long)rx * ry / ((long)a * b));
    }

    public static (Verdict Verdict, List<(int x, int y, int w, int h)> Cells) Fits(int X, int Y, int a, int b, int n, double seconds)
    {
        var model = new CpModel();
        var minSide = Math.Min(a, b);
        var rx = Raster(X - minSide, a, b);
        var ry = Raster(Y - minSide, a, b);
        var xs = new IntVar[n];
        var ys = new IntVar[n];
        var ws = new IntVar[n];
        var hs = new IntVar[n];
        var xi = new IntervalVar[n];
        var yi = new IntervalVar[n];
        var sizes = Domain.FromValues([Math.Min(a, b), Math.Max(a, b)]);
        for (var i = 0; i < n; i++)
        {
            xs[i] = model.NewIntVarFromDomain(Domain.FromValues(rx.Select(v => (long)v).ToArray()), $"x{i}");
            ys[i] = model.NewIntVarFromDomain(Domain.FromValues(ry.Select(v => (long)v).ToArray()), $"y{i}");
            ws[i] = model.NewIntVarFromDomain(sizes, $"w{i}");
            hs[i] = model.NewIntVarFromDomain(sizes, $"h{i}");
            model.Add(ws[i] + hs[i] == a + b);
            var xe = model.NewIntVar(0, X, $"xe{i}");
            var ye = model.NewIntVar(0, Y, $"ye{i}");
            xi[i] = model.NewIntervalVar(xs[i], ws[i], xe, $"ix{i}");
            yi[i] = model.NewIntervalVar(ys[i], hs[i], ye, $"iy{i}");
        }

        var noOverlap = model.AddNoOverlap2D();
        for (var i = 0; i < n; i++)
        {
            noOverlap.AddRectangle(xi[i], yi[i]);
        }
        // Redondant : surface occupée sur chaque bande verticale / horizontale (cumulatifs).
        model.AddCumulative(Y).AddDemands(xi, hs);
        model.AddCumulative(X).AddDemands(yi, ws);
        for (var i = 0; i + 1 < n; i++)
        {
            model.Add(xs[i] * (Y + 1) + ys[i] < xs[i + 1] * (Y + 1) + ys[i + 1]);
        }

        var solver = new CpSolver { StringParameters = $"max_time_in_seconds:{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},num_search_workers:8" };
        var status = solver.Solve(model);
        if (status is CpSolverStatus.Feasible or CpSolverStatus.Optimal)
        {
            var cells = Enumerable.Range(0, n).Select(i => ((int)solver.Value(xs[i]), (int)solver.Value(ys[i]), (int)solver.Value(ws[i]), (int)solver.Value(hs[i]))).ToList();
            return (Verdict.Feasible, cells);
        }

        return (status == CpSolverStatus.Infeasible ? Verdict.Infeasible : Verdict.Unknown, []);
    }
}
