using Google.OrTools.Sat;
using PalTunes.Core.Models;

namespace PalTunes.Reference;

/// <summary>
/// Référence exacte du chargement hétérogène de colis (pavés, haut imposé, rotation de 90° au sol) : les colis
/// tiennent-ils dans <c>k</c> palettes (ou caisses) L × l × H, charge maxi par unité ? Modèle CP-SAT : affectation à une
/// unité, positions 3D, non-chevauchement par disjonction des six séparations, gravité (chaque colis repose sur le sol
/// ou sur le dessus d'un autre colis de la même unité, avec recouvrement des empreintes). Cette gravité est plus
/// permissive que celle de PalTunes (appui minimal en %, charges) : un « impossible » prouve donc l'optimalité de PalTunes.
/// </summary>
public static class HeteroReference
{
    public sealed record Item(Article Article, int L, int W, int H, double Weight);

    public enum Verdict { Feasible, Infeasible, Unknown }

    /// <param name="fullSupport">
    /// Gravité stricte : un colis posé sur un autre repose entièrement sur son dessus (empreinte incluse). Une solution
    /// trouvée est alors physiquement réalisable (restriction) ; sans cette option, simple recouvrement (relaxation).
    /// </param>
    public static (Verdict Verdict, List<List<Placement>> Units) Fits(IReadOnlyList<Item> items, int k, int L, int W, int H, double maxWeight, double seconds,
        bool fullSupport = false)
    {
        var n = items.Count;
        var m = new CpModel();
        var assign = new BoolVar[n, k];
        var x = new IntVar[n];
        var y = new IntVar[n];
        var z = new IntVar[n];
        var w = new IntVar[n];
        var d = new IntVar[n];
        for (var i = 0; i < n; i++)
        {
            var it = items[i];
            var lits = new List<ILiteral>();
            for (var b = 0; b < k; b++)
            {
                assign[i, b] = m.NewBoolVar($"a{i}_{b}");
                lits.Add(assign[i, b]);
            }

            m.AddExactlyOne(lits);
            var rot = m.NewBoolVar($"r{i}");
            w[i] = m.NewIntVar(Math.Min(it.L, it.W), Math.Max(it.L, it.W), $"w{i}");
            d[i] = m.NewIntVar(Math.Min(it.L, it.W), Math.Max(it.L, it.W), $"d{i}");
            m.Add(w[i] == it.L).OnlyEnforceIf(rot.Not());
            m.Add(d[i] == it.W).OnlyEnforceIf(rot.Not());
            m.Add(w[i] == it.W).OnlyEnforceIf(rot);
            m.Add(d[i] == it.L).OnlyEnforceIf(rot);
            x[i] = m.NewIntVar(0, L, $"x{i}");
            y[i] = m.NewIntVar(0, W, $"y{i}");
            z[i] = m.NewIntVar(0, H - it.H, $"z{i}");
            m.Add(x[i] + w[i] <= L);
            m.Add(y[i] + d[i] <= W);
        }

        // Symétries : première unité pour le premier colis, unités remplies dans l'ordre.
        m.Add(assign[0, 0] == 1);
        for (var b = 0; b < k; b++)
        {
            var bin = b;
            m.Add(LinearExpr.WeightedSum(Enumerable.Range(0, n).Select(i => assign[i, bin]),
                Enumerable.Range(0, n).Select(i => (long)Math.Round(items[i].Weight * 1000))) <= (long)Math.Min(long.MaxValue / 4, Math.Round(maxWeight * 1000)));
        }

        var same = new BoolVar[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var s = m.NewBoolVar($"s{i}_{j}");
                same[i, j] = same[j, i] = s;
                for (var b = 0; b < k; b++)
                {
                    m.AddBoolOr([assign[i, b].Not(), assign[j, b].Not(), s]);
                    m.AddBoolOr([assign[i, b].Not(), assign[j, b], s.Not()]);
                }

                var sep = new BoolVar[6];
                for (var t = 0; t < 6; t++)
                {
                    sep[t] = m.NewBoolVar($"sep{i}_{j}_{t}");
                }

                m.Add(x[i] + w[i] <= x[j]).OnlyEnforceIf(sep[0]);
                m.Add(x[j] + w[j] <= x[i]).OnlyEnforceIf(sep[1]);
                m.Add(y[i] + d[i] <= y[j]).OnlyEnforceIf(sep[2]);
                m.Add(y[j] + d[j] <= y[i]).OnlyEnforceIf(sep[3]);
                m.Add(z[i] + items[i].H <= z[j]).OnlyEnforceIf(sep[4]);
                m.Add(z[j] + items[j].H <= z[i]).OnlyEnforceIf(sep[5]);
                m.AddBoolOr([s.Not(), sep[0], sep[1], sep[2], sep[3], sep[4], sep[5]]);
            }
        }

        // Gravité : au sol, ou posé sur le dessus d'un colis de la même unité dont l'empreinte recouvre la sienne.
        for (var i = 0; i < n; i++)
        {
            var ground = m.NewBoolVar($"g{i}");
            m.Add(z[i] == 0).OnlyEnforceIf(ground);
            var supports = new List<ILiteral> { ground };
            for (var j = 0; j < n; j++)
            {
                if (j == i)
                {
                    continue;
                }

                var on = m.NewBoolVar($"on{i}_{j}");
                m.AddImplication(on, same[i, j]);
                m.Add(z[i] == z[j] + items[j].H).OnlyEnforceIf(on);
                if (fullSupport)
                {
                    m.Add(x[j] <= x[i]).OnlyEnforceIf(on);
                    m.Add(x[i] + w[i] <= x[j] + w[j]).OnlyEnforceIf(on);
                    m.Add(y[j] <= y[i]).OnlyEnforceIf(on);
                    m.Add(y[i] + d[i] <= y[j] + d[j]).OnlyEnforceIf(on);
                }
                else
                {
                    m.Add(x[i] + 1 <= x[j] + w[j]).OnlyEnforceIf(on);
                    m.Add(x[j] + 1 <= x[i] + w[i]).OnlyEnforceIf(on);
                    m.Add(y[i] + 1 <= y[j] + d[j]).OnlyEnforceIf(on);
                    m.Add(y[j] + 1 <= y[i] + d[i]).OnlyEnforceIf(on);
                }
                supports.Add(on);
            }

            m.AddBoolOr(supports);
        }

        var solver = new CpSolver { StringParameters = $"max_time_in_seconds:{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},num_search_workers:8" };
        var status = solver.Solve(m);
        if (status is CpSolverStatus.Feasible or CpSolverStatus.Optimal)
        {
            var units = Enumerable.Range(0, k).Select(_ => new List<Placement>()).ToList();
            for (var i = 0; i < n; i++)
            {
                var b = Enumerable.Range(0, k).First(b => solver.BooleanValue(assign[i, b]));
                units[b].Add(new Placement
                {
                    ArticleId = items[i].Article.Id, X = solver.Value(x[i]), Y = solver.Value(y[i]), Z = solver.Value(z[i]),
                    DX = solver.Value(w[i]), DY = solver.Value(d[i]), DZ = items[i].H, Shape = ShapeKind.Box, Weight = items[i].Weight
                });
            }

            return (Verdict.Feasible, units);
        }

        return (status == CpSolverStatus.Infeasible ? Verdict.Infeasible : Verdict.Unknown, []);
    }
}
