using System.Diagnostics;
using System.Globalization;
using PalTunes.Core.Catalog;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;
using PalTunes.Core.Validation;
using Xunit.Abstractions;

namespace PalTunes.Tests;

/// <summary>
/// Validation du colisage (docs/VALIDATION_MOTEUR.md §6–7) : mise en caisse homogène de chaque type de produit
/// comparée à des références indépendantes (bornes de Barnes par couche × couches, mailles de cercles, usages comme le
/// carton de 12 bouteilles), puis colisage hétérogène de compositions multi-types comparé aux bornes de volume et de
/// poids ; chaque solution passe le contrôle indépendant.
/// </summary>
public class CaseValidationTests(ITestOutputHelper output)
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static double Reach(double side, double l, double w)
    {
        var best = 0.0;
        for (var i = 0; i * l <= side + 1e-9; i++)
        {
            best = Math.Max(best, i * l + Math.Floor((side - i * l) / w + 1e-9) * w);
        }

        return best;
    }

    /// <summary>Borne de Barnes d'une couche (dimensions efficaces), indépendante du moteur.</summary>
    private static int Barnes(double L, double W, double l, double w) => (int)Math.Floor(Reach(L, l, w) * Reach(W, l, w) / (l * w) + 1e-9);

    private static int Hex(double L, double W, double d)
    {
        if (d > L || d > W)
        {
            return 0;
        }

        var rows = (int)Math.Floor((W - d) / (d * Math.Sqrt(3) / 2) + 1e-9) + 1;
        return (rows + 1) / 2 * (int)(L / d) + rows / 2 * (int)((L - d / 2) / d);
    }

    /// <summary>Meilleure maille carrée ou hexagonale de cercles.</summary>
    private static int Circles(double L, double W, double d) =>
        Math.Max((int)(L / d) * (int)(W / d), Math.Max(Hex(L, W, d), Hex(W, L, d)));

    private static int Layers(double H, double h) => (int)Math.Floor(H / h + 1e-9);

    private static CaseSpec Inner(double l, double w, double h, double wall = 5, double tare = 0.5) =>
        new() { InnerLength = l, InnerWidth = w, InnerHeight = h, WallThickness = wall, Tare = tare };

    // ------------------------------------------------------------------ Colisage homogène : un cas par type

    public sealed record HomCase(string Type, string Name, Article Article, CaseSpec Spec, int Reference, string Source, CoilAxis? Axis = null);

    public static IEnumerable<HomCase> HomogeneousCases()
    {
        Article Box(ArticleKind kind, double l, double w, double h, double kg) => new() { Code = $"{kind}", Kind = kind, Length = l, Width = w, Height = h, Weight = kg };
        Article Round(ArticleKind kind, double d, double h, double kg) => new() { Code = $"{kind}", Kind = kind, Diameter = d, Height = h, Weight = kg };

        yield return new("Caisse", "Boîte 190 × 130 × 140 dans 590 × 390 × 290", Box(ArticleKind.Caisse, 190, 130, 140, 0.8), Inner(590, 390, 290),
            Barnes(590, 390, 190, 130) * Layers(290, 140), "Barnes 9 / couche × 2 couches (optimum)");
        yield return new("Bouteille", "Bouteille 1,5 L Ø90 × 320 dans 370 × 280 × 330", Round(ArticleKind.Bouteille, 90, 320, 1.6), Inner(370, 280, 330),
            12, "usage : carton de 12 bouteilles (4 × 3)");
        yield return new("Bidon", "Jerrican 20 L 290 × 190 × 370 dans 600 × 400 × 400", Box(ArticleKind.Bidon, 290, 190, 370, 21), Inner(600, 400, 400),
            Barnes(600, 400, 290, 190) * Layers(400, 370), "Barnes 4 / couche (optimum)");
        yield return new("Seau", "Seau 10 L Ø270 × 260 dans 560 × 560 × 270", Round(ArticleKind.Seau, 270, 260, 11), Inner(560, 560, 270),
            Circles(560, 560, 270), "meilleure maille (2 × 2)");
        yield return new("Tube", "Tube Ø110 × 1200 couché dans 1210 × 340 × 340", new Article { Code = "TUB", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 4.5, CoilAxis = CoilAxis.Horizontal },
            Inner(1210, 340, 340), 9, "lits carrés 3 × 3", CoilAxis.Horizontal);
        yield return new("Bobine", "Bobine Ø400 laize 300 dans 820 × 820 × 310", new Article { Code = "BOB", Kind = ArticleKind.Bobine, Diameter = 400, Width = 300, Weight = 30, CoilAxis = CoilAxis.Vertical },
            Inner(820, 820, 310), Circles(820, 820, 400), "meilleure maille (2 × 2)", CoilAxis.Vertical);
        yield return new("Plaque", "Plaque 1200 × 800 × 20 dans 1210 × 810 × 300", Box(ArticleKind.Plaque, 1200, 800, 20, 2), Inner(1210, 810, 300),
            Layers(300, 20), "1 par couche × 15 couches");
        yield return new("Sac", "Sac 600 × 400 × 120 dans 610 × 410 × 500", Box(ArticleKind.Sac, 600, 400, 120, 25), Inner(610, 410, 500),
            Layers(500, 120), "1 par couche × 4 couches");
        yield return new("Bac", "Bac 600 × 400 × 300 dans 1210 × 810 × 620", Box(ArticleKind.Bac, 600, 400, 300, 8), Inner(1210, 810, 620),
            Barnes(1210, 810, 600, 400) * Layers(620, 300), "Barnes 4 / couche × 2 couches");
        yield return new("Fût", "Fût 200 L Ø585 × 880 dans 1200 × 1200 × 900", Round(ArticleKind.Fut, 585, 880, 220), Inner(1200, 1200, 900, 20, 40),
            Circles(1200, 1200, 585), "usage : 4 fûts (2 × 2)");
        yield return new("Cuve", "Cuve IBC 1200 × 1000 × 1160 dans 1210 × 1010 × 1170", Box(ArticleKind.Cuve, 1200, 1000, 1160, 1050), Inner(1210, 1010, 1170, 20, 60),
            1, "1 cuve");
        yield return new("Autre", "Pavé 500 × 350 × 120 dans 1010 × 710 × 250", Box(ArticleKind.Autre, 500, 350, 120, 3), Inner(1010, 710, 250),
            Barnes(1010, 710, 500, 350) * Layers(250, 120), "Barnes 4 / couche × 2 couches (optimum)");
    }

    public static IEnumerable<object[]> HomogeneousData() => HomogeneousCases().Select((c, i) => new object[] { i });

    [Theory]
    [MemberData(nameof(HomogeneousData))]
    public void CaseHomogeneous_ReachesReference_AndPassesValidator(int index)
    {
        var c = HomogeneousCases().ElementAt(index);
        var sw = Stopwatch.StartNew();
        var r = CaseEngine.Solve(c.Article, c.Spec, axis: c.Axis);
        sw.Stop();
        Assert.NotEmpty(r.Solutions);
        var best = r.Solutions.Max(s => s.ItemsPerUnit);
        var rec = r.Recommended!;
        output.WriteLine($"{c.Type};{c.Name};{c.Reference};{best};{rec.ItemsPerLayer};{rec.LayerCount};{sw.ElapsedMilliseconds};{c.Source}");
        Assert.True(best >= c.Reference, $"{c.Name} : {best} < référence {c.Reference} ({c.Source})");
        Assert.Empty(SolutionValidator.Validate(rec, CaseEngine.CaseConstraints(c.Spec, c.Axis), checkSupport: true));
    }

    // ------------------------------------------------------------------ Colisage hétérogène : compositions multi-types

    public sealed record MixCase(string Name, List<(Article Article, int Quantity)> Lines);

    public static IEnumerable<MixCase> MixedCases()
    {
        Article A(string code, ArticleKind kind, double l = 0, double w = 0, double h = 0, double d = 0, double kg = 1, CoilAxis axis = CoilAxis.Vertical) => new()
        {
            Code = code, Kind = kind, Length = l, Width = w, Height = h, Diameter = d, Weight = kg, CoilAxis = axis
        };

        var boite = A("BOITE-190", ArticleKind.Caisse, 190, 130, 140, kg: 0.8);
        var boite2 = A("BOITE-300", ArticleKind.Caisse, 300, 200, 150, kg: 2);
        var petite = A("BOITE-150", ArticleKind.Caisse, 150, 100, 80, kg: 0.3);
        var bouteille = A("BTL-1L5", ArticleKind.Bouteille, h: 320, d: 90, kg: 1.6);
        var flacon = A("FLC-250", ArticleKind.Bouteille, h: 180, d: 60, kg: 0.3);
        var bidon5 = A("BID-5", ArticleKind.Bidon, 190, 120, 270, kg: 5.4);
        var seau = A("SEAU-10", ArticleKind.Seau, h: 260, d: 270, kg: 11);
        var pot = A("POT-1", ArticleKind.Seau, h: 140, d: 120, kg: 1.1);
        var tube = A("TUB-40", ArticleKind.Tube, l: 580, d: 40, kg: 0.4, axis: CoilAxis.Horizontal);
        var bobine = A("BOB-100", ArticleKind.Bobine, w: 100, d: 150, kg: 0.9);
        var sac = A("SAC-5", ArticleKind.Sac, 300, 200, 80, kg: 5);
        var autre = A("AUT", ArticleKind.Autre, 250, 150, 100, kg: 1);

        yield return new("Boîtes de trois formats", [(boite, 10), (boite2, 4), (petite, 20)]);
        yield return new("Bouteilles + flacons", [(bouteille, 12), (flacon, 24)]);
        yield return new("Bidons 5 L + bouteilles", [(bidon5, 4), (bouteille, 6)]);
        yield return new("Seaux + pots", [(seau, 2), (pot, 10)]);
        yield return new("Tubes couchés + petites boîtes", [(tube, 10), (petite, 12)]);
        yield return new("Bobines + sacs + boîtes", [(bobine, 8), (sac, 2), (boite, 4)]);
        yield return new("Tous les types courants", [(boite, 4), (bouteille, 4), (flacon, 8), (bidon5, 2), (pot, 4), (tube, 4), (bobine, 4), (autre, 2)]);
    }

    public static IEnumerable<object[]> MixedData() => MixedCases().Select((c, i) => new object[] { i });

    [Theory]
    [MemberData(nameof(MixedData))]
    public void CaseMixed_AllPlaced_NearBounds_AndPassesValidator(int index)
    {
        var c = MixedCases().ElementAt(index);
        var cases = CaseCatalog.Defaults().ToList();
        var pallet = PalletCatalog.Defaults().First(p => p.Code == "EUR1");
        var sw = Stopwatch.StartNew();
        var r = CaseEngine.ProposeMixed(c.Lines, cases, pallet: pallet, palletConstraints: new PackagingConstraints { MaxTotalHeight = 1800 });
        sw.Stop();
        var s = r.Recommended;
        Assert.True(s != null, string.Join(" ", r.Messages));
        var type = cases.First(x => x.Id == s!.Base.PalletId);
        var spec = type.ToSpec();

        // Bornes inférieures du nombre de caisses de ce format : volume (enveloppes) et poids (charge maxi, 25 kg brut).
        var volume = c.Lines.Sum(l => l.Article.ForPalletizing() is var a && a.IsCylinder
            ? a.Diameter * a.Diameter * (a.Kind == ArticleKind.Tube ? a.Length : a.Kind == ArticleKind.Bobine ? a.Width : a.Height) * l.Quantity
            : a.Length * a.Width * a.Height * l.Quantity);
        var volumeBound = (int)Math.Ceiling(volume / (spec.InnerLength * spec.InnerWidth * spec.InnerHeight) - 1e-9);
        // Même règle que le moteur : 25 kg brut si chaque produit seul le permet, sinon charge maxi de la caisse seule.
        var manual = c.Lines.Max(l => l.Article.Weight) + spec.Tare <= CaseEngine.DefaultManualHandlingLimit;
        var weightLimit = Math.Min(type.MaxWeight > 0 ? type.MaxWeight : double.MaxValue,
            manual ? CaseEngine.DefaultManualHandlingLimit - spec.Tare : double.MaxValue);
        var weightBound = (int)Math.Ceiling(c.Lines.Sum(l => l.Article.Weight * l.Quantity) / weightLimit - 1e-9);
        var bound = Math.Max(1, Math.Max(volumeBound, weightBound));
        var asked = c.Lines.Sum(l => l.Quantity);
        var fill = s!.Units.Average(u => u.Metrics.FillRate);
        output.WriteLine($"{c.Name};{asked};{s.TotalItems};{type.Code};{bound};{s.Units.Count};{fill.ToString("0.#", Fr)};{s.CasesPerPallet};{s.PalletCount};{sw.ElapsedMilliseconds}");

        Assert.Equal(asked, s.TotalItems);
        Assert.Empty(SolutionValidator.Validate(s, CaseEngine.CaseConstraints(spec), checkSupport: true));
        Assert.All(s.Units, u => Assert.True(u.Items.Sum(p => p.Weight) <= weightLimit + 1e-6));
        Assert.True(s.Units.Count <= bound + 1, $"{c.Name} : {s.Units.Count} caisses pour une borne de {bound}");
    }
}

