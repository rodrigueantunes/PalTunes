using PalTunes.Core.Catalog;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.Tests;

/// <summary>Cas de contrôle de l'étude hétérogène (docs/ETUDE_HETEROGENE.md §3.2 et §11).</summary>
public class HeterogeneousStudyTests
{
    private static BaseInfo EurBase => BaseInfo.From(PalletCatalog.Defaults().Single(p => p.Code == "EUR1"), false, 1, 1);

    private static Article Carton(string code, double l, double w, double h, double kg, bool fragile = false) =>
        new() { Code = code, Kind = ArticleKind.Caisse, Length = l, Width = w, Height = h, Weight = kg, Fragile = fragile };

    /// <summary>Produits sur lesquels repose directement le placement (dessus exactement à sa cote).</summary>
    private static List<Placement> Below(LoadUnit unit, Placement p) =>
        unit.Items.Where(q => !ReferenceEquals(q, p) && Math.Abs(q.MaxZ - p.Z) < 0.5 &&
                              Math.Min(q.MaxX, p.MaxX) - Math.Max(q.X, p.X) > 1 && Math.Min(q.MaxY, p.MaxY) - Math.Max(q.Y, p.Y) > 1).ToList();

    [Theory]
    [InlineData(ArticleKind.Caisse, 400, 300, 250, 12, 30)]
    [InlineData(ArticleKind.Caisse, 600, 400, 300, 9, 18)]
    [InlineData(ArticleKind.Bac, 600, 400, 300, 8, 32)]
    [InlineData(ArticleKind.Sac, 600, 400, 120, 25, 300)]
    public void Profile_DeducedCapacity_MatchesStudyTable(ArticleKind kind, double l, double w, double h, double kg, double expected)
    {
        var a = new Article { Code = "X", Kind = kind, Length = l, Width = w, Height = h, Weight = kg };
        var p = StackingProfile.For(a);
        Assert.Equal(expected, p.Capacity, 3);
        Assert.False(p.CapacityEntered);
    }

    [Fact]
    public void Profile_Drum_UsesFloor_AndBottomZone()
    {
        var drum = new Article { Code = "F", Kind = ArticleKind.Fut, Diameter = 585, Height = 880, Weight = 220 };
        var p = StackingProfile.For(drum);
        Assert.Equal(110, p.Capacity, 3);
        Assert.Equal(StackZone.Bas, p.Zone);
        Assert.True(p.RoundTop);
    }

    [Fact]
    public void Profile_EnteredValue_AndFragile_Win()
    {
        var a = Carton("C", 400, 300, 250, 12);
        a.MaxLoadOnTop = 90;
        Assert.Equal(90, StackingProfile.For(a).Capacity);
        Assert.True(StackingProfile.For(a).CapacityEntered);
        a.Fragile = true;
        Assert.Equal(0, StackingProfile.For(a).Capacity);
        Assert.Equal(StackZone.Haut, StackingProfile.For(a).Zone);
    }

    [Fact]
    public void Sacks_NeverOnTopOfCartons()
    {
        var sack = new Article { Code = "SAC", Kind = ArticleKind.Sac, Length = 600, Width = 400, Height = 120, Weight = 25 };
        var light = Carton("L", 300, 200, 200, 3);
        var r = HeterogeneousEngine.Solve([(light, 20), (sack, 12)], EurBase, new PackagingConstraints());
        foreach (var s in r.Solutions)
        {
            Assert.True(s.IsCompliant, s.Title + " : " + string.Join(" | ", s.Violations));
            foreach (var unit in s.Units)
            {
                foreach (var p in unit.Items.Where(p => p.ArticleId == sack.Id))
                {
                    Assert.DoesNotContain(Below(unit, p), q => q.ArticleId == light.Id);
                }
            }
        }
    }

    [Fact]
    public void Drum_NeverOnCartons()
    {
        var drum = new Article { Code = "FUT", Kind = ArticleKind.Fut, Diameter = 585, Height = 880, Weight = 220 };
        var box = Carton("B", 400, 300, 300, 8);
        var cs = new PackagingConstraints { MaxLoadWeight = 1500 };
        var r = HeterogeneousEngine.Solve([(box, 12), (drum, 2)], EurBase, cs);
        foreach (var s in r.Solutions)
        {
            foreach (var unit in s.Units)
            {
                foreach (var p in unit.Items.Where(p => p.ArticleId == drum.Id))
                {
                    Assert.DoesNotContain(Below(unit, p), q => q.ArticleId == box.Id);
                }
            }
        }
    }

    [Fact]
    public void Plate_OffFloor_NeedsFullSupport()
    {
        var plate = new Article { Code = "PLQ", Kind = ArticleKind.Plaque, Length = 1200, Width = 800, Height = 10, Weight = 15 };
        var box = Carton("B", 590, 390, 200, 6);
        var r = HeterogeneousEngine.Solve([(box, 4), (plate, 3)], EurBase, new PackagingConstraints());
        foreach (var s in r.Solutions)
        {
            foreach (var unit in s.Units)
            {
                foreach (var p in unit.Items.Where(p => p.ArticleId == plate.Id && p.Z > 0.5))
                {
                    var covered = Below(unit, p).Sum(q => (Math.Min(q.MaxX, p.MaxX) - Math.Max(q.X, p.X)) * (Math.Min(q.MaxY, p.MaxY) - Math.Max(q.Y, p.Y)));
                    Assert.True(covered / (p.DX * p.DY) >= 0.95 - 1e-6, $"{s.Title} : appui {covered / (p.DX * p.DY):P0}");
                }
            }
        }
    }

