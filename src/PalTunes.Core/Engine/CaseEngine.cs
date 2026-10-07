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

    /// <param name="axis">Tubes, bagues, bobines : axe dans la caisse (Indifferent = horizontal et vertical calculés,
    /// le meilleur est proposé ; null = axe de la fiche article).</param>
    public static PackagingConstraints CaseConstraints(CaseSpec spec, CoilAxis? axis = null) => new()
    {
        ForcedAxis = axis,
        MaxTotalHeight = spec.InnerHeight,
        Gap = spec.Gap,
        CenterLoad = true,
        MaxStackLevels = 1,
        MaxStackedHeight = 0,
        MinSupportPercent = 80,
        // Caisse limitée par sa charge maxi : la dernière couche est remplie jusqu'au poids (sinon, une seule couche trop
        // lourde rendait la caisse impossible : 26 bouteilles de 1,6 kg pour une charge de 30 kg).
        AllowPartialLayer = true
    };

    /// <summary>
    /// Sac tassé à la mise en caisse (case cochée sur l'article) : épaisseur réduite du taux de tassement, empreinte
    /// inchangée (c'est l'air qui est chassé). Même identifiant ; article inchangé s'il n'est pas tassable.
    /// </summary>
    public static Article Pressed(Article article)
    {
        if (article.CompressionRate <= 0)
        {
            return article;
        }

        var pressed = article.Clone();
        pressed.Height = Math.Round(article.Height * (1 - article.CompressionRate), 2);
        return pressed;
    }

    /// <summary>Avertissement de tassement : épaisseur avant / après, fermeture sous presse.</summary>
    public static string? PressNote(Article article) => article.CompressionRate <= 0
        ? null
        : $"Sacs {article.Code} tassés de {(article.CompressionRate * 100).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} % à la mise en caisse : " +
          $"épaisseur {article.Height:0.#} → {Pressed(article).Height:0.#} mm ; fermer la caisse en appuyant (le sac repousse sur le couvercle).";

    public static EngineResult Solve(Article article, CaseSpec spec, int? targetQuantity = null, CaseType? type = null,
        PalletType? pallet = null, PackagingConstraints? palletConstraints = null, CoilAxis? axis = null)
    {
        var pressNote = PressNote(article);
        article = Pressed(article);
        var result = HomogeneousEngine.Solve(article, CaseBase(spec, type), CaseConstraints(spec, axis), targetQuantity);
        if (result.Solutions.Count == 0 && result.Messages.Count > 0 && result.Messages[^1].StartsWith("Aucune disposition", StringComparison.Ordinal))
        {
            result.Messages[^1] = $"{article.DimensionsText} ne tient pas dans une caisse intérieure {spec.InnerLength:0} × {spec.InnerWidth:0} × {spec.InnerHeight:0} mm : agrandissez la caisse ou autorisez une autre orientation de l'article.";
        }

        foreach (var s in result.Solutions)
        {
            s.StackLevels = 1;
            s.StackLimitReason = "";
            // Dans une caisse, les parois tiennent les produits : pas de cornières, cales ni cerclage à prévoir.
            s.Warnings.RemoveAll(w => w.StartsWith("Élancement", StringComparison.Ordinal) ||
                                      w.StartsWith("Tubes couchés", StringComparison.Ordinal) ||
                                      w.StartsWith("Tubes debout élancés", StringComparison.Ordinal) ||
                                      w.StartsWith("Bobines couchées", StringComparison.Ordinal));
            s.Recommendation = s.Recommendation?
                .Replace("produits par conditionnement", "produit(s) par caisse")
                .Replace("plan de couche optimal prouvé", "plan optimal prouvé");
            if (pressNote != null)
            {
                s.Warnings.Add(pressNote);
            }
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

    /// <summary>
    /// Caisse d'un colisage (article non enregistré : dimensions extérieures, poids brut, quantité, contenu) palettisée
    /// sur une palette : meilleure solution homogène, null si la caisse n'est pas palettisable.
    /// </summary>
    public static (Article Box, Solution? Solution) PalletOfCases(Article content, Solution caseSolution, CaseSpec spec, CaseType? type, CoilAxis? axis,
        PalletType pallet, PackagingConstraints constraints, string code)
    {
        var box = CreateCaseArticle(content, caseSolution, spec, code, type, axis);
        if (type != null)
        {
            box.Designation = $"{type.Name} de {caseSolution.ItemsPerUnit} × {content.Code}";
            box.Color = type.Color;
        }

        var best = HomogeneousEngine.Solve(box, BaseInfo.From(pallet, false, 1, 1), constraints).Recommended;
        return (box, best?.FirstUnit == null ? null : best);
    }

    /// <summary>
    /// Article « caisse » d'une caisse mixte (colisage hétérogène) : dimensions extérieures, poids brut de cette caisse,
    /// quantité totale de produits ; contenu détaillé (articles × quantités, caisse) enregistré pour recalculer et
    /// réimprimer la fiche de colisage, et rappelé dans la désignation et les notes.
    /// </summary>
    public static Article CreateMixedCaseArticle(LoadUnit unit, CaseSpec spec, CaseType? type, string code, Func<Guid, string> codeOf, CoilAxis? axis = null)
    {
        var groups = unit.Items.GroupBy(p => p.ArticleId).OrderByDescending(g => g.Count()).ToList();
        var content = string.Join(" + ", groups.Select(g => $"{g.Count()} × {codeOf(g.Key)}"));
        return new Article
        {
            Code = code,
            Kind = ArticleKind.Caisse,
            QuantityPerCase = unit.Items.Count,
            Designation = $"{type?.Name ?? "Caisse"} mixte : {content}",
            Length = Math.Max(spec.OuterLength, spec.OuterWidth),
            Width = Math.Min(spec.OuterLength, spec.OuterWidth),
            Height = spec.OuterHeight,
            Weight = Math.Round(unit.Items.Sum(p => p.Weight) + spec.Tare, 5),
            Orientation = OrientationRule.HautImpose,
            Color = type?.Color,
            Notes = $"Colisage mixte : {content}. Intérieur {spec.InnerLength:0} × {spec.InnerWidth:0} × {spec.InnerHeight:0} mm, paroi {spec.WallThickness:0.#} mm.",
            CaseContent = groups.Count == 0 ? null : new CaseContent(groups[0].Key, type?.Code, spec.InnerLength, spec.InnerWidth, spec.InnerHeight, spec.WallThickness,
                spec.Tare, spec.Gap, axis, groups.Select(g => new CaseContentLine(g.Key, g.Count())).ToList())
        };
    }

    /// <summary>
    /// Colisage d'un article caisse créé au colisage, recalculé pour la fiche (produit, caisse, solution). Caisse mixte :
    /// <see cref="Lines"/> (articles × quantités) et <see cref="Unit"/> (la caisse montrée, la première sinon) ;
    /// <see cref="Content"/> est alors l'article le plus nombreux.
    /// </summary>
    public sealed record CaseSheet(Article Content, CaseType? Type, CaseSpec Spec, Solution Solution, CoilAxis? Axis,
        IReadOnlyList<(Article Article, int Quantity)>? Lines = null, LoadUnit? Unit = null)
    {
        public bool IsMixed => Lines is { Count: > 1 } || Solution.Kind == PackagingKind.Heterogene;

        /// <summary>Caisse montrée sur la fiche.</summary>
        public LoadUnit ShownUnit => Unit ?? Solution.FirstUnit!;

        /// <summary>Contenu de la caisse montrée : article × quantité (le plus nombreux d'abord).</summary>
        public IReadOnlyList<(Article Article, int Quantity)> ContentLines(Func<Guid, Article?> find) => IsMixed
            ? ShownUnit.Items.GroupBy(p => p.ArticleId).OrderByDescending(g => g.Count())
                .Select(g => (find(g.Key) ?? Lines?.FirstOrDefault(l => l.Article.Id == g.Key).Article ?? Content, g.Count())).ToList()
            : [(Content, ShownUnit.Items.Count)];

        /// <summary>« 350 × BAG0000001 + 100 × BAG0000011 ».</summary>
        public string ContentText(Func<Guid, Article?> find) =>
            string.Join(" + ", ContentLines(find).Select(l => $"{l.Quantity.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} × {l.Article.Code}"));
    }

    /// <summary>
    /// Fiche de colisage possible pour cet article : caisse dont le contenu est connu (créée au colisage) et dont le
    /// produit existe toujours.
    /// </summary>
    public static bool CanRebuild(Article? box, Func<Guid, Article?> findArticle) =>
        box is { Kind: ArticleKind.Caisse, CaseContent: { } link } &&
        (link.IsMixed ? link.Lines!.All(l => findArticle(l.ArticleId) != null) : findArticle(link.ArticleId) != null);

    /// <summary>
    /// Recalcule le colisage d'un article caisse : même produit, même caisse, quantité par caisse imposée si renseignée.
    /// Null si le contenu est inconnu ou ne tient plus dans la caisse.
    /// </summary>
    public static CaseSheet? Rebuild(Article box, Func<Guid, Article?> findArticle, IEnumerable<CaseType> cases)
    {
        if (box is not { Kind: ArticleKind.Caisse, CaseContent: { } link } || !CanRebuild(box, findArticle) || findArticle(link.ArticleId) is not { } content)
        {
            return null;
        }

        var type = link.CaseTypeCode == null
            ? null
            : cases.FirstOrDefault(c => string.Equals(c.Code, link.CaseTypeCode, StringComparison.OrdinalIgnoreCase));
        var spec = type?.ToSpec(link.Gap) ?? new CaseSpec
        {
            InnerLength = link.InnerLength,
            InnerWidth = link.InnerWidth,
            InnerHeight = link.InnerHeight,
            WallThickness = link.WallThickness,
            Tare = link.Tare,
            Gap = link.Gap
        };
        if (link.IsMixed)
        {
            // Caisse mixte : mêmes articles × quantités dans la même caisse, une seule caisse attendue.
            var lines = link.Lines!.Select(l => (findArticle(l.ArticleId)!, l.Quantity)).ToList();
            var mixed = SolveMixed(lines, spec, type, axis: link.Axis);
            var best = mixed.Solutions.Where(s => s.Units.Count == 1 && s.UnplacedItems == 0).OrderByDescending(s => s.Recommended).FirstOrDefault()
                       ?? mixed.Recommended ?? mixed.Solutions.FirstOrDefault();
            return best is { FirstUnit: not null } ? new CaseSheet(content, type, spec, best, link.Axis, lines) : null;
        }

        var result = Solve(content, spec, box.CaseQuantity, type, axis: link.Axis);
        var solution = result.Solutions.FirstOrDefault(s => box.CaseQuantity is { } q && s.ItemsPerUnit == q)
                       ?? result.Recommended
                       ?? result.Solutions.FirstOrDefault();
        return solution == null ? null : new CaseSheet(content, type, spec, solution, link.Axis);
    }

    /// <summary>Caisses du catalogue dans lesquelles l'article tient (au moins un produit, poids compris).</summary>
    public static List<CaseType> PossibleCases(Article article, IEnumerable<CaseType> cases, double gap = 0, CoilAxis? axis = null) =>
        cases.Where(c => c.Validate().Count == 0 && (c.MaxWeight <= 0 || article.Weight <= c.MaxWeight) &&
                         Solve(article, c.ToSpec(gap), 1, c, axis: axis).Solutions.Count > 0).ToList();

    /// <summary>Produits cylindriques debout (axe vertical) dans la solution.</summary>
    public static bool IsUpright(Solution s) => s.FirstUnit is { Items.Count: > 0 } u && u.Items[0].Shape == ShapeKind.CylinderZ;

    /// <summary>
    /// Pourquoi aucune caisse active ne convient : trop lourd (charge maxi des caisses), trop grand (dimensions
    /// intérieures), ou les deux ; poids unitaire suspect signalé (erreur de saisie la plus fréquente).
    /// </summary>
    public static string Diagnose(Article article, IEnumerable<CaseType> cases, double gap = 0, CoilAxis? axis = null)
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var active = cases.Where(c => c.Active && c.Validate().Count == 0).ToList();
        var name = $"{article.Code} ({article.DimensionsText} mm, {article.Weight.ToString("0.#####", fr)} kg)";
        if (active.Count == 0)
        {
            return $"Aucune caisse active dans le catalogue : activez ou ajoutez des caisses (espace Caisses).";
        }

        bool Fits(CaseType c)
        {
            var spec = c.ToSpec(gap);
            spec.MaxWeight = 0;
            return Solve(article, spec, 1, c, axis: axis).Solutions.Count > 0;
        }

        var fitting = active.Where(Fits).ToList();
        var parts = new List<string>();
        if (fitting.Count == 0)
        {
            var largest = active.OrderByDescending(c => c.InnerLength * c.InnerWidth * c.InnerHeight).First();
            parts.Add($"Aucune caisse active pour {name} : il ne tient dans aucune caisse (la plus grande : {largest.Code}, intérieur {largest.InnerText} mm).");
        }
        else
        {
            var maxLoad = fitting.Max(c => c.MaxWeight <= 0 ? double.MaxValue : c.MaxWeight);
            parts.Add(maxLoad == double.MaxValue
                ? $"Aucune caisse active pour {name}."
                : $"Aucune caisse active pour {name} : le poids unitaire dépasse la charge maxi de toutes les caisses où il tient " +
                  $"({fitting.Count} caisse(s), charge maxi {fitting.Min(c => c.MaxWeight).ToString("0.#", fr)} à {maxLoad.ToString("0.#", fr)} kg).");
        }

        if (ArticleSchema.WeightWarning(article) is { } warning)
        {
            parts.Add(warning);
        }

        return string.Join(" ", parts);
    }

    /// <summary>Poids brut maximal d'une caisse manutentionnée à la main (NF X35-109 : 25 kg, valeur courante).</summary>
    public const double DefaultManualHandlingLimit = 25;

    // ------------------------------------------------------------------ Colisage hétérogène (plusieurs articles)

    /// <summary>
    /// Plusieurs articles dans une caisse donnée : moteur hétérogène dans le volume intérieur (mêmes règles d'appui,
    /// de charge et d'ordre lourd / léger qu'une palette), autant de caisses identiques que nécessaire. Poids des produits
    /// par caisse limité par la charge maxi de la caisse et, si <paramref name="manualHandlingLimit"/> &gt; 0, par le
    /// poids brut manutentionnable à la main. Palette de destination : caisses par palette et palettes nécessaires.
    /// </summary>
    public static EngineResult SolveMixed(IReadOnlyList<(Article Article, int Quantity)> lines, CaseSpec spec, CaseType? type = null,
        PalletType? pallet = null, PackagingConstraints? palletConstraints = null, CoilAxis? axis = null, double manualHandlingLimit = 0)
    {
        var constraints = CaseConstraints(spec, axis);
        var limits = new List<double>();
        if (spec.MaxWeight > 0)
        {
            limits.Add(spec.MaxWeight);
        }

        string? manualNote = null;
        var heaviest = lines.Count == 0 ? 0 : lines.Max(l => l.Article.Weight);
        if (manualHandlingLimit > 0)
        {
            if (heaviest + spec.Tare <= manualHandlingLimit + 1e-9)
            {
                limits.Add(manualHandlingLimit - spec.Tare);
            }
            else
            {
                manualNote = $"Un produit seul dépasse {manualHandlingLimit:0.#} kg brut en caisse : limite de manutention à la main ignorée.";
            }
        }

        if (limits.Count > 0)
        {
            constraints.MaxLoadWeight = limits.Min();
        }

        var pressNotes = lines.Select(l => PressNote(l.Article)).OfType<string>().ToList();
        var result = HeterogeneousEngine.Solve([.. lines.Select(l => (Pressed(l.Article), l.Quantity))], CaseBase(spec, type), constraints);
        foreach (var s in result.Solutions)
        {
            s.StackLevels = 1;
            s.StackLimitReason = "";
            // Dans une caisse, les parois tiennent les produits : ni élancement, ni centre de gravité à surveiller.
            s.Warnings.RemoveAll(w => w.Contains("élancement", StringComparison.OrdinalIgnoreCase) ||
                                      w.Contains("centre de gravité", StringComparison.Ordinal) ||
                                      w.StartsWith("Tubes couchés", StringComparison.Ordinal) ||
                                      w.StartsWith("Bobines couchées", StringComparison.Ordinal));
            for (var i = 0; i < s.Warnings.Count; i++)
            {
                s.Warnings[i] = s.Warnings[i].Replace("Unité ", "Caisse ").Replace("ne tient pas sur la base", "ne tient pas dans la caisse");
            }

            for (var i = 0; i < s.Violations.Count; i++)
            {
                s.Violations[i] = s.Violations[i].Replace("Unité ", "Caisse ").Replace("sur cette base", "dans cette caisse");
            }

            if (manualNote != null)
            {
                s.Warnings.Add(manualNote);
            }

            s.Warnings.AddRange(pressNotes);

            s.Title = s.Title.Replace("unité(s)", "caisse(s)");
            s.Recommendation = s.Recommendation?.Replace("unité(s) de charge", "caisse(s)");
            if (pallet != null && s.Units.Count > 0)
            {
                PalletizeMixed(s, spec, pallet, palletConstraints);
            }
        }

        return result;
    }

    /// <summary>
    /// Caisses mixtes sur la palette de destination : toutes ont les mêmes dimensions extérieures ; le poids retenu est
    /// celui de la plus lourde (prudent). Caisses par palette, puis palettes nécessaires pour toutes les caisses.
    /// </summary>
    public static void PalletizeMixed(Solution s, CaseSpec spec, PalletType pallet, PackagingConstraints? palletConstraints)
    {
        var box = new Article
        {
            Code = CaseCode,
            Kind = ArticleKind.Caisse,
            Length = Math.Max(spec.OuterLength, spec.OuterWidth),
            Width = Math.Min(spec.OuterLength, spec.OuterWidth),
            Height = spec.OuterHeight,
            Weight = Math.Max(0.001, s.Units.Max(u => u.Items.Sum(p => p.Weight)) + spec.Tare),
            Orientation = OrientationRule.HautImpose
        };
        var best = HomogeneousEngine.Solve(box, BaseInfo.From(pallet, false, 1, 1), palletConstraints ?? new PackagingConstraints()).Recommended;
        s.DestinationPallet = pallet.Code;
        s.CasesPerPallet = best is { IsCompliant: true } ? best.ItemsPerUnit : 0;
        s.PalletCount = s.CasesPerPallet > 0 ? (int)Math.Ceiling(s.Units.Count / (double)s.CasesPerPallet) : 0;
        s.ItemsPerPallet = s.PalletCount > 0 ? (int)Math.Ceiling(s.TotalItems / (double)s.PalletCount) : 0;
        if (s.CasesPerPallet == 0)
        {
            s.Warnings.Add($"Caisse non palettisable sur {pallet.Code} avec les contraintes saisies (dimensions, hauteur ou poids).");
        }
    }

    /// <summary>
    /// Meilleure caisse du catalogue pour une composition de plusieurs articles. Pour chaque caisse active où chaque
    /// article tient seul, la solution recommandée du moteur hétérogène ; classement : caisses manutentionnables à la
    /// main d'abord, puis (palette connue) le moins de palettes, puis le plus petit volume total de caisses (meilleur
    /// remplissage), puis le moins de caisses.
    /// </summary>
    public static EngineResult ProposeMixed(IReadOnlyList<(Article Article, int Quantity)> lines, IEnumerable<CaseType> cases, double gap = 0,
        double manualHandlingLimit = DefaultManualHandlingLimit, PalletType? pallet = null, PackagingConstraints? palletConstraints = null,
        CoilAxis? axis = null)
    {
        var result = new EngineResult();
        var list = new List<(Solution Solution, CaseType Case)>();
        var refused = new List<string>();
        foreach (var c in cases.Where(c => c.Active && c.Validate().Count == 0))
        {
            var spec = c.ToSpec(gap);
            var misfit = lines.FirstOrDefault(l => !(c.MaxWeight <= 0 || l.Article.Weight <= c.MaxWeight) ||
                                                   Solve(l.Article, spec, 1, c, axis: axis).Solutions.Count == 0).Article;
            if (misfit != null)
            {
                refused.Add($"{c.Code} ({misfit.Code})");
                continue;
            }

            var solved = SolveMixed(lines, spec, c, pallet, palletConstraints, axis, manualHandlingLimit);
            if (solved.Recommended is { IsCompliant: true, Units.Count: > 0 } best && best.ExcludedArticles.Count == 0 && best.UnplacedItems == 0)
            {
                best.Recommended = false;
                best.Recommendation = null;
                best.Title = $"{c.Code} · {best.Units.Count} caisse(s)";
                list.Add((best, c));
            }
        }

        if (list.Count == 0)
        {
            result.Messages.Add(refused.Count > 0
                ? $"Aucune caisse active ne convient à toute la composition : un article ne tient pas (ou est trop lourd) dans {string.Join(", ", refused.Take(6))}{(refused.Count > 6 ? "…" : "")}."
                : "Aucune caisse active dans le catalogue : activez ou ajoutez des caisses (espace Caisses).");
            return result;
        }

        double Fill((Solution Solution, CaseType Case) x) => x.Solution.Units.Average(u => u.Metrics.FillRate);
        double Volume((Solution Solution, CaseType Case) x) => x.Case.InnerLength * x.Case.InnerWidth * x.Case.InnerHeight;
        double Gross((Solution Solution, CaseType Case) x) => x.Solution.Units.Max(u => u.Items.Sum(p => p.Weight)) + x.Case.Tare;
        bool Manual((Solution Solution, CaseType Case) x) => manualHandlingLimit <= 0 || Gross(x) <= manualHandlingLimit + 1e-9;
        foreach (var x in list.Where(x => !Manual(x)))
        {
            x.Solution.Warnings.Add($"Poids brut jusqu'à {Gross(x):0.##} kg > {manualHandlingLimit:0.#} kg : manutention mécanisée nécessaire.");
        }

        // Caisses manutentionnables à la main d'abord ; puis, palette connue, le moins de palettes ; puis le plus petit
        // volume total de caisses (meilleur remplissage) ; puis le moins de caisses.
        var ranked = list
            .OrderBy(x => Manual(x) ? 0 : 1)
            .ThenBy(x => pallet == null ? 0 : x.Solution.PalletCount > 0 ? x.Solution.PalletCount : int.MaxValue)
            .ThenBy(x => x.Solution.Units.Count * Volume(x))
            .ThenBy(x => x.Solution.Units.Count)
            .Take(12)
            .ToList();
        var top = ranked[0].Solution;
        top.Recommended = true;
        top.Recommendation = $"Recommandée : {ranked[0].Case.Code} – {ranked[0].Case.Name}, {top.Units.Count} caisse(s) pour {top.TotalItems} produit(s), " +
                             $"remplissage moyen {Fill(ranked[0]):0} %" +
                             (pallet != null && top.PalletCount > 0 ? $", {top.CasesPerPallet} caisses par palette {pallet.Code} → {top.PalletCount} palette(s)." : ".");
        result.Solutions.AddRange(ranked.Select(x => x.Solution));
        if (refused.Count > 0)
        {
            result.Messages.Add($"{refused.Count} caisse(s) écartée(s) : un article n'y tient pas seul.");
        }

        return result;
    }

    /// <summary>
    /// Meilleure composition en caisse (catalogue actif) : pour chaque caisse possible, la solution recommandée.
    /// Classement : caisses manutentionnables à la main (poids brut ≤ <paramref name="manualHandlingLimit"/>, 0 = ignoré)
    /// d'abord, puis taux de remplissage du volume intérieur, puis nombre de produits, puis caisse la plus légère.
    /// </summary>
    public static EngineResult Propose(Article article, IEnumerable<CaseType> cases, double gap = 0, int? targetQuantity = null,
        double manualHandlingLimit = DefaultManualHandlingLimit, PalletType? pallet = null, PackagingConstraints? palletConstraints = null,
        CoilAxis? axis = null)
    {
        var result = new EngineResult();
        var list = new List<Solution>();
        foreach (var c in cases.Where(c => c.Active && c.Validate().Count == 0))
        {
            var solved = Solve(article, c.ToSpec(gap), targetQuantity, c, pallet, palletConstraints, axis);
            // Tubes, bagues, bobines : la meilleure solution debout et la meilleure couchée de chaque caisse sont proposées,
            // comme en palettisation (axe horizontal ou vertical, le meilleur est recommandé).
            foreach (var group in solved.Solutions.Where(s => s.IsCompliant && s.FirstUnit is { Items.Count: > 0 })
                         .GroupBy(s => article.Kind is ArticleKind.Tube or ArticleKind.Bobine ? (IsUpright(s) ? 1 : 2) : 0))
            {
                var best = group.OrderByDescending(s => s.Recommended).ThenByDescending(s => s.ItemsPerUnit).ThenByDescending(s => s.Score).First();
                best.Recommended = false;
                best.Recommendation = null;
                best.Title = $"{c.Code} · {best.ItemsPerUnit} produits" + group.Key switch { 1 => " debout", 2 => " couchés", _ => "" };
                list.Add(best);
            }
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
            result.Messages.Add(Diagnose(article, cases, gap));
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

    /// <summary>
    /// Article « caisse » issu d'une solution : dimensions extérieures, poids = produits + tare, quantité par caisse et
    /// contenu (produit, caisse) pour la fiche de colisage.
    /// </summary>
    public static Article CreateCaseArticle(Article content, Solution solution, CaseSpec spec, string code, CaseType? type = null, CoilAxis? axis = null)
    {
        var weight = solution.FirstUnit!.Items.Sum(p => p.Weight) + spec.Tare;
        return new Article
        {
            Code = code,
            Kind = ArticleKind.Caisse,
            QuantityPerCase = solution.ItemsPerUnit,
            CaseContent = new CaseContent(content.Id, type?.Code, spec.InnerLength, spec.InnerWidth, spec.InnerHeight, spec.WallThickness, spec.Tare,
                spec.Gap, axis),
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
