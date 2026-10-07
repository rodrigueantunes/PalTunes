using System.Diagnostics;
using System.Text;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;
using PalTunes.Reference;

// Banc de comparaison : dotnet run -c Release --project tests/PalTunes.Reference -- [rect|circle|hetero|all] [nombre]
var what = args.Length > 0 ? args[0] : "all";
var count = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 400;
var report = new StringBuilder();

if (what is "rect" or "all")
{
    RunRect(count);
}

if (what is "circle" or "all")
{
    RunCircles();
}

if (what is "hetero" or "all")
{
    RunHetero(count);
}

if (what == "layers")
{
    // dotnet run -- layers 1200 800 600x400 365x245 ... : plan PalTunes et borne de Barnes (comparaison aux calculateurs).
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    var X = double.Parse(args[1], inv);
    var Y = double.Parse(args[2], inv);
    foreach (var c in args.Skip(3))
    {
        var p = c.Split('x').Select(v => double.Parse(v, inv)).ToArray();
        var r = RectLayerSolver.Solve(X, Y, p[0], p[1]);
        int T(double v) => (int)Math.Round(v * 10);
        report.AppendLine($"{c}={r.Best.Count} (borne {RectReference.Barnes(T(X), T(Y), T(p[0]), T(p[1]))}, {r.Best.Kind})");
    }
}

if (what == "fits")
{
    // dotnet run -- fits X Y a b n secondes : un plan à n produits existe-t-il ?
    var v = args.Skip(1).Select(int.Parse).ToArray();
    var (verdict, _) = RectReference.Fits(v[0], v[1], v[2], v[3], v[4], v[5]);
    report.AppendLine($"{v[0]}x{v[1]} {v[2]}x{v[3]} n={v[4]} : {verdict}");
}

if (what == "debug")
{
    var a = new Article { Code = "A", Kind = ArticleKind.Caisse, Length = 294, Width = 238, Height = 151, Weight = 3 };
    var b = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 171, Width = 132, Height = 144, Weight = 2 };
    var spec = new CaseSpec { InnerLength = 590, InnerWidth = 390, InnerHeight = 290, WallThickness = 5, Tare = 0.5 };
    var r = CaseEngine.SolveMixed([(a, 12), (b, 10)], spec);
    foreach (var s in r.Solutions)
    {
        report.AppendLine($"{s.Title} rec={s.Recommended} : " + string.Join(" | ", s.Units.Select(u => string.Join(" ", u.Items.GroupBy(p => p.ArticleId == a.Id ? "A" : "B").Select(g => $"{g.Count()}{g.Key}")))));
    }
}

if (what == "recheck")
{
    Recheck(count);
}

File.WriteAllText(Path.Combine(AppContext.BaseDirectory, $"reference-{what}.md"), report.ToString());
Console.WriteLine(report);

