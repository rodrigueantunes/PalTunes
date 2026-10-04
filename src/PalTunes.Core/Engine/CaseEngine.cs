using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

/// <summary>Caisse (carton, colis) dans laquelle on range des produits identiques. Dimensions en mm, masses en kg.</summary>
public sealed class CaseSpec
{
    public double InnerLength { get; set; } = 400;
    public double InnerWidth { get; set; } = 300;
    public double InnerHeight { get; set; } = 250;

    /// <summary>Épaisseur de paroi (dimensions extérieures = intérieures + 2 × paroi).</summary>
    public double WallThickness { get; set; } = 4;

    public double Tare { get; set; } = 0.3;

    /// <summary>Poids maximal des produits dans la caisse. 0 = non limité.</summary>
    public double MaxWeight { get; set; }

    public double Gap { get; set; }

    public double OuterLength => InnerLength + 2 * WallThickness;
    public double OuterWidth => InnerWidth + 2 * WallThickness;
    public double OuterHeight => InnerHeight + 2 * WallThickness;
}

/// <summary>
/// Analyse de colisage (équivalent « caisse » de StackBuilder) : nombre maximal de produits identiques dans une caisse,
/// avec les mêmes plans de couche que la palettisation (étude §4), puis création de l'article caisse à palettiser.
/// </summary>
public static class CaseEngine
{
    public const string CaseCode = "CAISSE";

    public static BaseInfo CaseBase(CaseSpec spec, CaseType? type = null) => new()
    {
        PalletId = type?.Id ?? Guid.Empty,
        PalletCode = type?.Code ?? CaseCode,
        PalletName = type?.Name ?? $"Caisse intérieure {spec.InnerLength:0} × {spec.InnerWidth:0} × {spec.InnerHeight:0}",
        Construction = PalletConstruction.Plein,
        Material = PalletMaterial.Carton,
        Color = "#B9925E",
        PalletLength = spec.InnerLength,
        PalletWidth = spec.InnerWidth,
        PalletHeight = 0,
        PalletDynamicLoad = spec.MaxWeight,
        IsCase = true
    };

    public static PackagingConstraints CaseConstraints(CaseSpec spec) => new()
    {
        MaxTotalHeight = spec.InnerHeight,
        Gap = spec.Gap,
        CenterLoad = true,
        MaxStackLevels = 1,
        MaxStackedHeight = 0,
        MinSupportPercent = 80
    };

    public static EngineResult Solve(Article article, CaseSpec spec, int? targetQuantity = null, CaseType? type = null,
        PalletType? pallet = null, PackagingConstraints? palletConstraints = null)
    {
        var result = HomogeneousEngine.Solve(article, CaseBase(spec, type), CaseConstraints(spec), targetQuantity);
        if (result.Solutions.Count == 0 && result.Messages.Count > 0 && result.Messages[^1].StartsWith("Aucune disposition", StringComparison.Ordinal))
        {
            result.Messages[^1] = $"{article.DimensionsText} ne tient pas dans une caisse intérieure {spec.InnerLength:0} × {spec.InnerWidth:0} × {spec.InnerHeight:0} mm : agrandissez la caisse ou autorisez une autre orientation de l'article.";
        }

        foreach (var s in result.Solutions)
        {
            s.StackLevels = 1;
            s.StackLimitReason = "";
            s.Warnings.RemoveAll(w => w.StartsWith("Élancement", StringComparison.Ordinal));
            s.Recommendation = s.Recommendation?
                .Replace("produits par conditionnement", "produit(s) par caisse")
                .Replace("plan de couche optimal prouvé", "plan optimal prouvé");
            if (pallet != null)
            {
                Palletize(article, s, spec, pallet, palletConstraints);
            }
        }

        return result;
    }

    /// <summary>
    /// Palettise la caisse (article « caisse » : dimensions extérieures, poids brut) sur la palette de destination et
    /// renseigne caisses par palette et produits par palette (meilleure solution homogène, étude §4).
    /// </summary>
    public static void Palletize(Article content, Solution caseSolution, CaseSpec spec, PalletType pallet, PackagingConstraints? palletConstraints)
    {
        var box = CreateCaseArticle(content, caseSolution, spec, "CAISSE");
        var constraints = palletConstraints ?? new PackagingConstraints();
        var best = HomogeneousEngine.Solve(box, BaseInfo.From(pallet, false, 1, 1), constraints).Recommended;
        caseSolution.DestinationPallet = pallet.Code;
        caseSolution.CasesPerPallet = best is { IsCompliant: true } ? best.ItemsPerUnit : 0;
        caseSolution.ItemsPerPallet = caseSolution.CasesPerPallet * caseSolution.ItemsPerUnit;
        if (caseSolution.CasesPerPallet == 0)
        {
            caseSolution.Warnings.Add($"Caisse non palettisable sur {pallet.Code} avec les contraintes saisies (dimensions, hauteur ou poids).");
        }
    }

    /// <summary>Caisses du catalogue dans lesquelles l'article tient (au moins un produit, poids compris).</summary>
    public static List<CaseType> PossibleCases(Article article, IEnumerable<CaseType> cases, double gap = 0) =>
        cases.Where(c => c.Validate().Count == 0 && (c.MaxWeight <= 0 || article.Weight <= c.MaxWeight) &&
                         Solve(article, c.ToSpec(gap), null, c).Solutions.Count > 0).ToList();

    /// <summary>Poids brut maximal d'une caisse manutentionnée à la main (NF X35-109 : 25 kg, valeur courante).</summary>
    public const double DefaultManualHandlingLimit = 25;

