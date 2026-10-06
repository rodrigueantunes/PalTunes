using System.Diagnostics;
using System.Globalization;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;
using PalTunes.Core.Validation;
using Xunit.Abstractions;

namespace PalTunes.Tests;

/// <summary>
/// Validation du moteur (docs/VALIDATION_MOTEUR.md) : palettes homogènes de chaque type de produit comparées à des
/// références indépendantes du moteur — optimums publiés du « pallet loading problem », usages industriels, calculs
/// analytiques (borne de surface, grilles, réseaux de cercles) — puis palettes hétérogènes multi-types comparées aux
/// bornes de volume et de poids, chaque solution passant le contrôle indépendant (chevauchements, appuis, limites).
/// </summary>
public class ValidationTests(ITestOutputHelper output)
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static PalletType Pallet(string code, double l, double w, double h = 144, double load = 1500) => new()
    {
        Code = code, Name = code, Length = l, Width = w, Height = h, Tare = 25, DynamicLoad = load, StaticLoad = load * 4
    };

    private static readonly PalletType Eur = Pallet("EUR1", 1200, 800);

    // ------------------------------------------------------------------ Références analytiques (indépendantes du moteur)

    /// <summary>Borne de surface : jamais plus de produits que l'aire de la palette divisée par celle du produit.</summary>
    private static int AreaBound(double L, double W, double l, double w) => (int)Math.Floor(L * W / (l * w) + 1e-9);

    /// <summary>
    /// Borne de Barnes (dimensions efficaces) : chaque côté de la palette réduit à la plus grande combinaison
    /// i·l + j·w qu'il peut contenir, puis borne de surface.
    /// </summary>
    private static int EffectiveBound(double L, double W, double l, double w)
    {
        static double Reach(double side, double l, double w)
        {
            var best = 0.0;
            for (var i = 0; i * l <= side + 1e-9; i++)
            {
                best = Math.Max(best, i * l + Math.Floor((side - i * l) / w + 1e-9) * w);
            }

            return best;
        }

        return (int)Math.Floor(Reach(L, l, w) * Reach(W, l, w) / (l * w) + 1e-9);
    }

    /// <summary>Meilleure grille simple (tous les produits dans le même sens).</summary>
    private static int Grid(double L, double W, double l, double w) =>
        Math.Max((int)(L / l) * (int)(W / w), (int)(L / w) * (int)(W / l));

    /// <summary>Cercles de diamètre d : meilleure maille carrée ou hexagonale (rangées dans un sens ou dans l'autre).</summary>
    private static int Circles(double L, double W, double d) => Math.Max(Grid(L, W, d, d), Math.Max(Hex(L, W, d), Hex(W, L, d)));

    private static int Hex(double L, double W, double d)
    {
        if (d > L || d > W)
        {
            return 0;
        }

        var row = (int)(L / d);
        var shifted = (int)((L - d / 2) / d);
        var rows = (int)Math.Floor((W - d) / (d * Math.Sqrt(3) / 2) + 1e-9) + 1;
        return (rows + 1) / 2 * row + rows / 2 * shifted;
    }

    /// <summary>Borne de densité des cercles (maille hexagonale, π/√12) : un plan de couche ne peut pas faire mieux.</summary>
    private static int CircleBound(double L, double W, double d) => (int)Math.Floor(L * W * Math.PI / Math.Sqrt(12) / (Math.PI * d * d / 4) + 1e-9);

    // ------------------------------------------------------------------ Homogène : un cas par type de produit

    /// <param name="Accepted">Minimum exigé du moteur ; inférieur à la référence seulement pour un écart connu et documenté.</param>
    public sealed record HomCase(string Type, string Name, Article Article, PalletType Pallet, PackagingConstraints Constraints,
        int Reference, int? UpperBound, string Source, int? Accepted = null);

    public static IEnumerable<HomCase> HomogeneousCases()
    {
        PackagingConstraints H(double max = 1800) => new() { MaxTotalHeight = max };
        Article Box(ArticleKind kind, double l, double w, double h, double kg) => new()
        {
            Code = $"{kind}-{l:0}x{w:0}", Kind = kind, Length = l, Width = w, Height = h, Weight = kg
        };
        Article Round(ArticleKind kind, double d, double h, double kg) => new()
        {
            Code = $"{kind}-D{d:0}", Kind = kind, Diameter = d, Height = h, Weight = kg
        };

        // Caisses : cas d'école (borne de surface atteinte) et optimums publiés du pallet loading problem.
        yield return new("Caisse", "Carton 600 × 400 sur EUR", Box(ArticleKind.Caisse, 600, 400, 300, 9), Eur, H(), 4, AreaBound(1200, 800, 600, 400), "borne de surface (optimum)");
        yield return new("Caisse", "Carton 400 × 300 sur EUR", Box(ArticleKind.Caisse, 400, 300, 250, 8), Eur, H(), 8, AreaBound(1200, 800, 400, 300), "borne de surface (optimum)");
        yield return new("Caisse", "Carton 300 × 200 sur EUR", Box(ArticleKind.Caisse, 300, 200, 150, 4), Eur, H(), 16, AreaBound(1200, 800, 300, 200), "borne de surface (optimum)");
        yield return new("Caisse", "Carton 365 × 245 sur EUR", Box(ArticleKind.Caisse, 365, 245, 200, 6), Eur, H(), EffectiveBound(1200, 800, 365, 245),
            EffectiveBound(1200, 800, 365, 245), "borne de Barnes 9 (optimum ; surface brute 10)");
        yield return new("Caisse", "Carton 330 × 220 sur 1200 × 1000", Box(ArticleKind.Caisse, 330, 220, 200, 6), Pallet("1210", 1200, 1000), H(), EffectiveBound(1200, 1000, 330, 220),
            EffectiveBound(1200, 1000, 330, 220), "borne de Barnes (optimum)");
        yield return new("Caisse", "Instance N1 (43 × 26, boîte 7 × 3)", Box(ArticleKind.Caisse, 70, 30, 20, 0.1), Pallet("N1", 430, 260, 10), H(50), 53, 53,
            "optimum publié 53 (Birgin et al.) ; 52 en 5 blocs récursifs, 53 demande l'approche en L", Accepted: 52);
        yield return new("Caisse", "Instance N4 (42 × 39, boîte 9 × 4)", Box(ArticleKind.Caisse, 90, 40, 20, 0.1), Pallet("N4", 420, 390, 10), H(50), 45, 45,
            "optimum publié 45 (Birgin et al.)");
        yield return new("Sac", "Sac 25 kg 600 × 400 sur EUR", Box(ArticleKind.Sac, 600, 400, 120, 25), Eur, H(), 4, 4, "borne de surface (optimum)");
        yield return new("Bac", "Bac 600 × 400 sur EUR", Box(ArticleKind.Bac, 600, 400, 300, 8), Eur, H(), 4, 4, "borne de surface (optimum)");
        yield return new("Plaque", "Plaque 1200 × 800 ép. 20", Box(ArticleKind.Plaque, 1200, 800, 20, 2), Eur, H(), 1, 1, "borne de surface (optimum)");
        yield return new("Fût", "Fût 200 L Ø585 sur EUR 1200 × 800", Round(ArticleKind.Fut, 585, 880, 220), Eur, H(2000), 2, 2, "usage : 2 fûts de 200 L par palette EUR");
        yield return new("Fût", "Fût 200 L Ø585 sur 1200 × 1200", Round(ArticleKind.Fut, 585, 880, 220), Pallet("1212", 1200, 1200), H(2000), 4, 4,
            "usage : 4 fûts de 200 L sur palette 1200 × 1200");
        yield return new("Bobine", "Bobine Ø400 laize 300 debout", new Article { Code = "BOB", Kind = ArticleKind.Bobine, Diameter = 400, Width = 300, Weight = 30, CoilAxis = CoilAxis.Vertical },
            Eur, H(), Circles(1200, 800, 400), CircleBound(1200, 800, 400), "meilleure maille carrée / hexagonale");
        yield return new("Tube", "Tube Ø110 × 1200 debout", new Article { Code = "TUB", Kind = ArticleKind.Tube, Diameter = 110, Length = 600, Weight = 2, CoilAxis = CoilAxis.Vertical },
            Eur, H(), Circles(1200, 800, 110), CircleBound(1200, 800, 110), "meilleure maille carrée / hexagonale");
        yield return new("Bidon", "Jerrican 20 L 290 × 190 × 370", Box(ArticleKind.Bidon, 290, 190, 370, 21), Eur, H(), EffectiveBound(1200, 800, 290, 190),
            EffectiveBound(1200, 800, 290, 190), "borne de Barnes 16 (optimum ; surface brute 17)");
        yield return new("Seau", "Seau 10 L Ø270", Round(ArticleKind.Seau, 270, 260, 11), Eur, H(), Circles(1200, 800, 270), CircleBound(1200, 800, 270), "meilleure maille carrée / hexagonale");
        yield return new("Bouteille", "Bouteille 1,5 L Ø90", Round(ArticleKind.Bouteille, 90, 320, 1.6), Eur, H(), Circles(1200, 800, 90), CircleBound(1200, 800, 90),
            "meilleure maille carrée / hexagonale");
        yield return new("Cuve", "Cuve IBC 1200 × 1000 × 1160 sur 1200 × 1000", Box(ArticleKind.Cuve, 1200, 1000, 1160, 1050), Pallet("1210", 1200, 1000, 0, 5000), H(2400), 1, 1,
            "usage : 1 cuve par niveau");
        yield return new("Autre", "Pavé générique 500 × 350", Box(ArticleKind.Autre, 500, 350, 120, 3), Eur, H(), EffectiveBound(1200, 800, 500, 350),
            EffectiveBound(1200, 800, 500, 350), "borne de Barnes (optimum)");
    }

    public static IEnumerable<object[]> HomogeneousData() => HomogeneousCases().Select((c, i) => new object[] { i });

    [Theory]
    [MemberData(nameof(HomogeneousData))]
    public void Homogeneous_ReachesReference_AndPassesValidator(int index)
    {
        var c = HomogeneousCases().ElementAt(index);
        var sw = Stopwatch.StartNew();
        var r = HomogeneousEngine.Solve(c.Article, BaseInfo.From(c.Pallet, false, 1, 1), c.Constraints);
        sw.Stop();
        Assert.NotEmpty(r.Solutions);
        var bestLayer = r.Solutions.Max(s => s.ItemsPerLayer);
        var rec = r.Recommended!;
        output.WriteLine($"{c.Type};{c.Name};{c.Reference};{c.UpperBound};{bestLayer};{rec.ItemsPerLayer};{rec.LayerCount};{rec.ItemsPerUnit};{sw.ElapsedMilliseconds};{c.Source}");

        Assert.True(bestLayer >= (c.Accepted ?? c.Reference), $"{c.Name} : {bestLayer} par couche < référence {c.Reference} ({c.Source})");
        if (c.UpperBound is { } bound)
        {
            Assert.True(bestLayer <= bound, $"{c.Name} : {bestLayer} par couche > borne {bound}");
        }

        Assert.Empty(SolutionValidator.Validate(rec, c.Constraints, checkSupport: true));
    }

    // ------------------------------------------------------------------ Hétérogène : compositions multi-types

    public sealed record HetCase(string Name, List<(Article Article, int Quantity)> Lines, PalletType Pallet, PackagingConstraints Constraints);

    public static IEnumerable<HetCase> HeterogeneousCases()
    {
        Article A(string code, ArticleKind kind, double l = 0, double w = 0, double h = 0, double d = 0, double kg = 1, double? top = null,
            CoilAxis axis = CoilAxis.Vertical) => new()
        {
            Code = code, Kind = kind, Length = l, Width = w, Height = h, Diameter = d, Weight = kg, MaxLoadOnTop = top, CoilAxis = axis
        };

        var car600 = A("CAR-600", ArticleKind.Caisse, 600, 400, 300, kg: 9, top: 120);
        var car400 = A("CAR-400", ArticleKind.Caisse, 400, 300, 250, kg: 8, top: 90);
        var car300 = A("CAR-300", ArticleKind.Caisse, 300, 200, 150, kg: 4, top: 60);
        var bac = A("BAC", ArticleKind.Bac, 600, 400, 300, kg: 8, top: 200);
        var sac = A("SAC", ArticleKind.Sac, 600, 400, 120, kg: 25);
        var fut = A("FUT", ArticleKind.Fut, h: 880, d: 585, kg: 220);
        var bidon = A("BID-20", ArticleKind.Bidon, 290, 190, 370, kg: 21, top: 80);
        var seau = A("SEAU-10", ArticleKind.Seau, h: 260, d: 270, kg: 11);
        var bouteille = A("BTL-1L5", ArticleKind.Bouteille, h: 320, d: 90, kg: 1.6);
        var ibc = A("IBC", ArticleKind.Cuve, 1200, 1000, 1160, kg: 1050, top: 1100);
        var bobine = A("BOB", ArticleKind.Bobine, w: 300, d: 400, kg: 30);
        var tube = A("TUB", ArticleKind.Tube, l: 1200, d: 110, kg: 4.5, axis: CoilAxis.Horizontal);
        var plaque = A("PLQ", ArticleKind.Plaque, 1200, 800, 20, kg: 15);
        var autre = A("AUT", ArticleKind.Autre, 500, 350, 120, kg: 3);
        var c1800 = new PackagingConstraints { MaxTotalHeight = 1800 };

        yield return new("Cartons de trois formats", [(car600, 20), (car400, 40), (car300, 60)], Eur, c1800);
        yield return new("Cartons + bacs + sacs", [(car600, 16), (bac, 12), (sac, 20)], Eur, c1800);
        yield return new("Bidons + seaux + cartons", [(bidon, 48), (seau, 30), (car400, 24)], Eur, c1800);
        yield return new("Bouteilles + bidons + seaux", [(bouteille, 300), (bidon, 32), (seau, 16)], Eur, c1800);
        yield return new("Fûts + bidons + sacs", [(fut, 4), (bidon, 32), (sac, 20)], Eur, new PackagingConstraints { MaxTotalHeight = 2000 });
        yield return new("Bobines + tubes + plaques", [(bobine, 12), (tube, 40), (plaque, 10)], Eur, c1800);
        yield return new("Tous les types (sauf cuve)", [(car600, 8), (car400, 12), (bac, 6), (sac, 10), (bidon, 16), (seau, 12), (bouteille, 120), (bobine, 6), (autre, 10)],
            Eur, c1800);
        yield return new("Cuves IBC + bidons", [(ibc, 2), (bidon, 32)], Pallet("1210", 1200, 1000, 144, 5000), new PackagingConstraints { MaxTotalHeight = 2600 });
    }

    public static IEnumerable<object[]> HeterogeneousData() => HeterogeneousCases().Select((c, i) => new object[] { i });

    [Theory]
    [MemberData(nameof(HeterogeneousData))]
    public void Heterogeneous_MixedTypes_NearBounds_AndPassesValidator(int index)
    {
        var c = HeterogeneousCases().ElementAt(index);
        var baseInfo = BaseInfo.From(c.Pallet, false, 1, 1);
        var sw = Stopwatch.StartNew();
        var r = HeterogeneousEngine.Solve(c.Lines, baseInfo, c.Constraints);
        sw.Stop();
        var s = r.Recommended;
        Assert.NotNull(s);

        // Bornes inférieures du nombre de palettes (aucun moteur ne peut faire moins) : volume et poids.
        var usefulHeight = c.Constraints.MaxTotalHeight - c.Pallet.Height;
        var volume = c.Lines.Sum(l => l.Article.ForPalletizing() is var a && a.IsCylinder
            ? Math.Pow(a.Diameter, 2) * (a.Kind == ArticleKind.Tube && a.CoilAxis == CoilAxis.Horizontal ? a.Length : a.Kind == ArticleKind.Bobine ? a.Width : a.Height) * l.Quantity
            : a.Length * a.Width * a.Height * l.Quantity);
        var volumeBound = (int)Math.Ceiling(volume / (c.Pallet.Length * c.Pallet.Width * usefulHeight) - 1e-9);
        var weightBound = (int)Math.Ceiling(c.Lines.Sum(l => l.Article.Weight * l.Quantity) / c.Pallet.DynamicLoad - 1e-9);
        var bound = Math.Max(1, Math.Max(volumeBound, weightBound));
        var placed = s!.TotalItems;
        var asked = c.Lines.Sum(l => l.Quantity);
        var fill = s.Units.Average(u => u.Metrics.FillRate);
        output.WriteLine($"{c.Name};{asked};{placed};{bound};{s.UnitCount};{fill.ToString("0.#", Fr)};{sw.ElapsedMilliseconds};{s.Title}");

        Assert.Equal(asked, placed);
        Assert.Empty(SolutionValidator.Validate(s, c.Constraints, checkSupport: true));
        Assert.True(s.UnitCount <= bound + 1, $"{c.Name} : {s.UnitCount} palettes pour une borne de {bound}");
    }
}