/// <summary>
/// Compositions hétérogènes où une unité de moins était possible (solution réalisable, appui total, contrôle sans
/// violation) : PalTunes y atteint désormais ce nombre (0.1.7 : variante tout mélangé, recherche élargie des ordres de pose).
/// </summary>
public class MixedOptimumRegressionTests
{
    private static Article Box(string code, double l, double w, double h) => new() { Code = code, Kind = ArticleKind.Caisse, Length = l, Width = w, Height = h, Weight = 3 };

    [Fact]
    public void Case_FullUnitsLeaveRoom_ForOtherArticle()
    {
        var spec = new CaseSpec { InnerLength = 590, InnerWidth = 390, InnerHeight = 290, WallThickness = 5, Tare = 0.5 };
        var s = CaseEngine.SolveMixed([(Box("A", 294, 238, 151), 12), (Box("B", 171, 132, 144), 10)], spec).Recommended!;
        Assert.Equal(6, s.Units.Count);
        Assert.Empty(SolutionValidator.Validate(s, CaseEngine.CaseConstraints(spec), checkSupport: true));
    }

    [Fact]
    public void Case_TwoFormats_ThreeCases()
    {
        // 5 caisses avant 0.1.7 ; 4 trouvées à appui total ; 3 désormais (borne de volume 2).
        var spec = new CaseSpec { InnerLength = 590, InnerWidth = 390, InnerHeight = 290, WallThickness = 5, Tare = 0.5 };
        var s = CaseEngine.SolveMixed([(Box("A", 283, 253, 167), 6), (Box("B", 280, 228, 121), 6)], spec).Recommended!;
        Assert.Equal(3, s.Units.Count);
        Assert.Equal(12, s.TotalItems);
        Assert.Empty(SolutionValidator.Validate(s, CaseEngine.CaseConstraints(spec), checkSupport: true));
    }