    /// <summary>
    /// Meilleure composition en caisse (catalogue actif) : pour chaque caisse possible, la solution recommandée.
    /// Classement : caisses manutentionnables à la main (poids brut ≤ <paramref name="manualHandlingLimit"/>, 0 = ignoré)
    /// d'abord, puis taux de remplissage du volume intérieur, puis nombre de produits, puis caisse la plus légère.
    /// </summary>
    public static EngineResult Propose(Article article, IEnumerable<CaseType> cases, double gap = 0, int? targetQuantity = null,
        double manualHandlingLimit = DefaultManualHandlingLimit, PalletType? pallet = null, PackagingConstraints? palletConstraints = null)
    {
        var result = new EngineResult();
        var list = new List<Solution>();
        foreach (var c in cases.Where(c => c.Active && c.Validate().Count == 0))
        {
            var best = Solve(article, c.ToSpec(gap), targetQuantity, c, pallet, palletConstraints).Recommended;
            if (best == null || !best.IsCompliant)
            {
                continue;
            }

            best.Recommended = false;
            best.Recommendation = null;
            best.Title = $"{c.Code} · {best.ItemsPerUnit} produits";
            list.Add(best);
        }

        double Gross(Solution s) => s.FirstUnit!.Items.Sum(p => p.Weight) + cases.First(c => c.Id == s.Base.PalletId).Tare;
        bool Manual(Solution s) => manualHandlingLimit <= 0 || Gross(s) <= manualHandlingLimit + 1e-9;
        foreach (var s in list.Where(s => !Manual(s)))
        {
            s.Warnings.Add($"Poids brut {Gross(s):0.##} kg > {manualHandlingLimit:0.#} kg : manutention mécanisée nécessaire.");
        }

        // Palette de destination connue : le critère principal est le nombre de produits par palette.
        list = list
            .OrderBy(s => Manual(s) ? 0 : 1)
            .ThenByDescending(s => pallet != null ? s.ItemsPerPallet : 0)
            .ThenByDescending(s => Math.Round(InnerFill(s, cases), 1))
            .ThenByDescending(s => s.ItemsPerUnit)
            .ThenBy(s => cases.First(c => c.Id == s.Base.PalletId).Tare)
            .ToList();
        if (list.Count == 0)
        {
            result.Messages.Add($"Aucune caisse active du catalogue ne peut contenir {article.DimensionsText} ({article.Weight:0.#####} kg).");
            return result;
        }

        var top = list[0];
        top.Recommended = true;
        var type = cases.First(c => c.Id == top.Base.PalletId);
        top.Recommendation = (pallet != null
                                 ? $"Meilleure caisse pour la palette {pallet.Code} : {type.Code} – {type.Name} : {top.ItemsPerUnit} produits par caisse × " +
                                   $"{top.CasesPerPallet} caisses par palette = {top.ItemsPerPallet} produits par palette ; "
                                 : $"Meilleure caisse : {type.Code} – {type.Name} : {top.ItemsPerUnit} produits ; ") +
                             $"{top.ItemsPerLayer} par couche × {top.LayerCount} dans la caisse, volume intérieur rempli à {InnerFill(top, cases):0} %, " +
                             $"caisse extérieure {type.OuterText} mm, {Gross(top):0.##} kg brut" +
                             (Manual(top) && manualHandlingLimit > 0 ? $" (manutention manuelle ≤ {manualHandlingLimit:0.#} kg)." : ".");
        result.Solutions.AddRange(list);
        return result;
    }

    /// <summary>Volume des produits / volume intérieur de la caisse (%).</summary>
    public static double InnerFill(Solution s, IEnumerable<CaseType> cases)
    {
        var type = cases.FirstOrDefault(c => c.Id == s.Base.PalletId);
        var unit = s.FirstUnit;
        if (type == null || unit == null)
        {
            return 0;
        }

        var volume = unit.Items.Sum(p => p.Shape switch
        {
            ShapeKind.CylinderZ => Math.PI * p.DX * p.DY / 4 * p.DZ,
            ShapeKind.CylinderX => Math.PI * p.DY * p.DZ / 4 * p.DX,
            ShapeKind.CylinderY => Math.PI * p.DX * p.DZ / 4 * p.DY,
            _ => p.DX * p.DY * p.DZ
        });
        return volume / (type.InnerLength * type.InnerWidth * type.InnerHeight) * 100;
    }

    /// <summary>Article « caisse » issu d'une solution : dimensions extérieures, poids = produits + tare.</summary>
    public static Article CreateCaseArticle(Article content, Solution solution, CaseSpec spec, string code)
    {
        var weight = solution.FirstUnit!.Items.Sum(p => p.Weight) + spec.Tare;
        return new Article
        {
            Code = code,
            Kind = ArticleKind.Caisse,
            Designation = $"Caisse de {solution.ItemsPerUnit} × {content.Code}",
            Length = Math.Max(spec.OuterLength, spec.OuterWidth),
            Width = Math.Min(spec.OuterLength, spec.OuterWidth),
            Height = spec.OuterHeight,
            Weight = Math.Round(weight, 5),
            Orientation = OrientationRule.HautImpose,
            Client = content.Client,
            Family = content.Family,
            SubFamily = content.SubFamily,
            Notes = $"Colisage : {solution.ItemsPerLayer} par couche × {solution.LayerCount} couche(s), {solution.OrientationText}. " +
                    $"Intérieur {spec.InnerLength:0} × {spec.InnerWidth:0} × {spec.InnerHeight:0} mm, paroi {spec.WallThickness:0.#} mm."
        };
    }
}
