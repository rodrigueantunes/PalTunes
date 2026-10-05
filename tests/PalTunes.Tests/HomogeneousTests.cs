using PalTunes.Core.Catalog;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.Tests;

/// <summary>Exemples chiffrés de l'étude (§4.4, §4.5, §5.3, §10.3).</summary>
public class HomogeneousTests
{
    private static PalletType Eur => PalletCatalog.Defaults().Single(p => p.Code == "EUR1");

    private static BaseInfo EurBase => BaseInfo.From(Eur, false, 1, 1);

    [Fact]
    public void Carton400x300_OnEur_Gives48_InSixLayers()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12 };
        var r = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints());
        var best = r.Recommended!;
        Assert.Equal(8, best.ItemsPerLayer);
        Assert.Equal(6, best.LayerCount);
        Assert.Equal(48, best.ItemsPerUnit);
        Assert.True(best.ProvenOptimal);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
        var m = best.FirstUnit!.Metrics;
        Assert.Equal(1500, m.LoadHeight, 3);
        Assert.Equal(1644, m.EnclosureHeight, 3);
        Assert.Equal(1200, m.EnclosureLength, 3);
        Assert.Equal(800, m.EnclosureWidth, 3);
        Assert.Equal(576, m.LoadWeight, 3);
    }

    [Theory]
    [InlineData(250, 13)]
    [InlineData(400, 6)]
    public void Circles_BestOfSquareAndHex(double diameter, int expected)
    {
        Assert.Equal(expected, CircleLayerSolver.Solve(1200, 800, diameter).Best.Count);
    }

    [Fact]
    public void Tubes_Staggered_Gives111_AndIsRecommended()
    {
        var a = new Article { Code = "T", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 4.5, CoilAxis = CoilAxis.Indifferent };
        var r = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints());
        var best = r.Recommended!;
        Assert.Equal(StackPattern.Quinconce, best.Pattern);
        Assert.Equal(111, best.ItemsPerUnit);
        Assert.Contains(r.Solutions, s => s.ItemsPerUnit == 105 && s.Pattern == StackPattern.Colonne);
        Assert.Contains(r.Solutions, s => s.FirstUnit!.Items[0].Shape == ShapeKind.CylinderZ);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
    }

    [Fact]
    public void Tubes_ForcedVerticalAxis_OnlyUprightSolutions()
    {
        var a = new Article { Code = "T", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 4.5, CoilAxis = CoilAxis.Indifferent };
        var r = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints { ForcedAxis = CoilAxis.Vertical });
        Assert.NotEmpty(r.Solutions);
        Assert.All(r.Solutions, s => Assert.All(s.FirstUnit!.Items, p => Assert.Equal(ShapeKind.CylinderZ, p.Shape)));
        Assert.Equal(80, r.Recommended!.ItemsPerUnit);
    }

    [Fact]
    public void Corners_AddThicknessToEnclosure()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12 };
        var r = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints { Corners = true, CornerThickness = 5, FilmThickness = 1 });
        var m = r.Recommended!.FirstUnit!.Metrics;
        Assert.Equal(1212, m.EnclosureLength, 3);
        Assert.Equal(812, m.EnclosureWidth, 3);
    }

    [Fact]
    public void SlipSheets_ReduceLayersAndAreCounted()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 275, Weight = 5 };
        var c = new PackagingConstraints { SlipSheetThickness = 5, SlipSheetOnPallet = true };
        var best = HomogeneousEngine.Solve(a, EurBase, c).Recommended!;
        // 1656 utiles : 6 couches × 275 = 1650 + 6 intercalaires × 5 = 1680 > 1656 → 5 couches.
        Assert.Equal(5, best.LayerCount);
        Assert.Equal(5, best.FirstUnit!.Metrics.SlipSheetCount);
    }

    [Fact]
    public void Plate_OnTwoPhysicalPallets()
    {
        var a = new Article { Code = "P", Kind = ArticleKind.Plaque, Length = 1600, Width = 1200, Height = 10, Weight = 15 };
        var single = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints { MaxTotalHeight = 1200 });
        Assert.Empty(single.Solutions);

        var twin = BaseInfo.From(Eur, rotated: true, countL: 2, countW: 1);
        Assert.Equal(1600, twin.Length);
        Assert.Equal(1200, twin.Width);
        var r = HomogeneousEngine.Solve(a, twin, new PackagingConstraints { MaxTotalHeight = 1200 });
        var best = r.Recommended!;
        Assert.Equal(2, best.Base.PhysicalCount);
        Assert.Equal(105, best.ItemsPerUnit);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
    }

    [Fact]
    public void Propose_FindsTwinPalletsForLargePlate()
    {
        var a = new Article { Code = "P", Kind = ArticleKind.Plaque, Length = 1600, Width = 1200, Height = 10, Weight = 15 };
        var r = HomogeneousEngine.Propose(a, PalletCatalog.Defaults(), new PackagingConstraints { MaxTotalHeight = 1200 });
        Assert.NotEmpty(r.Solutions);
        Assert.Contains(r.Solutions, s => s.Base.PhysicalCount == 2 && Math.Abs(s.Base.Length - 1600) < 1 && Math.Abs(s.Base.Width - 1200) < 1);
        Assert.Single(r.Solutions, s => s.Recommended);
    }

    [Fact]
    public void Strength_LimitsLayers_AndColumnIsPreferred()
    {
        // 12 kg, 40 kg supportables : colonne → 1 + 40/12 = 4 couches ; croisé (× 0,5) → 1 + 20/12 = 2 couches.
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 200, Weight = 12, MaxLoadOnTop = 40 };
        var r = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints());
        var best = r.Recommended!;
        Assert.Equal(StackPattern.Colonne, best.Pattern);
        Assert.Equal(4, best.LayerCount);
        Assert.Contains("résistance", best.LayerLimitReason);
    }

    [Fact]
    public void Sacks_PreferCrossedPattern()
    {
        var a = new Article { Code = "S", Kind = ArticleKind.Sac, Length = 500, Width = 300, Height = 120, Weight = 25 };
        var r = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints());
        Assert.Contains(r.Solutions, s => s.Pattern == StackPattern.Croise);
        Assert.Equal(StackPattern.Croise, r.Recommended!.Pattern);
    }

    [Fact]
    public void TargetQuantity_PartialLayer()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12 };
        var best = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints(), targetQuantity: 20).Recommended!;
        Assert.Equal(20, best.ItemsPerUnit);
        Assert.Equal(3, best.LayerCount);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
    }

    [Fact]
    public void Stacking_LimitedByStackedHeight()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12 };
        var best = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints { MaxTotalHeight = 1300, MaxStacking = 2, MaxStackedHeight = 2700 }).Recommended!;
        // 1300 - 144 = 1156 → 4 couches (1000) ; encombrement 1144 ; 2700 / 1144 = 2 niveaux = 1 gerbage.
        Assert.Equal(2, best.StackLevels);
        Assert.Equal(1, best.Stackings);
    }
}

