using PalTunes.Core.Catalog;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.Tests;

/// <summary>Caisses créées au colisage avant la 0.1.3 : quantité et contenu repris de la fiche à l'ouverture (schéma 6).</summary>
public class LegacyCaseTests
{
    [Fact]
    public void LegacyCaseArticle_IsCompleted_AndRebuildsColisage()
    {
        var db = new Database { Cases = [.. CaseCatalog.Defaults()] };
        var ring = new Article { Code = "BAG0000001", Kind = ArticleKind.Tube, Diameter = 76, InnerDiameter = 60, Length = 50, Weight = 0.12, CoilAxis = CoilAxis.Indifferent, Client = "C00007" };
        var type = db.Cases.First(c => c.Code == "CRT-8060");
        var colisage = CaseEngine.Solve(ring, type.ToSpec(), null, type, axis: CoilAxis.Horizontal).Recommended!;
        var box = new Article
        {
            Code = "CAI-BAG0000001-CRT-8060", Kind = ArticleKind.Caisse, Length = 800, Width = 600, Height = 400, Client = "C00007",
            Weight = Math.Round(colisage.ItemsPerUnit * ring.Weight + type.Tare, 5),
            Designation = $"{type.Name} de {colisage.ItemsPerUnit} × BAG0000001",
            Notes = $"Colisage : {colisage.ItemsPerLayer} par couche × {colisage.LayerCount} couche(s), Couché, axe dans la largeur. Intérieur 786 × 586 × 386 mm, paroi 7 mm."
        };
        var mixed = new Article { Code = "CAI-MIX-CRT-2015", Kind = ArticleKind.Caisse, Length = 200, Width = 150, Height = 100, Weight = 0.5, Designation = "Carton 200 × 150 × 100 mixte : 4 × BAG0000001 + 2 × X" };
        db.Articles.AddRange([ring, box, mixed]);

        Assert.Equal(2, db.CompleteLegacyCaseArticles());
        Assert.Equal(colisage.ItemsPerUnit, box.QuantityPerCase);
        Assert.Equal(ring.Id, box.CaseContent!.ArticleId);
        Assert.Equal("CRT-8060", box.CaseContent.CaseTypeCode);
        Assert.Equal(386, box.CaseContent.InnerHeight);
        Assert.Equal(CoilAxis.Horizontal, box.CaseContent.Axis);
        Assert.Equal(6, mixed.QuantityPerCase);
        Assert.Null(mixed.CaseContent);

        var sheet = CaseEngine.Rebuild(box, id => db.FindArticle(id), db.Cases);
        Assert.NotNull(sheet);
        Assert.Equal(colisage.ItemsPerUnit, sheet!.Solution.ItemsPerUnit);

        // Déjà complétée : rien à refaire.
        Assert.Equal(0, db.CompleteLegacyCaseArticles());
    }