    [Fact]
    public void LyingTubes_CarryOnlyTubes()
    {
        var tube = new Article { Code = "TUB", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 4.5, CoilAxis = CoilAxis.Horizontal };
        var box = Carton("B", 300, 200, 200, 3);
        var r = HeterogeneousEngine.Solve([(tube, 14), (box, 16)], EurBase, new PackagingConstraints());
        foreach (var s in r.Solutions)
        {
            foreach (var unit in s.Units)
            {
                foreach (var p in unit.Items.Where(p => p.ArticleId == box.Id))
                {
                    Assert.DoesNotContain(Below(unit, p), q => q.ArticleId == tube.Id);
                }
            }
        }
    }

    [Fact]
    public void FragileItems_InTopZone_NothingAbove()
    {
        var glass = Carton("VER", 270, 180, 320, 8.4, fragile: true);
        var box = Carton("B", 400, 300, 250, 12);
        var r = HeterogeneousEngine.Solve([(box, 16), (glass, 6)], EurBase, new PackagingConstraints());
        var best = r.Recommended!;
        foreach (var unit in best.Units)
        {
            foreach (var p in unit.Items.Where(p => p.ArticleId == glass.Id))
            {
                Assert.DoesNotContain(unit.Items, q => Below(unit, q).Contains(p));
            }
        }
    }

    [Fact]
    public void MixedWeights_HeavyBelowLight()
    {
        var heavy = Carton("H", 400, 300, 250, 18);
        var medium = Carton("M", 400, 300, 250, 9);
        var light = Carton("L", 400, 300, 250, 2);
        var r = HeterogeneousEngine.Solve([(light, 8), (medium, 8), (heavy, 8)], EurBase, new PackagingConstraints());
        var best = r.Recommended!;
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
        Assert.True(best.Units.All(u => u.Metrics.OrderRespect >= 95), string.Join(", ", best.Units.Select(u => u.Metrics.OrderRespect)));
        Assert.True(best.Units.All(u => u.Metrics.CapacityUseMax <= 100 + 1e-6));
        var unit = best.Units[0];
        Assert.True(unit.Items.Where(p => p.ArticleId == heavy.Id).Average(p => p.Z) < unit.Items.Where(p => p.ArticleId == light.Id).Average(p => p.Z));
    }

    [Fact]
    public void DeducedCapacity_IsRespected_WithoutEnteredValues()
    {
        // 12 kg × 0,5 × 5 = 30 kg déduits : un carton ne reçoit jamais plus de 30 kg, même sans charge maxi saisie.
        var box = Carton("B", 400, 300, 250, 12);
        var r = HeterogeneousEngine.Solve([(box, 48)], EurBase, new PackagingConstraints());
        Assert.All(r.Solutions, s => Assert.All(s.Units, u => Assert.True(u.Metrics.CapacityUseMax <= 100 + 1e-6, $"{s.Title} {u.Metrics.CapacityUseMax}")));

        // Colonnes parfaitement alignées : capacité « colonne » 12 × 5 = 60 kg → 6 couches, comme en homogène.
        var layered = r.Solutions.Single(s => s.Strategy == MixedStrategy.CouchesHomogenes);
        Assert.Single(layered.Units);
        Assert.Equal(6, layered.Units[0].Layers.Count);
    }

    [Fact]
    public void Profile_AlignedCapacity_ForDeducedCartonsOnly()
    {
        var box = Carton("B", 400, 300, 250, 12);
        Assert.Equal(60, StackingProfile.For(box).AlignedCapacity, 3);
        box.MaxLoadOnTop = 40;
        Assert.Equal(40, StackingProfile.For(box).AlignedCapacity, 3);
    }
}

public class DemoMixTests
{
    [Fact]
    public void DemoMix02_SacksAndCratesBelow_Compliant()
    {
        var db = PalTunes.Core.Storage.Database.CreateDefault(withDemo: true);
        var mix = db.Packagings.Single(p => p.Code == "CDT-MIX-02");
        var r = PackagingCalculator.Compute(mix, db);
        var best = r.Recommended!;
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
        Assert.Equal(0, best.UnplacedItems);
        var sack = db.Articles.Single(a => a.Code == "SAC-25").Id;
        var small = db.Articles.Single(a => a.Code == "CAR-300").Id;
        foreach (var u in best.Units)
        {
            var sacks = u.Items.Where(p => p.ArticleId == sack).ToList();
            var smalls = u.Items.Where(p => p.ArticleId == small).ToList();
            if (sacks.Count > 0 && smalls.Count > 0)
            {
                Assert.True(sacks.Max(p => p.Z) <= smalls.Max(p => p.Z));
            }
        }
    }
}
