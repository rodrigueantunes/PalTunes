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

public class HeterogeneousScaleTests
{
    private static BaseInfo EurBase => BaseInfo.From(PalletCatalog.Defaults().Single(p => p.Code == "EUR1"), false, 1, 1);

    private static Article Ring(string code, double d, double l, double kg, double inner = 0) =>
        new() { Code = code, Kind = ArticleKind.Tube, Diameter = d, Length = l, InnerDiameter = inner, Weight = kg, CoilAxis = CoilAxis.Indifferent };

    [Fact]
    public void ArticleThatDoesNotFit_IsExcluded_RestIsOptimal()
    {
        var huge = new Article { Code = "XXL", Kind = ArticleKind.Caisse, Length = 1500, Width = 1000, Height = 100, Weight = 5 };
        var heavy = new Article { Code = "LOURD", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 200, Weight = 5000 };
        var ok = new Article { Code = "OK", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 10 };
        var r = HeterogeneousEngine.Solve([(huge, 2), (heavy, 1), (ok, 20)], EurBase, new PackagingConstraints());
        Assert.NotEmpty(r.Solutions);
        Assert.All(r.Solutions, s =>
        {
            Assert.True(s.IsCompliant, s.Title + " : " + string.Join(" | ", s.Violations));
            Assert.Equal(20, s.TotalItems);
            Assert.Equal(2, s.ExcludedArticles.Count);
            Assert.Contains(s.ExcludedArticles, e => e.StartsWith("XXL × 2"));
            Assert.Contains(s.ExcludedArticles, e => e.StartsWith("LOURD × 1") && e.Contains("poids"));
            Assert.Contains(s.Warnings, w => w.StartsWith("Article exclu"));
        });
    }

    [Fact]
    public void LargeQuantity_FullSingleArticlePallets_ThenMixedRemainder()
    {
        var carton = new Article { Code = "CAR", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12 };
        var other = new Article { Code = "AUT", Kind = ArticleKind.Caisse, Length = 300, Width = 200, Height = 150, Weight = 4 };
        var r = HeterogeneousEngine.Solve([(carton, 200), (other, 10)], EurBase, new PackagingConstraints());
        var best = r.Recommended!;
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
        Assert.Equal(210, best.TotalItems);
        var full = best.Units.Where(u => u.IsFullPallet).ToList();
        Assert.NotEmpty(full);
        Assert.All(full, u => Assert.All(u.Items, p => Assert.Equal(carton.Id, p.ArticleId)));
        Assert.Equal(Enumerable.Range(1, best.Units.Count), best.Units.Select(u => u.Index));
    }

    [Fact]
    public void StackedRings_LinearTime_AndCompliant()
    {
        // 600 bagues mélangées : 188 s avant la v0.1.0 (propagation des charges exponentielle avec la hauteur).
        var order = new List<(Article, int)>
        {
            (Ring("B76", 76, 50, 0.032, 68), 100), (Ring("B60", 60, 40, 0.02, 54), 100), (Ring("T110", 110, 250, 0.3, 100), 100),
            (Ring("B25", 25.7, 12, 0.001, 21.7), 100), (Ring("T40", 40, 600, 0.15, 34), 100), (Ring("M76", 76, 1000, 0.6, 70), 100)
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = HeterogeneousEngine.Solve(order, EurBase, new PackagingConstraints());
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 15000, $"{sw.ElapsedMilliseconds} ms");
        Assert.All(r.Solutions, s =>
        {
            Assert.Equal(600, s.TotalItems);
            Assert.True(s.IsCompliant, s.Title + " : " + string.Join(" | ", s.Violations));
        });
    }

    [Fact]
    public void HundredThousandRings_ByLayers_InSeconds()
    {
        var order = new List<(Article, int)>
        {
            (Ring("B25", 25.7, 12, 0.001, 21.7), 100000), (Ring("B76", 76, 50, 0.032, 68), 2000), (Ring("T110", 110, 250, 0.3, 100), 300)
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = HeterogeneousEngine.Solve(order, EurBase, new PackagingConstraints());
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 60000, $"{sw.ElapsedMilliseconds} ms");
        var best = r.Recommended!;
        Assert.Equal(102300, best.TotalItems);
        Assert.True(best.IsCompliant, string.Join(" | ", best.Violations));
        Assert.Contains(r.Messages, m => m.Contains("Couches homogènes"));
    }

    [Fact]
    public void UprightCylinders_RealContactArea()
    {
        var below = new Placement { X = 0, Y = 0, DX = 100, DY = 100, DZ = 50, Shape = ShapeKind.CylinderZ };
        var aligned = new Placement { X = 0, Y = 0, Z = 50, DX = 100, DY = 100, DZ = 50, Shape = ShapeKind.CylinderZ };
        var offset = new Placement { X = 50, Y = 87, Z = 50, DX = 100, DY = 100, DZ = 50, Shape = ShapeKind.CylinderZ };
        Assert.Equal(Math.PI * 50 * 50, Geometry.ContactArea(aligned, below), 3);
        // Carrés englobants en recouvrement, disques sans contact (quinconce) : aucun appui.
        Assert.True(Geometry.FootprintOverlap(offset, below) > 0);
        Assert.Equal(0, Geometry.ContactArea(offset, below), 3);
    }
}

public class SolutionStorageTests
{
    [Fact]
    public void Solution_CompactRoundTrip_AndLegacyFormatRead()
    {
        var carton = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12 };
        var pallet = PalletCatalog.Defaults().Single(p => p.Code == "EUR1");
        var s = HomogeneousEngine.Solve(carton, BaseInfo.From(pallet, false, 1, 1), new PackagingConstraints()).Recommended!;
        var json = System.Text.Json.JsonSerializer.Serialize(s, DatabaseStore.Json);
        Assert.DoesNotContain("FirstUnit", json);
        Assert.Contains("\"Rows\"", json);
        Assert.DoesNotContain("\"Sequence\"", json);
        Assert.True(json.Length / s.TotalItems < 200, $"{json.Length / s.TotalItems} octets par produit");
        var back = System.Text.Json.JsonSerializer.Deserialize<Solution>(json, DatabaseStore.Json)!;
        var (a, b) = (s.FirstUnit!.Items, back.FirstUnit!.Items);
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal((a[i].ArticleId, a[i].X, a[i].Y, a[i].Z, a[i].DX, a[i].DY, a[i].DZ, a[i].Shape, a[i].Layer, a[i].Sequence, a[i].Weight),
                (b[i].ArticleId, b[i].X, b[i].Y, b[i].Z, b[i].DX, b[i].DY, b[i].DZ, b[i].Shape, b[i].Layer, b[i].Sequence, b[i].Weight));
        }

        // Ancien format (un objet par produit, versions 0.0.x) : toujours lu.
        var legacy = "{\"Units\":[{\"Items\":[{\"ArticleId\":\"" + carton.Id + "\",\"X\":10,\"Y\":20,\"Z\":0,\"DX\":400,\"DY\":300,\"DZ\":250,\"Shape\":\"Box\",\"Layer\":1,\"Sequence\":1,\"Weight\":12,\"MaxX\":410}]}]}";
        var old = System.Text.Json.JsonSerializer.Deserialize<Solution>(legacy, DatabaseStore.Json)!;
        Assert.Equal(10, old.FirstUnit!.Items.Single().X);
        Assert.Equal(carton.Id, old.FirstUnit.Items.Single().ArticleId);
    }
}