// Cas indécis du dernier passage (table « ? » de reference-rect.md), revus avec plus de temps par cas.
void Recheck(int seconds)
{
    var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "reference-rect.md")).Where(l => l.Contains("| ? |")).ToList();
    report.AppendLine($"## Cas indécis revus avec {seconds} s par cas");
    report.AppendLine();
    report.AppendLine("| Palette | Produit | PalTunes | CP-SAT | Borne | Verdict |");
    report.AppendLine("|---|---|---|---|---|---|");
    foreach (var l in lines)
    {
        var c = l.Split('|', StringSplitOptions.TrimEntries);
        var pal = c[2].Split('×', StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        var box = c[3].Split('×', StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        var engine = int.Parse(c[4]);
        var bound = int.Parse(c[6]);
        var (v, _) = RectReference.Fits(pal[0], pal[1], box[0], box[1], engine + 1, seconds);
        var verdict = v switch
        {
            RectReference.Verdict.Infeasible => "PalTunes optimal (prouvé)",
            RectReference.Verdict.Feasible => "écart : CP-SAT place " + (engine + 1),
            _ => "indécis"
        };
        report.AppendLine($"| {c[2]} | {c[3]} | {engine} | {(v == RectReference.Verdict.Feasible ? engine + 1 : v == RectReference.Verdict.Infeasible ? engine : "?")} | {bound} | {verdict} |");
        Console.WriteLine($"{c[2]} {c[3]} : {verdict}");
    }
}

void RunCircles()
{
    var pallets = new[] { (1200.0, 800.0), (1200.0, 1000.0), (1140.0, 1140.0), (800.0, 600.0), (590.0, 390.0), (390.0, 290.0) };
    int total = 0, equal = 0, worse = 0, better = 0;
    var rows = new List<string>();
    foreach (var (X, Y) in pallets)
    {
        for (var d = 30.0; d <= Math.Min(X, Y); d += 7)
        {
            total++;
            var pattern0 = CircleLayerSolver.Solve(X, Y, d).Best;
            var engine = pattern0.Count;
            // Contrôle géométrique indépendant : disques dans la base, centres à au moins un diamètre l'un de l'autre.
            var c = pattern0.Items.Select(r => (x: r.X + r.W / 2, y: r.Y + r.H / 2)).ToList();
            if (c.Any(p => p.x - d / 2 < -1e-6 || p.y - d / 2 < -1e-6 || p.x + d / 2 > X + 1e-6 || p.y + d / 2 > Y + 1e-6))
            {
                throw new InvalidOperationException($"cercle hors base {X}x{Y} Ø{d}");
            }

            for (var i = 0; i < c.Count; i++)
            {
                for (var k = i + 1; k < c.Count; k++)
                {
                    if (Math.Pow(c[i].x - c[k].x, 2) + Math.Pow(c[i].y - c[k].y, 2) < d * d - 1e-6)
                    {
                        throw new InvalidOperationException($"chevauchement {X}x{Y} Ø{d}");
                    }
                }
            }

            var (reference, pattern) = CircleReference.Best(X, Y, d);
            if (engine == reference)
            {
                equal++;
            }
            else if (engine < reference)
            {
                worse++;
                if (rows.Count < 40)
                {
                    rows.Add($"| {X} × {Y} | Ø{d} | {engine} | **{reference}** | {pattern} |");
                }
            }
            else
            {
                better++;
            }
        }
    }

    report.AppendLine("## Produits ronds (fûts, bobines et tubes debout, seaux, bouteilles) — PalTunes contre mailles mixtes");
    report.AppendLine();
    report.AppendLine($"{total} cas : {equal} identiques, {worse} où la référence place plus, {better} où PalTunes place plus.");
    report.AppendLine();
    if (rows.Count > 0)
    {
        report.AppendLine("| Base | Diamètre | PalTunes | Référence | Maille |");
        report.AppendLine("|---|---|---|---|---|");
        rows.ForEach(r => report.AppendLine(r));
        report.AppendLine();
    }
}

void RunHetero(int n)
{
    var rnd = new Random(7);
    var pallet = new PalTunes.Core.Models.PalletType { Code = "EUR1", Name = "EUR", Length = 1200, Width = 800, Height = 144, Tare = 25, DynamicLoad = 100000, StaticLoad = 400000 };
    foreach (var (label, L, W, H, isCase) in new[] { ("Palette 1200 × 800, charge 1000 mm", 1200, 800, 1000, false), ("Caisse intérieure 590 × 390 × 290", 590, 390, 290, true) })
    {
        int total = 0, byBound = 0, byCp = 0, gaps = 0, physical = 0, unknown = 0;
        var rows = new List<string>();
        var sw = Stopwatch.StartNew();
        var spec = new CaseSpec { InnerLength = L, InnerWidth = W, InnerHeight = H, WallThickness = 5, Tare = 0.5 };
        for (var t = 0; t < n; t++)
        {
            var types = rnd.Next(2, 5);
            var arts = new List<(PalTunes.Core.Models.Article Article, int Quantity)>();
            for (var k = 0; k < types; k++)
            {
                var l = isCase ? rnd.Next(80, 301) : rnd.Next(150, 601);
                var w = isCase ? rnd.Next(60, l + 1) : rnd.Next(100, l + 1);
                var h = isCase ? rnd.Next(50, 201) : rnd.Next(100, 401);
                arts.Add((new PalTunes.Core.Models.Article { Code = $"A{k}", Kind = PalTunes.Core.Models.ArticleKind.Caisse, Length = l, Width = w, Height = h, Weight = rnd.Next(1, 6) }, 0));
            }

            // Quantités : volume total entre 1,1 et 2,4 unités, 24 colis au plus.
            var target = (1.1 + rnd.NextDouble() * 1.3) * L * W * H;
            var vol = 0.0;
            var qty = new int[types];
            var guard = 0;
            while (vol < target && qty.Sum() < 24 && guard++ < 200)
            {
                var k = rnd.Next(types);
                qty[k]++;
                vol += arts[k].Article.Length * arts[k].Article.Width * arts[k].Article.Height;
            }

            var lines = arts.Select((a, k) => (a.Article, qty[k])).Where(x => x.Item2 > 0).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var constraints = isCase ? CaseEngine.CaseConstraints(spec) : new PackagingConstraints { MaxTotalHeight = 144 + H };
            var baseInfo = isCase ? CaseEngine.CaseBase(spec) : BaseInfo.From(pallet, false, 1, 1);
            var r = isCase ? CaseEngine.SolveMixed(lines, spec) : HeterogeneousEngine.Solve(lines, baseInfo, constraints);
            var s = r.Recommended;
            if (s == null || s.UnplacedItems > 0 || s.ExcludedArticles.Count > 0)
            {
                continue;
            }

            total++;
            var engine = s.Units.Count;
            var lb = Math.Max(1, (int)Math.Ceiling(lines.Sum(x => x.Article.Length * x.Article.Width * x.Article.Height * x.Item2) / ((double)L * W * H) - 1e-9));
            if (engine == lb)
            {
                byBound++;
                continue;
            }

            var items = lines.SelectMany(x => Enumerable.Repeat(new HeteroReference.Item(x.Article, (int)x.Article.Length, (int)x.Article.Width, (int)x.Article.Height, x.Article.Weight), x.Item2)).ToList();
            var (verdict, units) = HeteroReference.Fits(items, engine - 1, L, W, H, 1e9, 45);
            var desc = string.Join(" + ", lines.Select(x => $"{x.Item2} × {x.Article.Length:0}×{x.Article.Width:0}×{x.Article.Height:0}"));
            switch (verdict)
            {
                case HeteroReference.Verdict.Infeasible:
                    byCp++;
                    break;
                case HeteroReference.Verdict.Feasible:
                    var cpSolution = new Solution { Kind = PackagingKind.Heterogene, Base = baseInfo, Units = units.Select((u, i) => new LoadUnit { Index = i + 1, Items = u }).ToList() };
                    var issues = PalTunes.Core.Validation.SolutionValidator.Validate(cpSolution, constraints, checkSupport: true);
                    if (issues.Count == 0)
                    {
                        gaps++;
                        rows.Add($"| {desc} | {engine} | **{engine - 1}** | écart réel (solution CP-SAT conforme) |");
                    }
                    else
                    {
                        // Recouvrement seul : la solution flotte. Gravité stricte (appui total sur un colis) : réalisable ?
                        var strict = StrictCheck(items, engine - 1, L, W, H, baseInfo, constraints);
                        if (strict.Gap)
                        {
                            gaps++;
                            rows.Add($"| {desc} | {engine} | **{engine - 1}** | écart réel (appui total, solution conforme) |");
                        }
                        else
                        {
                            physical++;
                            rows.Add($"| {desc} | {engine} | {engine - 1} sans appui conforme | {strict.Note} |");
                        }
                    }

                    break;
                default:
                    var strict2 = StrictCheck(items, engine - 1, L, W, H, baseInfo, constraints);
                    if (strict2.Gap)
                    {
                        gaps++;
                        rows.Add($"| {desc} | {engine} | **{engine - 1}** | écart réel (appui total, solution conforme) |");
                    }
                    else
                    {
                        unknown++;
                        rows.Add($"| {desc} | {engine} | ? | indécis ; {strict2.Note} |");
                    }

                    break;
            }

            Console.Write($"\r{label} : {total} cas");
        }

        report.AppendLine($"## Hétérogène — {label} — PalTunes contre CP-SAT");
        report.AppendLine();
        report.AppendLine($"{total} compositions ({sw.Elapsed.TotalSeconds:0} s) : **{byBound + byCp} optimums prouvés** ({byBound} par la borne de volume, {byCp} par CP-SAT), " +
                          $"{gaps} écarts réels, {physical} écarts seulement sans appui conforme, {unknown} indécis.");
        report.AppendLine();
        if (rows.Count > 0)
        {
            report.AppendLine("| Composition | PalTunes | CP-SAT | Verdict |");
            report.AppendLine("|---|---|---|---|");
            rows.ForEach(x => report.AppendLine(x));
            report.AppendLine();
        }
    }
}

(bool Gap, string Note) StrictCheck(List<HeteroReference.Item> items, int k, int L, int W, int H, BaseInfo baseInfo, PackagingConstraints constraints)
{
    var (v, units) = HeteroReference.Fits(items, k, L, W, H, 1e9, 60, fullSupport: true);
    if (v == HeteroReference.Verdict.Infeasible)
    {
        return (false, "aucune solution à appui total en " + k + " (prouvé)");
    }

    if (v == HeteroReference.Verdict.Unknown)
    {
        return (false, "appui total : indécis (60 s)");
    }

    var s = new Solution { Kind = PackagingKind.Heterogene, Base = baseInfo, Units = units.Select((u, i) => new LoadUnit { Index = i + 1, Items = u }).ToList() };
    var issues = PalTunes.Core.Validation.SolutionValidator.Validate(s, constraints, checkSupport: true);
    return issues.Count == 0 ? (true, "") : (false, "appui total trouvé mais non conforme : " + issues[0]);
}

void RunRect(int n)
{
    var rnd = new Random(20261006);
    var cases = new List<(string Source, int X, int Y, int a, int b)>
    {
        ("N1 (Birgin)", 43, 26, 7, 3), ("N4 (Birgin)", 42, 39, 9, 4), ("(74,46,7,5)", 74, 46, 7, 5), ("(86,52,9,5)", 86, 52, 9, 5),
        ("(95,92,11,8)", 95, 92, 11, 8), ("(40,25,7,3)", 40, 25, 7, 3), ("(52,33,9,4)", 52, 33, 9, 4)
    };
    foreach (var (X, Y) in new[] { (1200, 800), (1200, 1000), (1140, 1140), (800, 600), (1000, 1000) })
    {
        for (var k = 0; k < n / 10; k++)
        {
            var a = rnd.Next(80, 601);
            var b = rnd.Next(60, a + 1);
            cases.Add(($"{X}×{Y}", X, Y, a, b));
        }
    }

    for (var k = 0; k < n / 2; k++)
    {
        var X = rnd.Next(30, 120);
        var Y = rnd.Next(20, X + 1);
        var a = rnd.Next(3, 25);
        var b = rnd.Next(2, a + 1);
        cases.Add(("aléatoire", X, Y, a, b));
    }

    var rows = new List<string>();
    int proved = 0, provedByBound = 0, gaps = 0, unknown = 0, total = 0;
    var sw = Stopwatch.StartNew();
    foreach (var (source, X, Y, a, b) in cases)
    {
        var bound = RectReference.Barnes(X, Y, a, b);
        if (bound < 2 || bound > 70)
        {
            continue;
        }

        total++;
        var engine = RectLayerSolver.Solve(X, Y, a, b).Best.Count;
        if (engine == bound)
        {
            proved++;
            provedByBound++;
            continue;
        }

        var (verdict, _) = RectReference.Fits(X, Y, a, b, engine + 1, 20);
        switch (verdict)
        {
            case RectReference.Verdict.Infeasible:
                proved++;
                break;
            case RectReference.Verdict.Feasible:
                gaps++;
                var best = engine + 1;
                while (best + 1 <= bound && RectReference.Fits(X, Y, a, b, best + 1, 20).Verdict == RectReference.Verdict.Feasible)
                {
                    best++;
                }

                rows.Add($"| {source} | {X} × {Y} | {a} × {b} | {engine} | **{best}** | {bound} |");
                break;
            default:
                unknown++;
                rows.Add($"| {source} | {X} × {Y} | {a} × {b} | {engine} | ? | {bound} |");
                break;
        }

        Console.Write($"\r{total} cas, {gaps} écarts, {unknown} indécis");
    }

    report.AppendLine("## Plans de couche rectangulaires (palette et caisse) — PalTunes contre CP-SAT");
    report.AppendLine();
    report.AppendLine($"{total} cas ({sw.Elapsed.TotalSeconds:0} s) : **{proved} optimums prouvés** ({provedByBound} par la borne de Barnes, {proved - provedByBound} par CP-SAT), " +
                      $"{gaps} écarts, {unknown} indécis (20 s).");
    report.AppendLine();
    if (rows.Count > 0)
    {
        report.AppendLine("| Source | Palette | Produit | PalTunes | CP-SAT | Borne |");
        report.AppendLine("|---|---|---|---|---|---|");
        rows.ForEach(r => report.AppendLine(r));
        report.AppendLine();
    }
}
