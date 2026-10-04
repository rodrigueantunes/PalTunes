using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.Core.Engine;

/// <summary>Point d'entrée unique : résout un conditionnement (base, article(s)) et appelle le bon moteur.</summary>
public static class PackagingCalculator
{
    public static EngineResult Compute(Packaging p, Database db)
    {
        var pallet = db.FindPallet(p.PalletId);
        if (pallet == null)
        {
            var r = new EngineResult();
            r.Messages.Add("Choisissez une palette.");
            return r;
        }

        var baseInfo = BaseInfo.From(pallet, p.PalletRotated, p.CountAlongLength, p.CountAlongWidth);
        if (p.Kind == PackagingKind.Homogene)
        {
            var article = db.FindArticle(p.ArticleId);
            if (article == null)
            {
                var r = new EngineResult();
                r.Messages.Add("Choisissez l'article à palettiser.");
                return r;
            }

            return HomogeneousEngine.Solve(article, baseInfo, p.Constraints, p.TargetQuantity);
        }

        var lines = p.Lines
            .Select(l => (Article: db.FindArticle(l.ArticleId), l.Quantity))
            .Where(l => l.Article != null && l.Quantity > 0)
            .Select(l => (l.Article!, l.Quantity))
            .ToList();
        if (lines.Count == 0)
        {
            var r = new EngineResult();
            r.Messages.Add("Ajoutez au moins une ligne article × quantité.");
            return r;
        }

        return HeterogeneousEngine.Solve(lines, baseInfo, p.Constraints);
    }

    /// <summary>Assistant homogène : toutes les palettes actives et assemblages pertinents (étude §11.2).</summary>
    public static EngineResult Propose(Packaging p, Database db)
    {
        var article = db.FindArticle(p.ArticleId);
        if (article == null)
        {
            var r = new EngineResult();
            r.Messages.Add("Choisissez l'article à palettiser.");
            return r;
        }

        return HomogeneousEngine.Propose(article, db.Pallets, p.Constraints, p.TargetQuantity);
    }
}