public class CaseTests
{
    [Fact]
    public void Case_Packs12Boxes_AndCreatesCaseArticle()
    {
        var a = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 100, Width = 100, Height = 120, Weight = 0.5 };
        var spec = new CaseSpec { InnerLength = 400, InnerWidth = 300, InnerHeight = 250, WallThickness = 4, Tare = 0.4 };
        var r = CaseEngine.Solve(a, spec);
        var best = r.Recommended!;
        Assert.Equal(24, best.ItemsPerUnit);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
        var box = CaseEngine.CreateCaseArticle(a, best, spec, "CAI-B");
        Assert.Equal(408, box.Length);
        Assert.Equal(308, box.Width);
        Assert.Equal(258, box.Height);
        Assert.Equal(12.4, box.Weight, 3);
        Assert.Empty(ArticleSchema.Validate(box));
    }
}

public class CornersInsideTests
{
    private static BaseInfo EurBase => BaseInfo.From(PalletCatalog.Defaults().Single(p => p.Code == "EUR1"), false, 1, 1);

    [Fact]
    public void CornersInside_KeepEnclosureWithinPallet()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 390, Width = 295, Height = 250, Weight = 12 };
        var c = new PackagingConstraints { Corners = true, CornerThickness = 5, CornersInside = true };
        var best = HomogeneousEngine.Solve(a, EurBase, c).Recommended!;
        Assert.Equal(8, best.ItemsPerLayer);
        var m = best.FirstUnit!.Metrics;
        Assert.Equal(1200, m.EnclosureLength, 3);
        Assert.Equal(800, m.EnclosureWidth, 3);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
    }

    [Fact]
    public void CornersInside_TubesNoLongerFitLengthwise()
    {
        var a = new Article { Code = "T", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 4.5, CoilAxis = CoilAxis.Horizontal };
        var outside = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints { Corners = true, CornerThickness = 5 });
        var inside = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints { Corners = true, CornerThickness = 5, CornersInside = true });
        Assert.NotEmpty(outside.Solutions);
        Assert.Empty(inside.Solutions);
    }

    [Fact]
    public void CornersInside_OffByDefault() => Assert.False(new PackagingConstraints().CornersInside);
}

