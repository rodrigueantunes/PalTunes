using PalTunes.Core.Catalog;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.Tests;

public class HeterogeneousTests
{
    private static BaseInfo EurBase => BaseInfo.From(PalletCatalog.Defaults().Single(p => p.Code == "EUR1"), false, 1, 1);

    [Fact]
    public void DemoMix_AllPlaced_Compliant()
    {
        var db = Database.CreateDefault(withDemo: true);
        var mix = db.Packagings.Single(p => p.Code == "CDT-MIX-01");
        var r = PackagingCalculator.Compute(mix, db);
        Assert.Equal(3, r.Solutions.Count);
        var best = r.Recommended!;
        Assert.Equal(0, best.UnplacedItems);
        Assert.Equal(46, best.TotalItems);
        Assert.Single(best.Units);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
        Assert.All(r.Solutions, s => Assert.True(s.IsCompliant, s.Title + " : " + string.Join(" | ", s.Violations)));
    }

    [Fact]
    public void Overflow_OpensSecondUnit()
    {
        var a = new Article { Code = "A", Kind = ArticleKind.Caisse, Length = 600, Width = 400, Height = 400, Weight = 10 };
        var b = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 300, Weight = 5 };
        // 4 / couche × 4 couches = 16 A par unité : 20 A + 10 B dépassent une unité de 1800.
        var r = HeterogeneousEngine.Solve([(a, 20), (b, 10)], EurBase, new PackagingConstraints());
        Assert.All(r.Solutions, s =>
        {
            Assert.Equal(0, s.UnplacedItems);
            Assert.True(s.Units.Count >= 2);
            Assert.True(s.IsCompliant, s.Title + " : " + string.Join(" | ", s.Violations));
        });
    }

    [Fact]
    public void Fragile_NothingOnTop()
    {
        var heavy = new Article { Code = "H", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 300, Weight = 20 };
        var fragile = new Article { Code = "F", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 300, Weight = 2, Fragile = true };
        var r = HeterogeneousEngine.Solve([(heavy, 16), (fragile, 8)], EurBase, new PackagingConstraints());
        foreach (var s in r.Solutions)
        {
            foreach (var unit in s.Units)
            {
                var fragiles = unit.Items.Where(p => p.ArticleId == fragile.Id).ToList();
                foreach (var f in fragiles)
                {
                    Assert.DoesNotContain(unit.Items, p => Math.Abs(p.Z - f.MaxZ) < 0.5 &&
                        Math.Min(p.MaxX, f.MaxX) - Math.Max(p.X, f.X) > 1 && Math.Min(p.MaxY, f.MaxY) - Math.Max(p.Y, f.Y) > 1);
                }
            }
        }
    }

    [Fact]
    public void Layered_UsesFullHomogeneousLayers()
    {
        var a = new Article { Code = "A", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12 };
        var b = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 600, Width = 400, Height = 200, Weight = 6 };
        var r = HeterogeneousEngine.Solve([(a, 19), (b, 5)], EurBase, new PackagingConstraints());
        var layered = r.Solutions.Single(s => s.Strategy == MixedStrategy.CouchesHomogenes);
        var unit = layered.Units.Single();
        Assert.Equal(16, unit.Items.Count(p => p.ArticleId == a.Id && p.Z < 300));
        Assert.Equal(24, unit.Items.Count);
    }
}