    /// <summary>Caisse mixte créée avant la 0.1.9 (contenu dans la désignation seulement) : articles × quantités repris, fiche complète.</summary>
    [Fact]
    public void LegacyMixedCase_IsCompleted_AndRebuildsInOneCase()
    {
        var db = new Database { Cases = [.. CaseCatalog.Defaults()] };
        var a = new Article { Code = "BAG0000001", Kind = ArticleKind.Tube, Diameter = 76, InnerDiameter = 68, Length = 50, Weight = 0.007, CoilAxis = CoilAxis.Indifferent, Client = "C00007" };
        var b = new Article { Code = "BAG0000011", Kind = ArticleKind.Tube, Diameter = 64.7, InnerDiameter = 60.7, Length = 49, Weight = 0.014, CoilAxis = CoilAxis.Indifferent, Client = "C04344" };
        var mixed = new Article
        {
            Code = "CAI-MIX-CRT-8060", Kind = ArticleKind.Caisse, Length = 800, Width = 600, Height = 400, Weight = 5.45,
            Designation = "Carton 800 × 600 × 400 mixte : 350 × BAG0000001 + 100 × BAG0000011",
            Notes = "Colisage mixte : 350 × BAG0000001 + 100 × BAG0000011. Intérieur 786 × 586 × 386 mm, paroi 7 mm."
        };
        db.Articles.AddRange([a, b, mixed]);

        Assert.Equal(2, db.CompleteLegacyCaseArticles());
        Assert.Equal(450, mixed.QuantityPerCase);
        var link = mixed.CaseContent!;
        Assert.True(link.IsMixed);
        Assert.Equal("CRT-8060", link.CaseTypeCode);
        Assert.Equal(a.Id, link.ArticleId);
        Assert.Equal([(a.Id, 350), (b.Id, 100)], link.Lines!.Select(l => (l.ArticleId, l.Quantity)).ToList());
        Assert.True(CaseEngine.CanRebuild(mixed, id => db.FindArticle(id)));

        var sheet = CaseEngine.Rebuild(mixed, id => db.FindArticle(id), db.Cases)!;
        Assert.True(sheet.IsMixed);
        Assert.Single(sheet.Solution.Units);
        Assert.Equal(350, sheet.ShownUnit.Items.Count(p => p.ArticleId == a.Id));
        Assert.Equal(100, sheet.ShownUnit.Items.Count(p => p.ArticleId == b.Id));
        Assert.Equal("350 × BAG0000001 + 100 × BAG0000011", sheet.ContentText(id => db.FindArticle(id)));
        Assert.Equal(0, db.CompleteLegacyCaseArticles());
    }

    /// <summary>Caisse mixte créée au colisage (0.1.9) : contenu détaillé enregistré, recalculé à l'identique.</summary>
    [Fact]
    public void MixedCaseArticle_StoresContent_AndRebuilds()
    {
        var cases = CaseCatalog.Defaults().ToList();
        var a = new Article { Code = "A", Kind = ArticleKind.Caisse, Length = 100, Width = 80, Height = 60, Weight = 0.4 };
        var b = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 120, Width = 100, Height = 50, Weight = 0.6 };
        var type = cases.First(c => c.Code == "CRT-6040");
        var s = CaseEngine.SolveMixed([(a, 10), (b, 6)], type.ToSpec(), type).Recommended!;
        var box = CaseEngine.CreateMixedCaseArticle(s.FirstUnit!, type.ToSpec(), type, "CAI-MIX-CRT-6040", id => id == a.Id ? "A" : "B");
        Article? Find(Guid id) => id == a.Id ? a : id == b.Id ? b : null;
        Assert.True(box.CaseContent!.IsMixed);
        Assert.Equal(16, box.CaseContent.Lines!.Sum(l => l.Quantity));
        var sheet = CaseEngine.Rebuild(box, Find, cases)!;
        Assert.Equal(16, sheet.ShownUnit.Items.Count);
        Assert.Equal("10 × A + 6 × B", sheet.ContentText(Find));

        // Détails du calcul d'une palette de cette caisse : contenu mixte expliqué.
        var c = new PackagingConstraints { MaxTotalHeight = 1800 };
        var eur = PalTunes.Core.Catalog.PalletCatalog.Defaults().First(p => p.Code == "EUR1");
        var ps = HomogeneousEngine.Solve(box, BaseInfo.From(eur, false, 1, 1), c).Recommended!;
        var d = PalTunes.Core.Export.CalculationDetails.Pallet(ps, ps.FirstUnit!, c, box, id => Find(id) ?? (id == box.Id ? box : null), x => CaseEngine.Rebuild(x, Find, cases));
        Assert.Contains(d.Single(x => x.Title == "Quantité par colisage").Lines, l => l.Contains("caisse mixte") && l.Contains("10 × A + 6 × B"));
        Assert.Contains(d.Single(x => x.Title == "Quantité par palette").Lines, l => l.StartsWith("Dont "));
    }
}