    [Theory]
    [InlineData(471, 360, 376, 11, 332, 258, 376, 13, 2)]
    public void Pallet_TwoFormats_ReachesOptimum(double l1, double w1, double h1, int q1, double l2, double w2, double h2, int q2, int expected)
    {
        var pallet = PalletCatalog.Defaults().First(p => p.Code == "EUR1");
        var c = new PackagingConstraints { MaxTotalHeight = pallet.Height + 1000 };
        var s = HeterogeneousEngine.Solve([(Box("A", l1, w1, h1), q1), (Box("B", l2, w2, h2), q2)], BaseInfo.From(pallet, false, 1, 1), c).Recommended!;
        Assert.Equal(expected, s.Units.Count);
        Assert.Empty(SolutionValidator.Validate(s, c, checkSupport: true));
    }
}

/// <summary>Onglets « Détails du calcul » (0.1.8) : étapes présentes et chiffres de la solution repris.</summary>
public class CalculationDetailsTests
{
    private static readonly PalletType Eur = PalletCatalog.Defaults().First(p => p.Code == "EUR1");

    [Fact]
    public void Pallet_Homogeneous_ExplainsLayersAndLimit()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 8 };
        var c = new PackagingConstraints { MaxTotalHeight = 1800 };
        var s = HomogeneousEngine.Solve(a, BaseInfo.From(Eur, false, 1, 1), c).Recommended!;
        var d = PalTunes.Core.Export.CalculationDetails.Pallet(s, s.FirstUnit!, c, a, _ => a)
            .Select(x => x with { Lines = x.Lines.Select(l => l.Replace(' ', ' ').Replace(' ', ' ')).ToList() }).ToList();
        Assert.Contains(d, x => x.Title == "Plan de couche" && x.Lines.Any(l => l.Contains("optimal")));
        var layers = d.Single(x => x.Title == "Nombre de couches");
        Assert.Contains(layers.Lines, l => l.Contains("⌊1 656 / 250⌋ = 6"));
        Assert.Contains(layers.Lines, l => l.Contains($"= {s.ItemsPerUnit}"));
        Assert.Contains(d, x => x.Title == "Base et limites" && x.Lines.Any(l => l.Contains("1 656 mm")));
        var qty = d.Single(x => x.Title == "Quantité par palette");
        Assert.Contains(qty.Lines, l => l.Contains($"{s.ItemsPerLayer} × {s.LayerCount}") && l.Contains($"= {s.ItemsPerUnit}"));
        Assert.DoesNotContain(d, x => x.Title == "Quantité par colisage");
    }

    private static List<PalTunes.Core.Export.DetailSection> Norm(IEnumerable<PalTunes.Core.Export.DetailSection> d) =>
        d.Select(x => x with { Lines = x.Lines.Select(l => l.Replace(' ', ' ').Replace(' ', ' ')).ToList() }).ToList();

    [Fact]
    public void Pallet_LayerSteps_ExplainGridsBlocksAndOperations()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 8 };
        var c = new PackagingConstraints { MaxTotalHeight = 1800 };
        var s = HomogeneousEngine.Solve(a, BaseInfo.From(Eur, false, 1, 1), c).Recommended!;
        var d = Norm(PalTunes.Core.Export.CalculationDetails.Pallet(s, s.FirstUnit!, c, a, _ => a));
        var steps = d.Single(x => x.Title == "Plan de couche pas à pas").Lines;
        Assert.Contains(steps, l => l.Contains("en long") && l.Contains("⌊1 200 / 400⌋ × ⌊800 / 300⌋ = 3 × 2 = 6"));
        Assert.Contains(steps, l => l.Contains("en travers") && l.Contains("⌊1 200 / 300⌋ × ⌊800 / 400⌋ = 4 × 2 = 8"));
        Assert.Contains(steps, l => l.StartsWith("Plan retenu"));
        Assert.Contains(steps, l => l.StartsWith("Borne simple") && l.Contains("au plus 8"));
        var ops = d.Last();
        Assert.Equal("Les opérations, en bref", ops.Title);
        Assert.Contains(ops.Lines, l => l.Contains("1 656 ÷ 250 = 6,62 → 6"));
        Assert.Contains(ops.Lines, l => l.Contains($"Produits par palette : 8 × 6 = {s.ItemsPerUnit}"));
    }

    [Fact]
    public void Pallet_Cylinders_ExplainAlignedAndStaggeredRows()
    {
        var a = new Article { Code = "F", Kind = ArticleKind.Fut, Diameter = 250, Height = 400, Weight = 5 };
        var c = new PackagingConstraints { MaxTotalHeight = 1800 };
        var s = HomogeneousEngine.Solve(a, BaseInfo.From(Eur, false, 1, 1), c).Recommended!;
        var d = Norm(PalTunes.Core.Export.CalculationDetails.Pallet(s, s.FirstUnit!, c, a, _ => a));
        var steps = d.Single(x => x.Title == "Plan de couche pas à pas").Lines;
        Assert.Contains(steps, l => l.StartsWith("Rangées alignées") && l.Contains("= 4 × 3 = 12"));
        Assert.Contains(steps, l => l.StartsWith("En quinconce"));
        Assert.Contains(steps, l => l.StartsWith("Plan retenu") && l.Contains($"= {s.ItemsPerLayer} produits par couche"));
        Assert.Equal("Les opérations, en bref", d.Last().Title);
    }

    [Fact]
    public void Pallet_CaseArticle_ExplainsQuantityPerColisage()
    {
        var product = new Article { Code = "P", Kind = ArticleKind.Caisse, Length = 100, Width = 100, Height = 120, Weight = 0.5 };
        var spec = new CaseSpec { InnerLength = 400, InnerWidth = 300, InnerHeight = 250, WallThickness = 4, Tare = 0.4 };
        var cs = CaseEngine.Solve(product, spec).Recommended!;
        var box = CaseEngine.CreateCaseArticle(product, cs, spec, "CAI-P");
        Article? Find(Guid id) => id == product.Id ? product : id == box.Id ? box : null;
        var c = new PackagingConstraints { MaxTotalHeight = 1800 };
        var s = HomogeneousEngine.Solve(box, BaseInfo.From(Eur, false, 1, 1), c).Recommended!;
        var d = PalTunes.Core.Export.CalculationDetails.Pallet(s, s.FirstUnit!, c, box, Find, a => CaseEngine.Rebuild(a, Find, []))
            .Select(x => x with { Lines = x.Lines.Select(l => l.Replace(' ', ' ').Replace(' ', ' ')).ToList() }).ToList();
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        string F(int v) => v.ToString("#,0", fr).Replace(' ', ' ').Replace(' ', ' ');
        Assert.Contains(d.Single(x => x.Title == "Quantité par palette").Lines,
            l => l.Contains($"{F(s.ItemsPerUnit)} × {F(cs.ItemsPerUnit)} = {F(s.ItemsPerUnit * cs.ItemsPerUnit)}"));
        Assert.Contains(d.Single(x => x.Title == "Quantité par colisage").Lines,
            l => l.Contains($"{cs.ItemsPerLayer} par couche × {cs.LayerCount}") && l.Contains($"= {cs.ItemsPerUnit} produit(s) par caisse"));
    }

    [Fact]
    public void Pallet_Heterogeneous_ExplainsBoundsAndStrategy()
    {
        var a = new Article { Code = "A", Kind = ArticleKind.Caisse, Length = 600, Width = 400, Height = 300, Weight = 9 };
        var b = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 8 };
        var c = new PackagingConstraints { MaxTotalHeight = 1800 };
        var s = HeterogeneousEngine.Solve([(a, 20), (b, 30)], BaseInfo.From(Eur, false, 1, 1), c).Recommended!;
        var d = PalTunes.Core.Export.CalculationDetails.Pallet(s, s.FirstUnit!, c, null, id => id == a.Id ? a : b);
        Assert.Contains(d, x => x.Title == "Bornes" && x.Lines.Any(l => l.StartsWith("Par le volume")));
        Assert.Contains(d, x => x.Title == "Stratégie");
        Assert.Equal(s.Units.Count, d.Single(x => x.Title == "Détail par palette").Lines.Count);
        Assert.Contains(d.Single(x => x.Title == "Quantité par palette").Lines, l => l.Contains("A × "));
        Assert.Contains(d.Single(x => x.Title == "Quantité par palette").Lines, l => l.Contains("B × "));
    }

    [Fact]
    public void Case_ExplainsCaseWeightAndPalletization()
    {
        var a = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 100, Width = 100, Height = 120, Weight = 0.5 };
        var spec = new CaseSpec { InnerLength = 400, InnerWidth = 300, InnerHeight = 250, WallThickness = 4, Tare = 0.4 };
        var s = CaseEngine.Solve(a, spec, pallet: Eur, palletConstraints: new PackagingConstraints { MaxTotalHeight = 1800 }).Recommended!;
        var d = PalTunes.Core.Export.CalculationDetails.Case(s, s.FirstUnit!, spec, null, a, 25, _ => a)
            .Select(x => x with { Lines = x.Lines.Select(l => l.Replace(' ', ' ').Replace(' ', ' ')).ToList() }).ToList();
        Assert.Contains(d, x => x.Title == "Caisse et limites" && x.Lines.Any(l => l.Contains("408 × 308 × 258")));
        Assert.Contains(d, x => x.Title == "Poids de la caisse" && x.Lines.Any(l => l.Contains("Poids brut")));
        Assert.Contains(d.Single(x => x.Title == "Quantité par colisage").Lines,
            l => l.Contains($"{s.ItemsPerLayer} × {s.LayerCount}") && l.Contains($"= {s.ItemsPerUnit} produit(s) par caisse"));
        Assert.Equal("Les opérations, en bref", d.Last().Title);
        Assert.Contains(d.Last().Lines, l => l.Contains("Poids brut"));
        Assert.Contains(d, x => x.Title == "Palettisation des caisses" && x.Lines.Any(l => l.Contains($"= {s.ItemsPerPallet.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")).Replace(' ', ' ').Replace(' ', ' ')}")));
    }
}