public class CaseCatalogTests
{
    [Fact]
    public void Propose_ReturnsOnlyPossibleCases_BestFirst()
    {
        var a = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 190, Width = 140, Height = 90, Weight = 0.4 };
        var cases = CaseCatalog.Defaults();
        var r = CaseEngine.Propose(a, cases, manualHandlingLimit: 0);
        Assert.NotEmpty(r.Solutions);
        Assert.Single(r.Solutions, s => s.Recommended);
        Assert.True(r.Solutions[0].Recommended);
        var fills = r.Solutions.Select(s => Math.Round(CaseEngine.InnerFill(s, cases), 1)).ToList();
        Assert.Equal(fills.OrderByDescending(f => f).ToList(), fills);
        var big = new Article { Code = "X", Kind = ArticleKind.Caisse, Length = 700, Width = 500, Height = 300, Weight = 5 };
        var possible = CaseEngine.PossibleCases(big, cases);
        Assert.NotEmpty(possible);
        Assert.All(possible, c => Assert.True(c.InnerLength >= 700));
        Assert.DoesNotContain(possible, c => c.Code == "CRT-6040");
    }
}

public class CaseManualHandlingTests
{
    [Fact]
    public void Propose_PrefersManuallyHandledCases()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 300, Width = 200, Height = 150, Weight = 4 };
        var cases = CaseCatalog.Defaults();
        var best = CaseEngine.Propose(a, cases).Recommended!;
        var gross = best.FirstUnit!.Items.Sum(p => p.Weight) + cases.First(c => c.Id == best.Base.PalletId).Tare;
        Assert.True(gross <= 25, $"{best.Base.PalletCode} : {gross} kg");
        var unlimited = CaseEngine.Propose(a, cases, manualHandlingLimit: 0).Recommended!;
        Assert.True(CaseEngine.InnerFill(unlimited, cases) >= CaseEngine.InnerFill(best, cases));
    }
}

public class CaseDestinationPalletTests
{
    [Fact]
    public void Propose_WithPallet_RanksByProductsPerPallet()
    {
        var a = new Article { Code = "P", Kind = ArticleKind.Caisse, Length = 95, Width = 95, Height = 140, Weight = 0.3 };
        var cases = CaseCatalog.Defaults();
        var eur = PalletCatalog.Defaults().Single(p => p.Code == "EUR1");
        var r = CaseEngine.Propose(a, cases, pallet: eur);
        Assert.NotEmpty(r.Solutions);
        Assert.All(r.Solutions, s => Assert.Equal("EUR1", s.DestinationPallet));
        var manual = r.Solutions.Where(s => s.FirstUnit!.Items.Sum(p => p.Weight) + cases.First(c => c.Id == s.Base.PalletId).Tare <= 25).ToList();
        Assert.Equal(manual.Max(s => s.ItemsPerPallet), r.Recommended!.ItemsPerPallet);
        Assert.Equal(r.Recommended.CasesPerPallet * r.Recommended.ItemsPerUnit, r.Recommended.ItemsPerPallet);
        Assert.Contains("EUR1", r.Recommended.Recommendation);
    }