/// <summary>
/// Mailles mixtes de produits ronds (0.1.7) : quelques rangées carrées dans une quinconce gagnent une rangée. Valeurs
/// de référence du banc de comparaison (tests/PalTunes.Reference) ; disques contrôlés deux à deux.
/// </summary>
public class CircleMixedLatticeTests
{
    [Theory]
    [InlineData(1200, 800, 107, 86)]
    [InlineData(1200, 800, 156, 38)]
    [InlineData(1200, 1000, 170, 41)]
    [InlineData(1200, 1000, 303, 11)]
    [InlineData(1140, 1140, 79, 218)]
    [InlineData(1200, 800, 400, 6)]
    public void MixedLattice_ReachesReference_WithoutOverlap(double x, double y, double d, int expected)
    {
        var pattern = CircleLayerSolver.Solve(x, y, d).Best;
        Assert.Equal(expected, pattern.Count);
        var c = pattern.Items.Select(r => (X: r.X + r.W / 2, Y: r.Y + r.H / 2)).ToList();
        Assert.All(c, p => Assert.True(p.X >= d / 2 - 1e-6 && p.Y >= d / 2 - 1e-6 && p.X <= x - d / 2 + 1e-6 && p.Y <= y - d / 2 + 1e-6));
        for (var i = 0; i < c.Count; i++)
        {
            for (var k = i + 1; k < c.Count; k++)
            {
                Assert.True(Math.Pow(c[i].X - c[k].X, 2) + Math.Pow(c[i].Y - c[k].Y, 2) >= d * d - 1e-6);
            }
        }
    }
}
