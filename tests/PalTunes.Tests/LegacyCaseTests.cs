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
}