    [Fact]
    public void Destination_ChangesRecommendation_OrCounts()
    {
        var a = new Article { Code = "P", Kind = ArticleKind.Caisse, Length = 95, Width = 95, Height = 140, Weight = 0.3 };
        var cases = CaseCatalog.Defaults();
        var eur = PalletCatalog.Defaults().Single(p => p.Code == "EUR1");
        var iso = PalletCatalog.Defaults().Single(p => p.Code == "ISO1210");
        var onEur = CaseEngine.Propose(a, cases, pallet: eur).Recommended!;
        var onIso = CaseEngine.Propose(a, cases, pallet: iso).Recommended!;
        Assert.Equal("ISO1210", onIso.DestinationPallet);
        Assert.True(onIso.ItemsPerPallet >= onEur.ItemsPerPallet);
    }
}

public class TubeCornerTests
{
    private static BaseInfo EurBase => BaseInfo.From(PalletCatalog.Defaults().Single(p => p.Code == "EUR1"), false, 1, 1);
    private static Article Tube => new() { Code = "T", Kind = ArticleKind.Tube, Diameter = 110, Length = 1500, Weight = 5, CoilAxis = CoilAxis.Horizontal };

    private static Solution Square(PackagingConstraints c) =>
        HomogeneousEngine.Solve(Tube, EurBase, c).Solutions.First(s => s.Pattern == StackPattern.Colonne && s.FirstUnit!.Items[0].Shape == ShapeKind.CylinderX);

    [Fact]
    public void OverhangingTubes_CornersStayAtPallet_AndTubesAreRemoved()
    {
        var follow = Square(new PackagingConstraints { OverhangLength = 150, Corners = true, CornerThickness = 5, CornerLeg = 60, CornersFollowTubes = true });
        var pallet = Square(new PackagingConstraints { OverhangLength = 150, Corners = true, CornerThickness = 5, CornerLeg = 60 });
        Assert.Equal(0, follow.FirstUnit!.RemovedForCorners);
        Assert.Null(follow.FirstUnit.CornerFrame);
        Assert.True(pallet.FirstUnit!.RemovedForCorners > 0);
        Assert.Equal(follow.ItemsPerUnit - pallet.FirstUnit.RemovedForCorners, pallet.ItemsPerUnit);
        var f = pallet.FirstUnit.CornerFrame!;
        Assert.Equal(5, f.X0, 3);
        Assert.Equal(1195, f.X1, 3);
        // Aucun tube restant dans les bandes des ailes aux extrémités de la palette.
        Assert.DoesNotContain(pallet.FirstUnit.Items, p => p.Y < f.Y0 + 60 - 0.5 && p.MaxY > f.Y0 + 0.5);
        Assert.True(pallet.IsCompliant, string.Join(" | ", pallet.Violations));
        // Encombrement : les cornières (au niveau de la palette) n'allongent pas la charge au-delà des tubes.
        Assert.Equal(1500, pallet.FirstUnit.Metrics.EnclosureLength, 3);
    }

    [Fact]
    public void TubesWithoutOverhang_Unchanged()
    {
        var a = new Article { Code = "T", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 5, CoilAxis = CoilAxis.Horizontal };
        var r = HomogeneousEngine.Solve(a, EurBase, new PackagingConstraints { Corners = true, CornerThickness = 5 });
        Assert.All(r.Solutions, s => Assert.Equal(0, s.FirstUnit!.RemovedForCorners));
    }

    [Fact]
    public void OptionIsOffByDefault() => Assert.False(new PackagingConstraints().CornersFollowTubes);
}
