using System.Globalization;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.Core.Export;

/// <summary>Section de l'explication d'un calcul : titre et lignes (une étape ou une règle par ligne).</summary>
public sealed record DetailSection(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// Détails du calcul, pas à pas et avec les chiffres de la solution affichée : données retenues, surface et hauteur
/// utiles, plan de couche, nombre de couches et facteur limitant, poids, gerbage ; hétérogène : stratégie, bornes et
/// règles ; colisage : caisse, limites de poids, palettisation des caisses. Les quantités (par palette, par colisage)
/// sont expliquées dans leur propre section, colisage des caisses de la palette compris.
/// </summary>
public static class CalculationDetails
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Cotes au dixième de mm : Ø74,2 reste 74,2 (un arrondi à 74 rendrait les calculs affichés faux en apparence).</summary>
    private static string Mm(double v) => Math.Round(v, 1).ToString("#,0.#", Fr);

    /// <summary>Surface en m², assez de chiffres pour refaire le calcul (0,005701 m² ; 1,92 m²).</summary>
    private static string M2(double mm2) => (mm2 / 1e6).ToString(mm2 < 1e5 ? "0.######" : "0.####", Fr);
    private static string Kg(double v) => v.ToString("#,0.###", Fr);
    private static string N(int v) => v.ToString("#,0", Fr);
    private static string Pct(double v) => v.ToString("0.#", Fr);
    private static string D(double v) => v.ToString("#,0.##", Fr);

    /// <summary>Texte brut des sections (titre souligné, une ligne par étape) : copie vers un courriel ou un document.</summary>
    public static string ToText(IEnumerable<DetailSection> sections, string? title = null)
    {
        var sb = new System.Text.StringBuilder();
        if (title != null)
        {
            sb.AppendLine(title).AppendLine(new string('=', title.Length)).AppendLine();
        }

        foreach (var section in sections)
        {
            sb.AppendLine(section.Title).AppendLine(new string('-', section.Title.Length));
            foreach (var line in section.Lines)
            {
                sb.AppendLine(line.StartsWith("   ", StringComparison.Ordinal) ? line : "• " + line);
            }

            sb.AppendLine();
        }

        return sb.ToString().Replace('\u202F', ' ');
    }

    // ------------------------------------------------------------------ Palette

    /// <summary>
    /// Conditionnement sur palette (homogène ou hétérogène), unité affichée. <paramref name="colisage"/> recalcule le
    /// colisage d'un article caisse (null : quantité par caisse saisie seule).
    /// </summary>
    public static List<DetailSection> Pallet(Solution s, LoadUnit unit, PackagingConstraints c, Article? article, Func<Guid, Article?> find,
        Func<Article, CaseEngine.CaseSheet?>? colisage = null)
    {
        var b = s.Base;
        var sections = new List<DetailSection>();
        var usableX = b.Length + 2 * (c.OverhangLength - c.CornerInset);
        var usableY = b.Width + 2 * (c.OverhangWidth - c.CornerInset);
        var usableH = c.MaxTotalHeight - b.PalletHeight - c.CapHeight;
        var capacity = c.MaxLoadWeight > 0 ? c.MaxLoadWeight : b.DynamicCapacity;

        var baseLines = new List<string>
        {
            b.PhysicalCount == 1
                ? $"Palette {b.PalletCode} ({b.PalletName}) : {Mm(b.Length)} × {Mm(b.Width)} mm, plancher de {Mm(b.PalletHeight)} mm."
                : $"{b.PhysicalCount} palettes {b.PalletCode} ({b.CountAlongLength} × {b.CountAlongWidth}) : base {Mm(b.Length)} × {Mm(b.Width)} mm, plancher de {Mm(b.PalletHeight)} mm.",
            $"Surface utile = base + 2 × débord{(c.CornerInset > 0 ? " − 2 × cornière" : "")} = ({Mm(b.Length)} + 2 × {Mm(c.OverhangLength - c.CornerInset)}) × ({Mm(b.Width)} + 2 × {Mm(c.OverhangWidth - c.CornerInset)}) = {Mm(usableX)} × {Mm(usableY)} mm.",
            $"Hauteur utile = hauteur totale maxi − plancher{(c.CapHeight > 0 ? " − coiffe" : "")} = {Mm(c.MaxTotalHeight)} − {Mm(b.PalletHeight)}{(c.CapHeight > 0 ? $" − {Mm(c.CapHeight)}" : "")} = {Mm(usableH)} mm.",
            capacity > 0
                ? $"Charge maxi = {(c.MaxLoadWeight > 0 ? "poids maxi saisi" : "charge dynamique de la palette")} : {Kg(capacity)} kg."
                : "Charge maxi : non limitée."
        };
        sections.Add(new("Base et limites", baseLines));

        if (s.Kind == PackagingKind.Homogene && article != null)
        {
            sections.AddRange(Homogeneous(s, unit, c, article, usableX, usableY, usableH, capacity, "palette"));
        }
        else
        {
            sections.AddRange(Heterogeneous(s, c, usableX, usableY, usableH, capacity, find, "palette"));
        }

        sections.Add(PalletQuantity(s, unit, article, find));
        if (ColisageQuantity(s, article, find, colisage) is { } col)
        {
            sections.Add(col);
        }

        var m = unit.Metrics;
        sections.Add(new("Poids et encombrement", new List<string>
        {
            $"Poids de la charge = {N(unit.Items.Count)} {Units(unit.Items, find)} = {Kg(m.LoadWeight)} kg{(capacity > 0 ? $" (≤ {Kg(capacity)} kg)" : "")}.",
            $"Poids total = charge + accessoires + palette(s) = {Kg(m.LoadWeight)} + {Kg(m.AccessoriesWeight)} + {Kg(b.Tare)} = {Kg(m.TotalWeight)} kg.",
            $"Hauteur d'encombrement = plancher + charge{(c.CapHeight > 0 ? " + coiffe" : "")} = {Mm(m.EnclosureHeight)} mm (maxi {Mm(c.MaxTotalHeight)} mm).",
            $"Encombrement au sol : {Mm(m.EnclosureLength)} × {Mm(m.EnclosureWidth)} mm (charge {Mm(m.LoadLength)} × {Mm(m.LoadWidth)} mm).",
            $"Remplissage = volume des produits / (surface utile × hauteur de charge) = {Pct(m.FillRate)} % ; surface au sol couverte {Pct(m.AreaRate)} %.",
            $"Remplissage de la capacité = volume des produits / (surface utile × hauteur utile) = {Pct(m.FillRate)} % × {Mm(m.LoadHeight)} / {Mm(usableH)} = {Pct(m.FillRate * m.LoadHeight / Math.Max(1, usableH))} % " +
            "(le premier mesure la compacité de la charge sur sa hauteur réelle, le second la part du volume disponible réellement utilisée)." +
            (unit.Items.Any(p => p.Shape != ShapeKind.Box) ? " Produits ronds : volume du cylindre extérieur, pas de la matière." : "")
        }));

        if (s.StackLevels > 1 || !string.IsNullOrEmpty(s.StackLimitReason))
        {
            sections.Add(new("Gerbage des palettes", new List<string>
            {
                s.StackLevels > 1
                    ? $"{s.StackLevels - 1} conditionnement(s) peuvent être gerbés sur le premier ({s.StackLevels} niveaux, hauteur gerbée {Mm(m.EnclosureHeight * s.StackLevels)} mm)."
                    : "Non gerbable.",
                string.IsNullOrEmpty(s.StackLimitReason) ? "" : $"Limite : {s.StackLimitReason}."
            }.Where(l => l.Length > 0).ToList()));
        }

        sections.Add(PalletOperations(s, unit, c, article, find, usableX, usableY, usableH, capacity));
        return sections;
    }

    // ------------------------------------------------------------------ Colisage

    /// <summary>Colisage (un article ou plusieurs), caisse affichée.</summary>
    public static List<DetailSection> Case(Solution s, LoadUnit unit, CaseSpec spec, CaseType? type, Article? article, double manualLimit, Func<Guid, Article?> find)
    {
        var sections = new List<DetailSection>();
        var c = CaseEngine.CaseConstraints(spec);
        var weightLimit = spec.MaxWeight > 0 ? spec.MaxWeight : 0;
        var caseLines = new List<string>
        {
            type != null ? $"Caisse du catalogue {type.Code} – {type.Name} ({type.MaterialLabel})." : "Caisse spécifique (caractéristiques saisies).",
            $"Volume intérieur utile : {Mm(spec.InnerLength)} × {Mm(spec.InnerWidth)} × {Mm(spec.InnerHeight)} mm ; parois de {spec.WallThickness.ToString("0.#", Fr)} mm, " +
            $"d'où l'extérieur {Mm(spec.OuterLength)} × {Mm(spec.OuterWidth)} × {Mm(spec.OuterHeight)} mm (intérieur + 2 × paroi).",
            weightLimit > 0 ? $"Charge maxi de la caisse : {Kg(weightLimit)} kg de produits." : "Charge de la caisse : non limitée.",
            manualLimit > 0
                ? $"Manutention à la main : poids brut ≤ {Kg(manualLimit)} kg (soit ≤ {Kg(manualLimit - spec.Tare)} kg de produits avec la tare de {Kg(spec.Tare)} kg), appliqué quand chaque produit le permet."
                : "Manutention à la main : non limitée.",
            spec.Gap > 0 ? $"Jeu entre produits : {spec.Gap.ToString("0.#", Fr)} mm." : "Pas de jeu entre produits."
        };
        sections.Add(new("Caisse et limites", caseLines));

        var pressed = (s.Kind == PackagingKind.Homogene && article != null ? [article] : s.Units.SelectMany(u => u.Items).Select(x => x.ArticleId).Distinct().Select(find).OfType<Article>())
            .Where(x => x.CompressionRate > 0).ToList();
        if (pressed.Count > 0)
        {
            sections.Add(new("Tassement à la mise en caisse", PressLines(pressed, spec)));
        }

        if (s.Kind == PackagingKind.Homogene && article != null)
        {
            sections.AddRange(Homogeneous(s, unit, c, article, spec.InnerLength, spec.InnerWidth, spec.InnerHeight, weightLimit, "caisse"));
        }
        else
        {
            sections.AddRange(Heterogeneous(s, c, spec.InnerLength, spec.InnerWidth, spec.InnerHeight, weightLimit, find, "caisse"));
        }

        sections.Add(CaseQuantity(s, unit, spec, article, find));

        var products = unit.Items.Sum(p => p.Weight);
        sections.Add(new("Poids de la caisse", new List<string>
        {
            $"Poids des produits = {N(unit.Items.Count)} produits = {Kg(products)} kg.",
            $"Poids brut = produits + tare = {Kg(products)} + {Kg(spec.Tare)} = {Kg(products + spec.Tare)} kg.",
            $"Remplissage du volume intérieur : {Pct(unit.Metrics.FillRate)} %."
        }));

        if (s.DestinationPallet != null)
        {
            var lines = new List<string>
            {
                $"Chaque caisse devient un pavé de {Mm(Math.Max(spec.OuterLength, spec.OuterWidth))} × {Mm(Math.Min(spec.OuterLength, spec.OuterWidth))} × {Mm(spec.OuterHeight)} mm, haut imposé, palettisé sur {s.DestinationPallet} par le moteur homogène.",
                s.CasesPerPallet > 0 ? $"Caisses par palette : {N(s.CasesPerPallet)}." : $"Caisse non palettisable sur {s.DestinationPallet} (dimensions, hauteur ou poids)."
            };
            if (s.CasesPerPallet > 0)
            {
                lines.Add(s.Kind == PackagingKind.Homogene
                    ? $"Produits par palette = caisses × quantité par caisse = {N(s.CasesPerPallet)} × {N(s.ItemsPerUnit)} = {N(s.ItemsPerPallet)}."
                    : $"Palettes nécessaires = ⌈{N(s.Units.Count)} caisses / {N(s.CasesPerPallet)}⌉ = {N(s.PalletCount)} (poids retenu : celui de la caisse la plus lourde).");
            }

            sections.Add(new("Palettisation des caisses", lines));
        }

        sections.Add(CaseOperations(s, unit, spec, c, article, find, weightLimit));
        return sections;
    }

    // ------------------------------------------------------------------ Quantités

    /// <summary>Quantité par palette : produits par couche × couches (homogène), produits par article (hétérogène), contenu des caisses.</summary>
    private static DetailSection PalletQuantity(Solution s, LoadUnit unit, Article? article, Func<Guid, Article?> find)
    {
        var lines = new List<string>();
        if (s.Kind == PackagingKind.Homogene && article != null)
        {
            var noun = article.CaseQuantity != null ? "colis" : "produit(s)";
            var beds = s.Pattern == StackPattern.Quinconce && unit.Items.FirstOrDefault()?.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY;
            lines.Add(!s.PartialTopLayer && LayerSum(unit, beds) is { } sum
                ? $"Quantité par palette = somme des {(beds ? "lits" : "couches")} = {sum} = {N(s.ItemsPerUnit)} {noun}."
                : $"Quantité par palette = produits par {(beds ? "lit" : "couche")} × nombre de {(beds ? "lits" : "couches")} = {N(s.ItemsPerLayer)} × {N(s.LayerCount)}" +
                  (s.PartialTopLayer ? $" (dernière couche incomplète) = {N(s.ItemsPerUnit)} {noun}." : $" = {N(s.ItemsPerUnit)} {noun}."));
            if (s.PartialTopLayer && s.LayerCount > 0)
            {
                var full = s.ItemsPerLayer * (s.LayerCount - 1);
                lines.Add($"Détail : {N(s.LayerCount - 1)} couche(s) complète(s) × {N(s.ItemsPerLayer)} = {N(full)}, plus {N(s.ItemsPerUnit - full)} sur la dernière couche.");
            }

            if (s.Units.Count > 1)
            {
                var all = s.Units.Sum(u => u.Items.Count);
                lines.Add($"Quantité totale : {N(all)} {noun} sur {N(s.Units.Count)} palettes = ⌈{N(all)} / {N(s.Units[0].Items.Count)}⌉" +
                          $" (cette palette : {N(unit.Items.Count)} ; dernière palette : {N(s.Units[^1].Items.Count)}).");
            }

            var m = unit.Metrics;
            lines.Add($"Contrôle : {N(unit.Items.Count)} × {Kg(article.Weight)} kg = {Kg(m.LoadWeight)} kg de charge" +
                      (m.TotalWeight > 0 ? $", {Kg(m.TotalWeight)} kg au total" : "") +
                      $" ; hauteur {Mm(m.EnclosureHeight)} mm ; chaque produit compté une fois, aucun chevauchement (contrôle géométrique de la solution).");

            if (article.CaseQuantity is { } q)
            {
                lines.Add($"Chaque colis contient {N(q)} produit(s){ContentOf(article, find)} (quantité par caisse) : " +
                          $"produits contenus par palette = {N(s.ItemsPerUnit)} × {N(q)} = {N(s.ItemsPerUnit * q)}.");
                if (article.CaseContent is { IsMixed: true } mixed)
                {
                    lines.Add("Dont " + string.Join(" + ", mixed.Lines!.Select(l => $"{N(s.ItemsPerUnit)} × {N(l.Quantity)} = {N(s.ItemsPerUnit * l.Quantity)} {find(l.ArticleId)?.Code ?? "?"}")) + ".");
                }
            }

            return new("Quantité par palette", lines);
        }

        foreach (var u in s.Units)
        {
            var parts = u.Items.GroupBy(p => p.ArticleId).Select(g => $"{find(g.Key)?.Code ?? "?"} × {N(g.Count())}").ToList();
            lines.Add($"{(s.Units.Count > 1 ? $"Palette {u.Index}" : "Palette")} : {string.Join(" + ", parts)} = {N(u.Items.Count)} {Units(u.Items, find)}.");
        }

        var colis = 0;
        var contained = 0;
        var loose = 0;
        foreach (var g in s.Units.SelectMany(x => x.Items).GroupBy(p => p.ArticleId))
        {
            if (find(g.Key) is not { CaseQuantity: { } } )
            {
                loose += g.Count();
            }

            if (find(g.Key) is { CaseQuantity: { } q } box)
            {
                colis += g.Count();
                contained += g.Count() * q;
                lines.Add($"{box.Code} : {N(g.Count())} colis × {N(q)} = {N(g.Count() * q)} produit(s){ContentOf(box, find)} contenus" +
                          (box.CaseContent is { IsMixed: true } mixed
                              ? $" (dont {string.Join(" + ", mixed.Lines!.Select(l => $"{N(g.Count() * l.Quantity)} {find(l.ArticleId)?.Code ?? "?"}"))})."
                              : "."));
            }
        }

        if (colis > 0)
        {
            lines.Add($"Total : {N(colis)} colis (unités manutentionnées) contenant {N(contained)} produits" +
                      (loose > 0 ? $", plus {N(loose)} produit(s) posé(s) sans caisse, soit {N(contained + loose)} produits en tout." : "."));
        }

        if (s.UnplacedItems > 0)
        {
            lines.Add($"{N(s.UnplacedItems)} produit(s) demandé(s) non placé(s) sur {N(s.RequestedItems)}.");
        }

        return new("Quantité par palette", lines);
    }

    /// <summary>« colis » quand tous les produits posés sont des caisses (quantité par caisse), sinon « produits ».</summary>
    private static string Units(IEnumerable<Placement> items, Func<Guid, Article?> find) =>
        items.Any() && items.All(p => find(p.ArticleId) is { CaseQuantity: not null }) ? "colis" : "produits";

    /// <summary>« de BAG0000001 » ou, caisse mixte, « (350 × BAG0000001 + 100 × BAG0000011) ».</summary>
    private static string ContentOf(Article box, Func<Guid, Article?> find) => box.CaseContent switch
    {
        { IsMixed: true } m => $" ({string.Join(" + ", m.Lines!.Select(l => $"{N(l.Quantity)} × {find(l.ArticleId)?.Code ?? "?"}"))})",
        { } link when find(link.ArticleId) is { } content => $" {content.Code}",
        _ => ""
    };

    /// <summary>Quantité par colisage des articles caisse de la palette : recalcul du colisage, sinon quantité saisie.</summary>
    private static DetailSection? ColisageQuantity(Solution s, Article? article, Func<Guid, Article?> find, Func<Article, CaseEngine.CaseSheet?>? colisage)
    {
        var boxes = s.Kind == PackagingKind.Homogene && article != null
            ? [article]
            : s.Units.SelectMany(u => u.Items).Select(p => p.ArticleId).Distinct().Select(find).OfType<Article>().ToList();
        var lines = new List<string>();
        foreach (var box in boxes.Where(b => b.CaseQuantity != null || b.CaseContent != null))
        {
            var sheet = colisage != null && box.CaseContent != null ? colisage(box) : null;
            if (sheet is { IsMixed: true })
            {
                var cs = sheet.Solution;
                var unit = sheet.ShownUnit;
                var where = sheet.Type != null ? $"caisse {sheet.Type.Code}" : "caisse spécifique";
                lines.Add($"{box.Code} : caisse mixte, {where} (intérieur {Mm(sheet.Spec.InnerLength)} × {Mm(sheet.Spec.InnerWidth)} × {Mm(sheet.Spec.InnerHeight)} mm) — " +
                          $"{sheet.ContentText(find)} = {N(unit.Items.Count)} produit(s) par caisse, posés en {N(unit.Layers.Count)} niveau(x) selon la stratégie « {cs.Title} » " +
                          $"(remplissage {Pct(unit.Metrics.FillRate)} %, lourd sous léger, appui ≥ {Pct(CaseEngine.CaseConstraints(sheet.Spec).MinSupportPercent)} %).");
            }
            else if (sheet != null)
            {
                var cs = sheet.Solution;
                var where = sheet.Type != null ? $"caisse {sheet.Type.Code}" : "caisse spécifique";
                lines.Add($"{box.Code} : {sheet.Content.Code} dans une {where} (intérieur {Mm(sheet.Spec.InnerLength)} × {Mm(sheet.Spec.InnerWidth)} × {Mm(sheet.Spec.InnerHeight)} mm) — " +
                          $"{N(cs.ItemsPerLayer)} par couche × {N(cs.LayerCount)} couche(s){(cs.PartialTopLayer ? " (dernière incomplète)" : "")} = {N(cs.ItemsPerUnit)} produit(s) par caisse" +
                          (string.IsNullOrEmpty(cs.LayerLimitReason) ? "." : $" (limite : {cs.LayerLimitReason})."));
                if (box.CaseQuantity is { } q && q != cs.ItemsPerUnit)
                {
                    lines.Add($"{box.Code} : quantité par caisse enregistrée {N(q)}, retenue pour les quantités de la palette.");
                }
            }
            else if (box.CaseQuantity is { } q)
            {
                lines.Add($"{box.Code} : quantité par caisse saisie sur l'article = {N(q)} produit(s) (pas de colisage calculé à détailler).");
            }
        }

        return lines.Count == 0 ? null : new("Quantité par colisage", lines);
    }

    /// <summary>Sacs tassés : épaisseur avant / après, pourquoi c'est possible, limite, gain de couches.</summary>
    private static List<string> PressLines(List<Article> sacks, CaseSpec spec)
    {
        var lines = new List<string>();
        foreach (var a in sacks)
        {
            var h = a.Height;
            var hp = CaseEngine.Pressed(a).Height;
            var before = h > 0 ? (int)Math.Floor(spec.InnerHeight / h + 1e-9) : 0;
            var after = hp > 0 ? (int)Math.Floor(spec.InnerHeight / hp + 1e-9) : 0;
            lines.Add($"{a.Code} : tassement de {Pct(a.CompressionRate * 100)} % (case cochée sur l'article) : épaisseur {Mm(h)} mm → {Mm(hp)} mm, empreinte {Mm(a.Length)} × {Mm(a.Width)} mm inchangée.");
            lines.Add($"{a.Code} : par la hauteur intérieure, ⌊{Mm(spec.InnerHeight)} / {Mm(h)}⌋ = {N(before)} couche(s) sans tassement → ⌊{Mm(spec.InnerHeight)} / {Mm(hp)}⌋ = {N(after)} couche(s) tassés" +
                      (after > before ? $" : +{N(after - before)} couche(s)." : " : pas de couche de plus ici (le gain d'épaisseur ne suffit pas pour une couche entière)."));
        }

        lines.Add("Pourquoi c'est possible : un sac contient de l'air — entre les plis d'une liasse de sacs vides, entre les grains d'une poudre ou d'un granulé. En appuyant, on le chasse : l'épaisseur diminue, l'empreinte ne change pas.");
        lines.Add("Ordres de grandeur : aplatissement et désaération des sacs pleins en ligne de conditionnement 10 à 15 % ; liasses de sacs papier vides 15 à 25 % sous pression modérée.");
        lines.Add($"Limite : {ArticleSchema.MaxCompressionPercent:0} % — au-delà, un sac plein repousse son contenu (incompressible) sur les parois : caisse bombée, sac éclaté ; des sacs vides se marquent ou se plient. Pour des sacs pleins, rester sous 15 %.");
        lines.Add("Fermeture : la pile tassée repousse sur le couvercle ; fermer la caisse en appuyant (ruban, cerclage) et vérifier que la caisse supporte cette poussée.");
        return lines;
    }

    /// <summary>Quantité par colisage de la caisse calculée : produits par couche × couches, ou composition des caisses.</summary>
    private static DetailSection CaseQuantity(Solution s, LoadUnit unit, CaseSpec spec, Article? article, Func<Guid, Article?> find)
    {
        var lines = new List<string>();
        if (s.Kind == PackagingKind.Homogene && article != null)
        {
            lines.Add($"Quantité par colisage = produits par couche × nombre de couches = {N(s.ItemsPerLayer)} × {N(s.LayerCount)}" +
                      (s.PartialTopLayer ? $" (dernière couche incomplète) = {N(s.ItemsPerUnit)} produit(s) par caisse." : $" = {N(s.ItemsPerUnit)} produit(s) par caisse."));
            if (s.PartialTopLayer && s.LayerCount > 0)
            {
                var full = s.ItemsPerLayer * (s.LayerCount - 1);
                lines.Add($"Détail : {N(s.LayerCount - 1)} couche(s) complète(s) × {N(s.ItemsPerLayer)} = {N(full)}, plus {N(s.ItemsPerUnit - full)} sur la dernière couche.");
            }

            if (!string.IsNullOrEmpty(s.LayerLimitReason))
            {
                lines.Add($"Facteur limitant : {s.LayerLimitReason}.");
            }

            lines.Add($"C'est la quantité par caisse enregistrée sur l'article caisse créé depuis ce colisage ({Mm(spec.OuterLength)} × {Mm(spec.OuterWidth)} × {Mm(spec.OuterHeight)} mm).");
            return new("Quantité par colisage", lines);
        }

        foreach (var u in s.Units)
        {
            var parts = u.Items.GroupBy(p => p.ArticleId).Select(g => $"{find(g.Key)?.Code ?? "?"} × {N(g.Count())}").ToList();
            lines.Add($"{(s.Units.Count > 1 ? $"Caisse {u.Index}" : "Caisse")} : {string.Join(" + ", parts)} = {N(u.Items.Count)} produit(s).");
        }

        lines.Add($"Total : {N(s.Units.Sum(u => u.Items.Count))} produit(s) en {N(s.Units.Count)} caisse(s).");
        return new("Quantité par colisage", lines);
    }

    // ------------------------------------------------------------------ Homogène (palette ou caisse)

    private static IEnumerable<DetailSection> Homogeneous(Solution s, LoadUnit unit, PackagingConstraints c, Article article, double usableX, double usableY,
        double usableH, double capacity, string container)
    {
        var a = article.ForPalletizing();
        var first = unit.Items.Where(p => p.Layer == 1).ToList();
        var shape = first.Count > 0 ? first[0].Shape : ShapeKind.Box;
        var dims = a.IsCylinder ? $"Ø{Mm(a.Diameter)} × {Mm(a.AxisLength)} mm" : $"{Mm(a.Length)} × {Mm(a.Width)} × {Mm(a.Height)} mm";
        var productLines = new List<string>
        {
            $"{article.DisplayName} ({article.KindLabel}) : {dims}, {Kg(article.Weight)} kg." +
            (article.IsFolded ? " Carton plié : dimensions pliées retenues." : "") +
            (article.UsesInnerAsDiameter ? " Diamètre extérieur absent : diamètre intérieur retenu." : ""),
            $"Pose retenue : {s.OrientationText}."
        };
        if (ArticleSchema.EngineKind(article.Kind) != article.Kind)
        {
            productLines.Add($"{ArticleSchema.KindLabel(article.Kind)} : calculé comme un {(ArticleSchema.EngineKind(article.Kind) == ArticleKind.Fut ? "cylindre debout" : "bac rigide (haut imposé)")}.");
        }

        yield return new("Produit", productLines);

        var rule = TopStacking.For(article);
        if (rule != null)
        {
            yield return new("Forme du dessus et gerbage", TopLines(rule, s, c, article, container));
        }

        var n = s.ItemsPerLayer;
        var layerKind = string.IsNullOrEmpty(s.LayerPatternKind) ? s.PatternLabel : s.LayerPatternKind;
        var planLines = new List<string>
        {
            shape == ShapeKind.CylinderZ
                ? $"Produits ronds debout : meilleure suite de rangées alignées (pas d'un diamètre) ou en quinconce (pas de D·√3/2, rangées décalées d'un demi-diamètre), dans les deux sens de la {container}."
                : shape is ShapeKind.CylinderX or ShapeKind.CylinderY
                    ? "Produits couchés : lits côte à côte, superposés ou en quinconce dans les creux du lit inférieur."
                    : "Produits rectangulaires : grille, découpes guillotine optimales et moulinets à 5 blocs (récursifs), rotation de 90° au sol autorisée.",
            $"Plan retenu : {layerKind} — {N(n)} produit(s) par couche.",
            s.UpperBound > 0
                ? shape == ShapeKind.CylinderZ
                    ? s.ProvenOptimal
                        ? $"Borne démontrée (inégalité d'Oler) : {N(s.UpperBound)} par couche : le plan l'atteint, il est optimal."
                        : $"Borne démontrée (inégalité d'Oler) : {N(s.UpperBound)} par couche ; le plan retenu en place {N(n)} : meilleure disposition trouvée, son optimalité n'est pas démontrée."
                    : s.ProvenOptimal
                        ? $"Borne théorique : {N(s.UpperBound)} par couche (surface utile / surface du produit, côtés réduits aux combinaisons de longueurs possibles) : le plan l'atteint, il est optimal."
                        : $"Borne théorique : {N(s.UpperBound)} par couche ; le plan retenu en place {N(n)} : meilleure disposition trouvée, son optimalité n'est pas démontrée."
                : ""
        };
        yield return new("Plan de couche", planLines.Where(l => l.Length > 0).ToList());
        yield return new("Plan de couche pas à pas", LayerSteps(s, unit, first, c.Gap, usableX, usableY, container));

        var f = LayerFacts(s, unit, first, a, c, usableH);
        var (layerH, sheet) = (f.LayerH, f.Sheet);
        var layerLines = new List<string>
        {
            f.Nested
                ? $"Lit du bas : {Mm(layerH)} mm (un diamètre){(f.Sheet0 > 0 ? $", sur un intercalaire de {f.Sheet0.ToString("0.#", Fr)} mm" : "")}. Chaque lit suivant est décalé d'un demi-diamètre " +
                  $"({Mm(layerH / 2)} mm) et ses tubes se logent dans les creux du lit inférieur : il ne monte que de D × √3/2 = {Mm(layerH)} × 0,866 = {D(f.Pitch)} mm " +
                  $"(triangle équilatéral de côté D entre trois centres de tubes)."
                : $"Hauteur d'une couche : {Mm(layerH)} mm{(sheet > 0 ? $" ; intercalaire de {sheet.ToString("0.#", Fr)} mm {SheetPattern(c)}" : "")}."
        };
        var limits = Limits(s, c, article, usableH, capacity, f, rule);
        var noun = f.Nested ? "lit(s)" : "couche(s)";
        layerLines.Add($"Chaque contrainte donne un nombre maximal de {(f.Nested ? "lits" : "couches")} :");
        layerLines.AddRange(limits.Select(l => $"   {l.Name} : {l.How} → {N(l.Layers)} {noun}{(l.Layers == limits.Min(x => x.Layers) ? "   ◄ la plus petite" : "")}"));
        var smallest = limits.MinBy(l => l.Layers);
        layerLines.Add($"La plus petite limite décide : {smallest.Name.ToLower(Fr)} → {N(smallest.Layers)} {noun}.");
        if (f.Nested)
        {
            var straight = (int)Math.Floor((usableH - f.Sheet0 + 1e-6) / layerH);
            layerLines.Add($"Comparaison, lits superposés sans emboîtement (un diamètre chacun) : ⌊{Mm(usableH - f.Sheet0)} / {Mm(layerH)}⌋ = {N(straight)} lits, {N(straight * f.N1)} produits ; " +
                           $"la quinconce en loge {N(f.ByHeight)} dans la même hauteur ({(f.ByHeight > straight ? $"+{N(f.ByHeight - straight)} lit(s)" : "pas de lit de plus")}).");
        }
        if (s.LayerCount != smallest.Layers || s.PartialTopLayer)
        {
            layerLines.Add(s.LayerLimitReason == "quantité imposée"
                ? $"Quantité imposée : {N(s.ItemsPerUnit)} produits, soit ⌈{N(s.ItemsPerUnit)} / {N(n)}⌉ = {N(s.LayerCount)} couche(s)."
                : s.PartialTopLayer && capacity > 0 && article.Weight > 0
                    ? $"Le poids admis permet ⌊{Kg(capacity)} / {Kg(article.Weight)}⌋ = {N((int)Math.Floor(capacity / article.Weight + 1e-9))} produits : " +
                      $"{N(s.LayerCount - 1)} couche(s) complète(s) ({N(n * (s.LayerCount - 1))}) + {N(s.ItemsPerUnit - n * (s.LayerCount - 1))} sur une dernière couche incomplète, remplie jusqu'au poids."
                    : s.PartialTopLayer
                    ? $"Dernière couche remplie jusqu'à la limite de poids : {N(s.LayerCount)} couche(s), la dernière incomplète."
                    : $"Retenu : {N(s.LayerCount)} {noun} (limite : {s.LayerLimitReason}).");
        }

        if (f.Nested)
        {
            layerLines.Add($"Hauteur de charge : {Mm(layerH)}{(f.Sheet0 > 0 ? $" + {f.Sheet0.ToString("0.#", Fr)}" : "")} + ({N(s.LayerCount)} − 1) × {D(f.Pitch)} = {Mm(StackHeight(c, f, s.LayerCount))} mm (≤ {Mm(usableH)} mm utiles).");
        }

        layerLines.Add(LayerSum(unit, f.Nested) is { } sum && !s.PartialTopLayer
            ? $"Produits = {sum} = {N(s.ItemsPerUnit)}."
            : $"Produits = {N(n)} par {(f.Nested ? "lit" : "couche")} × {N(s.LayerCount)} {noun}{(s.PartialTopLayer ? " (dernière incomplète)" : "")} = {N(s.ItemsPerUnit)}.");
        yield return new("Nombre de couches", layerLines);
        yield return new("Pourquoi pas plus ?", WhyNotMore(s, unit, c, article, usableX, usableY, usableH, capacity, f, rule, limits, container));
        if (container == "palette" && LoadStability.Assess(s, unit, c, article).Lines is { Count: > 0 } stability)
        {
            yield return new("Stabilité et maintien", stability);
        }

        if (s.Recommendation != null)
        {
            yield return new("Choix de la solution", [s.Recommendation, "Classement : solutions conformes d'abord, puis le plus de produits, puis la stabilité (colonne, croisé, imbrication)."]);
        }
    }

    /// <summary>Intercalaire sous la couche k (0 = sur la palette), comme le moteur.</summary>
    private static bool SheetBelow(PackagingConstraints c, int k) => c.SlipSheetThickness > 0 && (k == 0 ? c.SlipSheetOnPallet : k % Math.Max(1, c.SlipSheetEvery) == 0);

    private static string SheetPattern(PackagingConstraints c) =>
        (Math.Max(1, c.SlipSheetEvery) == 1 ? "sous chaque couche" : $"toutes les {c.SlipSheetEvery} couches") + (c.SlipSheetOnPallet ? ", et sur la palette" : "");

    /// <summary>
    /// Couches d'une palette homogène : hauteur d'une couche, intercalaire ; lits de produits couchés en quinconce
    /// (« emboîtés ») : lit du bas d'un diamètre, chaque lit suivant D·√3/2, lits alternés de N1 et N2 produits.
    /// </summary>
    private sealed record Facts(double LayerH, double Sheet, double Sheet0, double Pitch, int N1, int N2, int ByHeight)
    {
        public bool Nested => Pitch > 0;

        /// <summary>Produits de k couches (lits alternés N1, N2 en quinconce).</summary>
        public int Count(int k) => Nested ? (k + 1) / 2 * N1 + k / 2 * N2 : k * N1;
    }

    /// <summary>Hauteur de charge de k couches : intercalaires compris ; lits emboîtés : D + (k − 1) × D·√3/2 (intercalaire sur la palette seulement, comme le moteur).</summary>
    private static double StackHeight(PackagingConstraints c, Facts f, int k) =>
        f.Nested ? (k == 0 ? 0 : f.Sheet0 + f.LayerH + (k - 1) * f.Pitch) : StackHeight(c, f.LayerH, k);

    /// <summary>« 13 lits de 10 + 12 lits de 9 » quand les couches ne comptent pas toutes autant de produits ; sinon null.</summary>
    private static string? LayerSum(LoadUnit unit, bool beds)
    {
        var groups = unit.Layers.GroupBy(l => l.Count).OrderByDescending(g => g.Key).ToList();
        return groups.Count < 2 ? null : string.Join(" + ", groups.Select(g => $"{N(g.Count())} {(beds ? "lit(s)" : "couche(s)")} de {N(g.Key)}"));
    }

    /// <summary>Hauteur de charge de k couches, intercalaires compris.</summary>
    private static double StackHeight(PackagingConstraints c, double layerH, int k)
    {
        double z = 0;
        for (var i = 0; i < k; i++)
        {
            z += (SheetBelow(c, i) ? c.SlipSheetThickness : 0) + layerH;
        }

        return z;
    }

    private sealed record LayerLimit(string Name, int Layers, string How);

    /// <summary>Chaque contrainte et le nombre de couches qu'elle autorise (mêmes règles que le moteur).</summary>
    private static List<LayerLimit> Limits(Solution s, PackagingConstraints c, Article article, double usableH, double capacity, Facts f, TopStackRule? rule)
    {
        var n = Math.Max(1, s.ItemsPerLayer);
        var layerH = f.LayerH;
        var limits = new List<LayerLimit>();
        var byHeight = 0;
        while (byHeight < 10000 && StackHeight(c, f, byHeight + 1) <= usableH + 1e-6)
        {
            byHeight++;
        }

        limits.Add(new("Hauteur", byHeight, f.Nested
            ? $"lit du bas de {Mm(layerH + f.Sheet0)} mm, puis un lit tous les {D(f.Pitch)} mm : 1 + ⌊({Mm(usableH)} − {Mm(layerH + f.Sheet0)}) / {D(f.Pitch)}⌋ = " +
              $"1 + ⌊{((usableH - layerH - f.Sheet0) / f.Pitch).ToString("0.##", Fr)}⌋ ({N(byHeight)} lits = {Mm(StackHeight(c, f, byHeight))} mm)"
            : c.SlipSheetThickness > 0
                ? $"couches de {Mm(layerH)} mm et intercalaires tant que la hauteur cumulée reste ≤ {Mm(usableH)} mm ({N(byHeight)} couches = {Mm(StackHeight(c, layerH, byHeight))} mm)"
                : $"⌊{Mm(usableH)} / {Mm(layerH)}⌋ = ⌊{(usableH / Math.Max(1, layerH)).ToString("0.##", Fr)}⌋"));
        if (capacity > 0 && article.Weight > 0)
        {
            if (f.Nested && f.N2 != f.N1)
            {
                var byWeight = 0;
                while (byWeight < 10000 && f.Count(byWeight + 1) * article.Weight <= capacity + 1e-9)
                {
                    byWeight++;
                }

                limits.Add(new("Poids", byWeight,
                    $"lits de {N(f.N1)} et {N(f.N2)} produits ({Kg(f.N1 * article.Weight)} et {Kg(f.N2 * article.Weight)} kg) cumulés tant que la charge reste ≤ {Kg(capacity)} kg"));
            }
            else
            {
                var layerWeight = n * article.Weight;
                limits.Add(new("Poids", (int)Math.Floor(capacity / layerWeight + 1e-9),
                    $"⌊{Kg(capacity)} kg / ({N(n)} × {Kg(article.Weight)} kg par {(f.Nested ? "lit" : "couche")} = {Kg(layerWeight)} kg)⌋ = ⌊{(capacity / layerWeight).ToString("0.##", Fr)}⌋"));
            }
        }

        if (article.MaxLayers is { } ml)
        {
            limits.Add(new("Couches maxi de l'article", ml, "valeur saisie sur l'article"));
        }

        if (article.EffectiveMaxLoadOnTop is { } top && article.Weight > 0)
        {
            var tf = rule?.CapacityFactor ?? 1;
            var allowed = top * s.StrengthFactor * tf;
            var factors = (Math.Abs(s.StrengthFactor - 1) > 1e-9 ? $" × {s.StrengthFactor.ToString("0.##", Fr)} (répartition)" : "") +
                          (tf < 1 ? $" × {tf.ToString("0.##", Fr)} (forme du dessus)" : "");
            limits.Add(new("Résistance", 1 + (int)Math.Floor(allowed / article.Weight + 1e-9),
                $"le produit du bas porte au plus {Kg(top)} kg{(factors.Length > 0 ? $"{factors} = {Kg(allowed)} kg" : "")}, soit ⌊{Kg(allowed)} / {Kg(article.Weight)}⌋ = {N((int)Math.Floor(allowed / article.Weight + 1e-9))} produit(s) au-dessus de lui, + 1"));
        }

        if (TopStacking.MaxLayers(rule, k => SheetBelow(c, k)) is { } byShape)
        {
            limits.Add(new("Forme du dessus", byShape, rule!.Mode == TopStackMode.NonGerbable
                ? "rien ne peut être posé dessus"
                : byShape == 1 ? "intercalaire obligatoire sous chaque couche, absent ici" : $"intercalaire obligatoire : présent sous les couches 2 à {N(byShape)}"));
        }

        return limits;
    }

    /// <summary>Forme du dessus : raisonnement, puis ce qu'il change pour ce conditionnement.</summary>
    private static List<string> TopLines(TopStackRule rule, Solution s, PackagingConstraints c, Article article, string container)
    {
        var lines = new List<string>(rule.Steps)
        {
            $"Verdict : {rule.ModeLabel} — {rule.Summary}."
        };
        switch (rule.Mode)
        {
            case TopStackMode.Direct when rule.CapacityFactor < 1:
                lines.Add(article.EffectiveMaxLoadOnTop is { } top
                    ? $"Ici : la charge admissible saisie ({Kg(top)} kg) est ramenée à {Kg(top * rule.CapacityFactor)} kg par l'appui partiel."
                    : "Ici : aucune charge maxi saisie sur l'article, seule la stabilité de l'appui est vérifiée.");
                break;
            case TopStackMode.Intercalaire:
                var every = TopStacking.MaxLayers(rule, k => SheetBelow(c, k)) ?? 1;
                lines.Add(c.SlipSheetThickness > 0 && Math.Max(1, c.SlipSheetEvery) == 1
                    ? $"Ici : intercalaire sous chaque couche ({c.SlipSheetThickness.ToString("0.#", Fr)} mm) : gerbage accepté, charge admissible × {rule.CapacityFactor.ToString("0.##", Fr)}."
                    : container == "caisse"
                        ? "Ici : pas d'intercalaire dans la caisse : une seule couche de produits."
                        : $"Ici : {(c.SlipSheetThickness > 0 ? $"intercalaire {SheetPattern(c)} seulement" : "aucun intercalaire")} : {N(every)} couche(s) au plus. " +
                          "Mettez un intercalaire sous chaque couche (Accessoires, « tous les 1 ») pour monter plus haut.");
                break;
            case TopStackMode.NonGerbable:
                lines.Add("Ici : une seule couche.");
                break;
        }

        if (rule.Mode != TopStackMode.Direct && container == "palette")
        {
            lines.Add(c.CapHeight > 0
                ? "Gerbage des palettes : la coiffe répartit la charge de la palette du dessus."
                : "Gerbage des palettes : sans coiffe, la palette du dessus reposerait sur les poignées ou les cols — non gerbable.");
        }

        return lines;
    }

    /// <summary>
    /// Pourquoi pas une couche de plus, pas un produit de plus par couche : chaque contrainte que l'on dépasserait,
    /// avec les chiffres.
    /// </summary>
    private static List<string> WhyNotMore(Solution s, LoadUnit unit, PackagingConstraints c, Article article, double usableX, double usableY,
        double usableH, double capacity, Facts f, TopStackRule? rule, List<LayerLimit> limits, string container)
    {
        var lines = new List<string>();
        var n = Math.Max(1, s.ItemsPerLayer);
        var layers = s.LayerCount;
        var one = f.Nested ? "Un lit de plus" : "Une couche de plus";
        if (s.LayerLimitReason == "quantité imposée")
        {
            lines.Add($"{one} : la quantité imposée ({N(s.ItemsPerUnit)}) est atteinte.");
        }
        else
        {
            var next = layers + 1;
            var count = f.Count(next);
            lines.Add($"{one} ({N(next)} {(f.Nested ? "lits" : "couches")}, {N(count)} produits) dépasserait :");
            var reasons = new List<string>();
            var hNext = StackHeight(c, f, next);
            if (hNext > usableH + 1e-6)
            {
                reasons.Add(f.Nested
                    ? $"la hauteur : {Mm(f.LayerH + f.Sheet0)} + {N(next - 1)} × {D(f.Pitch)} = {Mm(hNext)} mm de charge > {Mm(usableH)} mm utiles (+{Mm(hNext - usableH)} mm), " +
                      $"soit {Mm(hNext + s.Base.PalletHeight + c.CapHeight)} mm de hauteur totale"
                    : $"la hauteur : {Mm(hNext)} mm de charge > {Mm(usableH)} mm utiles (+{Mm(hNext - usableH)} mm)");
            }

            if (capacity > 0 && count * article.Weight > capacity + 1e-9)
            {
                reasons.Add($"le poids : {N(count)} × {Kg(article.Weight)} kg = {Kg(count * article.Weight)} kg > {Kg(capacity)} kg admis");
            }

            if (article.MaxLayers is { } ml && next > ml)
            {
                reasons.Add($"les couches maxi de l'article ({N(ml)})");
            }

            if (article.EffectiveMaxLoadOnTop is { } top && article.Weight > 0)
            {
                var allowed = top * s.StrengthFactor * (rule?.CapacityFactor ?? 1);
                if (layers * article.Weight > allowed + 1e-9)
                {
                    reasons.Add($"la résistance : le produit du bas porterait {N(layers)} × {Kg(article.Weight)} kg = {Kg(layers * article.Weight)} kg > {Kg(allowed)} kg admis");
                }
            }

            if (limits.FirstOrDefault(l => l.Name == "Forme du dessus") is { } shape && next > shape.Layers)
            {
                reasons.Add($"la forme du dessus ({rule!.Summary})");
            }

            if (s.PartialTopLayer && reasons.Count == 0)
            {
                reasons.Add($"le poids : la dernière couche est déjà remplie jusqu'au poids admis ({Kg(capacity)} kg)");
            }

            lines.AddRange(reasons.Count > 0 ? reasons.Select(r => "   → " + r + ".") : ["   → aucune contrainte de couche : la solution retenue privilégie la stabilité (voir « Choix de la solution »)."]);
        }

        // Un produit de plus par couche.
        var p0 = unit.Items.FirstOrDefault();
        var round = p0?.Shape == ShapeKind.CylinderZ;
        if (p0?.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY)
        {
            // Tubes couchés : tous parallèles (le tube est plus long que l'autre côté), côte à côte dans un lit.
            var alongX = p0.Shape == ShapeKind.CylinderX;
            var (len, d) = (alongX ? p0.DX : p0.DY, p0.DZ);
            var (lax, lcross) = alongX ? (usableX, usableY) : (usableY, usableX);
            var g = c.Gap;
            var nAxis = Fit(lax, len, g);
            var nCross = Fit(lcross, d, g);
            lines.Add($"Un tube de plus par lit : dans l'axe {FitText(lax, len, g)} = {N(nAxis)} tube(s) bout à bout ; en travers {FitText(lcross, d, g)} = {N(nCross)} côte à côte, " +
                      $"un de plus demanderait {N(nCross + 1)} × {Mm(d + g)}{(g > 0 ? $" − {D(g)}" : "")} = {Mm((nCross + 1) * (d + g) - g)} mm > {Mm(lcross)} mm. " +
                      (len > lcross + 1e-6
                          ? $"Tourné, un tube de {Mm(len)} mm ne tient pas en travers ({Mm(len)} > {Mm(lcross)} mm) : {N(nAxis * nCross)} par lit est le maximum."
                          : "L'autre sens de pose est calculé lui aussi (autres solutions proposées)."));
        }
        else if (s.UpperBound > 0 && n >= s.UpperBound)
        {
            lines.Add(round
                ? $"Un produit de plus par couche ({N(n + 1)}) : impossible, la borne démontrée (inégalité d'Oler, voir le plan pas à pas) est de {N(s.UpperBound)} et le plan l'atteint."
                : $"Un produit de plus par couche ({N(n + 1)}) : impossible, la borne théorique est de {N(s.UpperBound)} par couche (surface utile et longueurs réellement combinables) et le plan l'atteint.");
        }
        else if (s.UpperBound > 0)
        {
            lines.Add($"Un produit de plus par couche ({N(n + 1)}) : la borne {(round ? "démontrée" : "théorique")} ({N(s.UpperBound)}) ne l'exclut pas. Aucune disposition à {N(n + 1)} n'a été trouvée " +
                      (round
                          ? "parmi les familles explorées (rangées alignées, en quinconce et mixtes, dans les deux sens)"
                          : "parmi les familles explorées (grilles, découpes guillotine, moulinets récursifs)") +
                      $" : {N(n)} est la meilleure disposition trouvée, pas un maximum démontré (une disposition irrégulière n'est pas exclue).");
        }
        else
        {
            lines.Add($"Un produit de plus par couche ({N(n + 1)}) : aucune disposition trouvée dans la surface utile {Mm(usableX)} × {Mm(usableY)} mm.");
        }

        if (s.PartialTopLayer && layers > 0)
        {
            var full = n * (layers - 1);
            lines.Add($"Dernière couche incomplète ({N(s.ItemsPerUnit - full)} sur {N(n)}) : " +
                      (s.LayerLimitReason == "quantité imposée" ? "la quantité imposée est atteinte." : $"le poids admis ({Kg(capacity)} kg) est atteint avant la fin de la couche."));
        }

        if (container == "palette" && s.Units.Count == 1 && s.LayerLimitReason != "quantité imposée")
        {
            lines.Add("Pour plus de produits : palette plus grande, débord autorisé, hauteur maximale plus haute ou charge maxi plus élevée (onglet Contraintes) — l'assistant compare toutes les palettes.");
        }

        return lines;
    }

    /// <summary>Hauteur d'une couche, intercalaire, couches par la hauteur ; lits emboîtés (produits couchés en quinconce).</summary>
    private static Facts LayerFacts(Solution s, LoadUnit unit, List<Placement> first, Article a, PackagingConstraints c, double usableH)
    {
        var layerH = unit.Layers.Count > 0 ? unit.Layers[0].Height : first.Count > 0 ? first.Max(p => p.DZ) : a.Height;
        var sheet = c.SlipSheetThickness > 0 ? c.SlipSheetThickness : 0;
        var sheet0 = SheetBelow(c, 0) ? c.SlipSheetThickness : 0;
        var nested = s.Pattern == StackPattern.Quinconce && first.Count > 0 && first[0].Shape is ShapeKind.CylinderX or ShapeKind.CylinderY;
        var n1 = Math.Max(1, s.ItemsPerLayer);
        if (nested && layerH > 0)
        {
            var pitch = layerH * Math.Sqrt(3) / 2;
            var n2 = unit.Layers.Count > 1 ? unit.Layers[1].Count : n1;
            var byNested = usableH + 1e-6 >= sheet0 + layerH ? 1 + (int)Math.Floor((usableH + 1e-6 - sheet0 - layerH) / pitch) : 0;
            return new Facts(layerH, sheet, sheet0, pitch, n1, n2, byNested);
        }

        var byHeight = layerH > 0 ? (int)Math.Floor((usableH + 1e-6) / (layerH + sheet)) : 0;
        return new Facts(layerH, sheet, sheet0, 0, n1, n1, byHeight);
    }

    private static int Fit(double length, double size, double gap) => size <= 0 ? 0 : Math.Max(0, (int)Math.Floor((length + gap + 1e-6) / (size + gap)));

    private static string FitText(double length, double size, double gap) =>
        gap > 0 ? $"⌊({Mm(length)} + {D(gap)}) / ({Mm(size)} + {D(gap)})⌋" : $"⌊{Mm(length)} / {Mm(size)}⌋";

    /// <summary>
    /// Plan de couche expliqué : surface et empreinte, grilles simples (tout dans un sens, tourné de 90°, ou rangées
    /// alignées / en quinconce pour les ronds debout), puis le plan retenu décomposé en blocs ou en rangées, et la
    /// surface couverte.
    /// </summary>
    private static List<string> LayerSteps(Solution s, LoadUnit unit, List<Placement> first, double gap, double usableX, double usableY, string container)
    {
        var lines = new List<string>();
        if (first.Count == 0)
        {
            return ["Aucun produit sur la première couche."];
        }

        var n = first.Count;
        var p0 = first[0];
        var gapText = gap > 0 ? $", jeu de {D(gap)} mm entre produits" : "";
        if (p0.Shape == ShapeKind.CylinderZ)
        {
            var d = p0.DX;
            lines.Add($"Surface utile de la {container} : {Mm(usableX)} × {Mm(usableY)} mm ; chaque produit occupe un cercle de Ø{Mm(d)} mm{gapText}.");
            var ax = Fit(usableX, d, gap);
            var ay = Fit(usableY, d, gap);
            lines.Add($"Rangées alignées (pas d'un diamètre) : {FitText(usableX, d, gap)} × {FitText(usableY, d, gap)} = {N(ax)} × {N(ay)} = {N(ax * ay)} produits.");
            var pitch = (d + gap) * Math.Sqrt(3) / 2;
            foreach (var (along, across, label) in new[] { (usableX, usableY, "rangées dans la longueur"), (usableY, usableX, "rangées dans la largeur") })
            {
                if (across < d)
                {
                    continue;
                }

                var rows = 1 + (int)Math.Floor((across - d) / pitch + 1e-6);
                var full = Fit(along, d, gap);
                var shifted = Math.Max(0, (int)Math.Floor((along - (d + gap) / 2 + gap + 1e-6) / (d + gap)));
                var total = (rows + 1) / 2 * full + rows / 2 * shifted;
                lines.Add($"En quinconce ({label}) : les rangées se rapprochent à {D(pitch)} mm (diamètre × 0,866) en se décalant d'un demi-diamètre → " +
                          $"1 + ⌊({Mm(across)} − {Mm(d)}) / {D(pitch)}⌋ = {N(rows)} rangées, alternativement {N(full)} et {N(shifted)} produits → " +
                          $"{N((rows + 1) / 2)} × {N(full)} + {N(rows / 2)} × {N(shifted)} = {N(total)} produits.");
            }

            // Rangées du plan retenu : dans le sens où les produits d'une même rangée se touchent.
            var byY = Rows(first, p => p.Y);
            var byX = Rows(first, p => p.X);
            int Tight(List<List<Placement>> rows, Func<Placement, double> along) => rows.Sum(r =>
                r.OrderBy(along).Zip(r.OrderBy(along).Skip(1), (u, v) => along(v) - along(u)).Count(st => st < d + gap + 1));
            var lengthwise = Tight(byY, p => p.X) - byY.Count >= Tight(byX, p => p.Y) - byX.Count;
            var rowsOf = lengthwise ? byY : byX;
            var counts = rowsOf.Select(r => r.Count).ToList();
            var steps = rowsOf.Zip(rowsOf.Skip(1), (r1, r2) => lengthwise ? r2[0].Y - r1[0].Y : r2[0].X - r1[0].X).ToList();
            var aligned = steps.Count(st => Math.Abs(st - (d + gap)) < 1);
            var kind = steps.Count == 0 ? "une seule rangée" : aligned == steps.Count ? "rangées alignées" : aligned == 0 ? "rangées en quinconce" : $"maille mixte ({N(aligned)} écart(s) aligné(s), {N(steps.Count - aligned)} en quinconce)";
            lines.Add($"Plan retenu : {N(rowsOf.Count)} rangées {(lengthwise ? "dans la longueur" : "dans la largeur")}, {kind} : " +
                      $"{string.Join(" + ", counts.GroupBy(x => x).OrderByDescending(g => g.Key).Select(g => g.Count() == 1 ? $"1 rangée de {N(g.Key)}" : $"{N(g.Count())} rangées de {N(g.Key)}"))} = {N(n)} produits par couche.");
            AddCoverage(lines, n, Math.PI * d * d / 4, usableX, usableY, "cercles");
            var (dd, xx, yy) = (d + gap, usableX + gap, usableY + gap);
            var (ca, cb) = ((xx - dd) / dd, (yy - dd) / dd);
            var oler = CircleLayerSolver.Bound(xx, yy, dd);
            lines.Add($"Borne démontrée (inégalité d'Oler, 1961) : les centres sont dans un rectangle de ({Mm(xx)} − {Mm(dd)}) × ({Mm(yy)} − {Mm(dd)}) mm, " +
                      $"soit a × b = {ca.ToString("0.###", Fr)} × {cb.ToString("0.###", Fr)} diamètres ; N ≤ 2/√3 × a × b + a + b + 1 = " +
                      $"{(2 / Math.Sqrt(3) * ca * cb).ToString("0.##", Fr)} + {(ca + cb).ToString("0.##", Fr)} + 1 = {(2 / Math.Sqrt(3) * ca * cb + ca + cb + 1).ToString("0.##", Fr)} " +
                      $"→ au plus {N(oler)} produits par couche, quelle que soit la disposition (même irrégulière).");
            lines.Add($"Repère, pas une borne : la quinconce parfaite couvre 90,69 % d'une surface infinie, soit environ {CircleLayerSolver.HexEstimate(xx, yy, dd).ToString("0.#", Fr)} produits ici ; les bords en font perdre.");
            lines.Add(n >= oler
                ? $"Le plan retenu atteint la borne démontrée : {N(n)} est le maximum."
                : $"Écart à la borne : {N(oler)} − {N(n)} = {N(oler - n)}. {N(n)} est la meilleure disposition trouvée (rangées alignées, en quinconce et mixtes, dans les deux sens) ; " +
                  $"aucune disposition à {N(n + 1)} n'est connue, sans que son impossibilité soit démontrée.");
        }
        else
        {
            var big = Math.Max(p0.DX, p0.DY);
            var small = Math.Min(p0.DX, p0.DY);
            var lying = p0.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY ? " (produit couché : son empreinte au sol)" : "";
            lines.Add($"Surface utile de la {container} : {Mm(usableX)} × {Mm(usableY)} mm ; empreinte d'un produit {Mm(big)} × {Mm(small)} mm{lying}{gapText}.");
            var g1 = Fit(usableX, big, gap) * Fit(usableY, small, gap);
            var g2 = Fit(usableX, small, gap) * Fit(usableY, big, gap);
            lines.Add($"Grille simple, produits en long (côté {Mm(big)} dans la longueur) : {FitText(usableX, big, gap)} × {FitText(usableY, small, gap)} = " +
                      $"{N(Fit(usableX, big, gap))} × {N(Fit(usableY, small, gap))} = {N(g1)} produits.");
            if (Math.Abs(big - small) > 0.5)
            {
                lines.Add($"Grille simple, produits en travers (tournés de 90°) : {FitText(usableX, small, gap)} × {FitText(usableY, big, gap)} = " +
                          $"{N(Fit(usableX, small, gap))} × {N(Fit(usableY, big, gap))} = {N(g2)} produits.");
            }

            var blocks = Blocks(first);
            if (blocks.Count <= 8)
            {
                lines.Add($"Plan retenu : {(blocks.Count == 1 ? "un seul bloc" : $"{N(blocks.Count)} blocs posés côte à côte")} :");
                lines.AddRange(blocks.Select((b, i) =>
                    $"Bloc {i + 1} : {N(b.Cols)} × {N(b.Rows)} = {N(b.Cols * b.Rows)} produit(s) {(b.A >= b.B - 0.5 ? "en long" : "en travers")} " +
                    $"({Mm(b.A)} × {Mm(b.B)}), zone de {Mm(b.Cols * b.A + (b.Cols - 1) * gap)} × {Mm(b.Rows * b.B + (b.Rows - 1) * gap)} mm."));
                if (blocks.Count > 1)
                {
                    lines.Add($"Total : {string.Join(" + ", blocks.Select(b => N(b.Cols * b.Rows)))} = {N(n)} produits par couche.");
                }
            }
            else
            {
                var inLong = first.Count(q => q.DX >= q.DY - 0.5);
                lines.Add($"Plan retenu : {N(inLong)} produit(s) en long + {N(n - inLong)} en travers = {N(n)} produits par couche (plan imbriqué « moulinet » : blocs tournés les uns par rapport aux autres, voir la vue de dessus).");
            }

            var simple = Math.Max(g1, g2);
            lines.Add(n > simple
                ? $"Gain sur la meilleure grille simple : {N(n)} − {N(simple)} = +{N(n - simple)} produit(s) par couche, en mélangeant les deux sens."
                : n == simple ? "Le plan retenu est la meilleure grille simple : aucun mélange des sens ne place plus de produits." : $"Plan retenu : {N(n)} produits (contraintes de pose).");
            AddCoverage(lines, n, big * small, usableX, usableY, lying.Length > 0 ? "enveloppes projetées au sol : un tube ne touche le sol que sur une ligne" : "produits");
        }

        if (s.Pattern == StackPattern.Quinconce && p0.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY)
        {
            lines.Add($"Lits en quinconce : un lit sur deux est décalé d'un demi-diamètre ({Mm(p0.DZ / 2)} mm), chaque tube se loge dans le creux de deux tubes du lit inférieur " +
                      "(hauteur des lits : voir « Nombre de couches »).");
        }
        else if (unit.Layers.Select(l => l.Pattern).Distinct().Count() > 1)
        {
            lines.Add("Couches alternées : d'une couche à l'autre le plan est retourné, pour croiser les joints et lier la charge (même principe, même calcul).");
        }

        return lines;
    }

    private static void AddCoverage(List<string> lines, int n, double area, double usableX, double usableY, string what)
    {
        var usable = usableX * usableY;
        if (usable <= 0)
        {
            return;
        }

        lines.Add($"Borne simple : surface utile ÷ surface d'un produit = {M2(usable)} m² ÷ {M2(area)} m² = " +
                  $"{(usable / area).ToString("0.##", Fr)} → au plus {N((int)Math.Floor(usable / area + 1e-9))} produits par couche, même sans aucune perte.");
        lines.Add($"Surface couverte = {N(n)} × {M2(area)} m² = {M2(n * area)} m², soit {(100 * n * area / usable).ToString("0.##", Fr)} % de la surface utile ({what}).");
    }

    /// <summary>Produits regroupés en rangées selon une coordonnée (même valeur à 1,5 mm près), rangées triées.</summary>
    private static List<List<Placement>> Rows(List<Placement> items, Func<Placement, double> key)
    {
        var rows = new List<List<Placement>>();
        foreach (var p in items.OrderBy(key))
        {
            if (rows.Count > 0 && Math.Abs(key(rows[^1][0]) - key(p)) < 1.5)
            {
                rows[^1].Add(p);
            }
            else
            {
                rows.Add([p]);
            }
        }

        return rows;
    }

    /// <summary>
    /// Décompose la couche en blocs rectangulaires de produits orientés pareil : depuis le premier produit restant
    /// (en bas à gauche), on prolonge la rangée tant qu'un voisin suit, puis on ajoute les rangées complètes au-dessus.
    /// </summary>
    private static List<(int Cols, int Rows, double A, double B)> Blocks(List<Placement> items)
    {
        const double tol = 1.5;
        var left = items.OrderBy(p => Math.Round(p.Y)).ThenBy(p => p.X).ToList();
        var blocks = new List<(int, int, double, double)>();
        while (left.Count > 0)
        {
            var o = left[0];
            bool Same(Placement p) => Math.Abs(p.DX - o.DX) < 0.5 && Math.Abs(p.DY - o.DY) < 0.5;
            Placement? Next(Placement prev) => left.Where(p => Same(p) && Math.Abs(p.Y - prev.Y) < tol && p.X > prev.X + tol && p.X - prev.MaxX < o.DX / 2)
                .MinBy(p => p.X);
            var row = new List<Placement> { o };
            while (Next(row[^1]) is { } nx)
            {
                row.Add(nx);
            }

            var rows = new List<List<Placement>> { row };
            while (true)
            {
                var top = rows[^1];
                var start = left.Where(p => Same(p) && Math.Abs(p.X - top[0].X) < tol && p.Y > top[0].Y + tol && p.Y - top[0].MaxY < o.DY / 2).MinBy(p => p.Y);
                if (start == null)
                {
                    break;
                }

                var next = top.Select(q => left.FirstOrDefault(p => Same(p) && Math.Abs(p.X - q.X) < tol && Math.Abs(p.Y - start.Y) < tol)).ToList();
                if (next.Any(p => p == null))
                {
                    break;
                }

                rows.Add(next!);
            }

            foreach (var p in rows.SelectMany(r => r))
            {
                left.Remove(p);
            }

            blocks.Add((row.Count, rows.Count, o.DX, o.DY));
        }

        return blocks;
    }

    // ------------------------------------------------------------------ Les opérations, en bref

    private static string Dec(double v) => v.ToString("#,0.##", Fr);

    /// <summary>Opérations simples, une par ligne, du début à la fin du calcul de la palette.</summary>
    private static DetailSection PalletOperations(Solution s, LoadUnit unit, PackagingConstraints c, Article? article, Func<Guid, Article?> find,
        double usableX, double usableY, double usableH, double capacity)
    {
        var b = s.Base;
        var m = unit.Metrics;
        var ops = new List<string>
        {
            $"Surface utile : {Mm(usableX)} × {Mm(usableY)} mm.",
            $"Hauteur utile : {Mm(c.MaxTotalHeight)} − {Mm(b.PalletHeight)} (plancher){(c.CapHeight > 0 ? $" − {Mm(c.CapHeight)} (coiffe)" : "")} = {Mm(usableH)} mm."
        };
        if (s.Kind == PackagingKind.Homogene && article != null)
        {
            var first = unit.Items.Where(p => p.Layer == 1).ToList();
            AddLayerOps(ops, s, unit, LayerFacts(s, unit, first, article.ForPalletizing(), c, usableH), usableH, capacity, article.Weight, "palette");
            var noun = article.CaseQuantity != null ? "colis" : "produits";
            if (article.CaseQuantity is { } q)
            {
                ops.Add($"Produits contenus : {N(s.ItemsPerUnit)} colis × {N(q)} par caisse = {N(s.ItemsPerUnit * q)} produits.");
            }

            ops.Add($"Poids de la charge : {N(unit.Items.Count)} {noun} × {Kg(article.Weight)} kg = {Kg(m.LoadWeight)} kg.");
        }
        else
        {
            var all = s.Units.SelectMany(u => u.Items).ToList();
            ops.Add($"{(Units(all, find) == "colis" ? "Colis" : "Produits")} à placer : {string.Join(" + ", all.GroupBy(p => p.ArticleId).Select(g => $"{N(g.Count())} {find(g.Key)?.Code ?? "?"}"))} = {N(all.Count)} {Units(all, find)}.");
            foreach (var u in s.Units)
            {
                ops.Add($"{(s.Units.Count > 1 ? $"Palette {u.Index}" : "Palette")} : {string.Join(" + ", u.Items.GroupBy(p => p.ArticleId).Select(g => N(g.Count())))} = {N(u.Items.Count)} {Units(u.Items, find)}, " +
                        $"{Kg(u.Items.Sum(p => p.Weight))} kg.");
            }

            ops.Add($"Nombre de palettes : {N(s.Units.Count)}.");
        }

        ops.Add($"Poids total : {Kg(m.LoadWeight)} (charge) + {Kg(m.AccessoriesWeight)} (accessoires) + {Kg(b.Tare)} (palette) = {Kg(m.TotalWeight)} kg.");
        ops.Add($"Hauteur totale : {Mm(m.EnclosureHeight)} mm (maxi {Mm(c.MaxTotalHeight)} mm).");
        return new("Les opérations, en bref", ops.Select((l, i) => $"{i + 1}. {l}").ToList());
    }

    /// <summary>Opérations simples du colisage : intérieur, couches, produits par caisse, poids brut, palette.</summary>
    private static DetailSection CaseOperations(Solution s, LoadUnit unit, CaseSpec spec, PackagingConstraints c, Article? article, Func<Guid, Article?> find, double weightLimit)
    {
        var ops = new List<string>
        {
            $"Intérieur de la caisse : {Mm(spec.InnerLength)} × {Mm(spec.InnerWidth)} × {Mm(spec.InnerHeight)} mm."
        };
        var products = unit.Items.Sum(p => p.Weight);
        if (s.Kind == PackagingKind.Homogene && article != null)
        {
            var first = unit.Items.Where(p => p.Layer == 1).ToList();
            AddLayerOps(ops, s, unit, LayerFacts(s, unit, first, article.ForPalletizing(), c, spec.InnerHeight), spec.InnerHeight, weightLimit, article.Weight, "caisse");
            ops.Add($"Poids brut : {N(unit.Items.Count)} × {Kg(article.Weight)} kg + {Kg(spec.Tare)} kg (caisse vide) = {Kg(products + spec.Tare)} kg.");
        }
        else
        {
            foreach (var u in s.Units)
            {
                ops.Add($"{(s.Units.Count > 1 ? $"Caisse {u.Index}" : "Caisse")} : {string.Join(" + ", u.Items.GroupBy(p => p.ArticleId).Select(g => $"{N(g.Count())} {find(g.Key)?.Code ?? "?"}"))} = {N(u.Items.Count)} produits ; " +
                        $"poids brut {Kg(u.Items.Sum(p => p.Weight))} + {Kg(spec.Tare)} = {Kg(u.Items.Sum(p => p.Weight) + spec.Tare)} kg.");
            }
        }

        if (s.DestinationPallet != null && s.CasesPerPallet > 0)
        {
            ops.Add($"Caisses par palette {s.DestinationPallet} : {N(s.CasesPerPallet)}.");
            ops.Add(s.Kind == PackagingKind.Homogene
                ? $"Produits par palette : {N(s.CasesPerPallet)} caisses × {N(s.ItemsPerUnit)} = {N(s.ItemsPerPallet)} produits."
                : $"Palettes : {N(s.Units.Count)} caisses ÷ {N(s.CasesPerPallet)} par palette → {N(s.PalletCount)} palette(s).");
        }

        return new("Les opérations, en bref", ops.Select((l, i) => $"{i + 1}. {l}").ToList());
    }

    /// <summary>Produits par couche, couches (hauteur, poids, limite retenue) et quantité, en opérations simples.</summary>
    private static void AddLayerOps(List<string> ops, Solution s, LoadUnit unit, Facts f, double usableH, double capacity, double weight, string container)
    {
        var n = s.ItemsPerLayer;
        var (layerH, sheet, byHeight) = (f.LayerH, f.Sheet, f.ByHeight);
        if (f.Nested)
        {
            ops.Add(f.N2 != f.N1 ? $"Produits par lit : {N(f.N1)}, puis {N(f.N2)} sur les lits décalés, en alternance." : $"Produits par lit : {N(n)}.");
            ops.Add($"Pas vertical des lits emboîtés : {Mm(layerH)} × √3/2 = {Mm(layerH)} × 0,866 = {D(f.Pitch)} mm.");
            ops.Add($"Lits par la hauteur : 1 + ({Mm(usableH)} − {Mm(layerH + f.Sheet0)}) ÷ {D(f.Pitch)} = 1 + {Dec((usableH - layerH - f.Sheet0) / f.Pitch)} → {N(byHeight)} lits " +
                    "(le lit du bas compte un diamètre, chaque lit suivant un pas ; on ne garde que les lits entiers).");
        }
        else
        {
            ops.Add($"Produits par couche : {N(n)}.");
        }

        if (layerH > 0 && !f.Nested)
        {
            var per = layerH + sheet;
            ops.Add($"Couches par la hauteur : {Mm(usableH)} ÷ {Mm(per)}{(sheet > 0 ? " (couche + intercalaire)" : "")} = {Dec(usableH / per)} → {N(byHeight)} couche(s) (on ne garde que les couches entières).");
        }

        if (f.Nested && f.N2 != f.N1 && capacity > 0 && weight > 0)
        {
            var byWeight = 0;
            while (byWeight < 10000 && f.Count(byWeight + 1) * weight <= capacity + 1e-9)
            {
                byWeight++;
            }

            ops.Add($"Poids des lits : {N(f.N1)} × {Kg(weight)} = {Kg(f.N1 * weight)} kg et {N(f.N2)} × {Kg(weight)} = {Kg(f.N2 * weight)} kg ; lits par le poids (cumul ≤ {Kg(capacity)} kg) : {N(byWeight)}.");
        }
        else if (capacity > 0 && n > 0 && weight > 0)
        {
            var layerWeight = n * weight;
            var (one, many) = f.Nested ? ("d'un lit", "lits") : ("d'une couche", "couches");
            ops.Add($"Poids {one} : {N(n)} × {Kg(weight)} kg = {Kg(layerWeight)} kg ; {many} par le poids : {Kg(capacity)} ÷ {Kg(layerWeight)} = {Dec(capacity / layerWeight)} → {N((int)Math.Floor(capacity / layerWeight + 1e-9))}.");
        }

        if (s.LayerCount != byHeight)
        {
            ops.Add($"{(f.Nested ? "Lits" : "Couches")} retenu(e)s : {N(s.LayerCount)}{(string.IsNullOrEmpty(s.LayerLimitReason) ? "" : $" (le plus petit des maxima ; limite : {s.LayerLimitReason})")}.");
        }

        if (f.Nested)
        {
            ops.Add($"Hauteur de charge : {Mm(layerH + f.Sheet0)} + {N(s.LayerCount - 1)} × {D(f.Pitch)} = {Mm(StackHeight(new PackagingConstraints(), f, s.LayerCount))} mm.");
        }

        var noun = container == "caisse" ? "par caisse" : "par palette";
        if (s.PartialTopLayer && s.LayerCount > 0)
        {
            var full = n * (s.LayerCount - 1);
            ops.Add($"Produits {noun} : {N(n)} × {N(s.LayerCount - 1)} + {N(s.ItemsPerUnit - full)} (dernière couche) = {N(s.ItemsPerUnit)}.");
        }
        else if (LayerSum(unit, f.Nested) is { } sum)
        {
            ops.Add($"Produits {noun} : {sum} = {N(s.ItemsPerUnit)}.");
        }
        else
        {
            ops.Add($"Produits {noun} : {N(n)} × {N(s.LayerCount)} = {N(s.ItemsPerUnit)}.");
        }
    }

    // ------------------------------------------------------------------ Hétérogène (palette ou caisse)

    private static IEnumerable<DetailSection> Heterogeneous(Solution s, PackagingConstraints c, double usableX, double usableY, double usableH, double capacity,
        Func<Guid, Article?> find, string container)
    {
        var all = s.Units.SelectMany(u => u.Items).ToList();
        var volume = all.Sum(p => p.DX * p.DY * p.DZ);
        var unitVolume = usableX * usableY * usableH;
        var weight = all.Sum(p => p.Weight);
        var byVolume = unitVolume > 0 ? (int)Math.Ceiling(volume / unitVolume - 1e-9) : 0;
        var byWeight = capacity > 0 ? (int)Math.Ceiling(weight / capacity - 1e-9) : 0;
        var plural = container == "caisse" ? "caisses" : "palettes";
        string Count(int k) => $"{N(k)} {(k > 1 ? plural : container)}";

        bool IsBox(Guid id) => find(id) is { CaseQuantity: not null } or { Kind: ArticleKind.Caisse };
        var allBoxes = all.Count > 0 && all.All(p => IsBox(p.ArticleId));
        yield return new("Composition", all.GroupBy(p => p.ArticleId)
            .Select(g => $"{find(g.Key)?.DisplayName ?? "?"} : {N(g.Count())} {(IsBox(g.Key) ? "colis" : "produit(s)")}, {Kg(g.Sum(p => p.Weight))} kg.")
            .Prepend($"{N(all.Count)} {(allBoxes ? "colis" : "produits")}, {Kg(weight)} kg, volume des enveloppes {(volume / 1e9).ToString("0.###", Fr)} m³.").ToList());

        var shaped = all.Select(p => p.ArticleId).Distinct().Select(find).OfType<Article>()
            .Select(a => (Article: a, Rule: TopStacking.For(a))).Where(x => x.Rule != null).ToList();
        if (shaped.Count > 0)
        {
            yield return new("Forme du dessus", shaped.Select(x =>
                $"{x.Article.Code} : {x.Rule!.ModeLabel} ({x.Rule.Summary}) — " +
                (x.Rule.Mode == TopStackMode.Direct
                    ? x.Rule.CapacityFactor < 1 ? $"charge reçue limitée à {Pct(100 * x.Rule.CapacityFactor)} % de sa capacité." : "charge reçue jusqu'à sa capacité."
                    : "rien n'est posé dessus dans un mélange (pas d'intercalaire par couche en hétérogène) : il est placé en haut d'une pile ou sans rien au-dessus.")).ToList());
        }

        yield return new("Bornes", new List<string>
        {
            $"Par le volume : ⌈{(volume / 1e9).ToString("0.###", Fr)} m³ / {(unitVolume / 1e9).ToString("0.###", Fr)} m³ utiles⌉ = {Count(byVolume)} au moins.",
            capacity > 0 ? $"Par le poids : ⌈{Kg(weight)} / {Kg(capacity)} kg⌉ = {Count(byWeight)} au moins." : "Par le poids : non limité.",
            $"Solution : {Count(s.Units.Count)}" + (s.Units.Count <= Math.Max(1, Math.Max(byVolume, byWeight)) ? " : la borne est atteinte, minimum prouvé." : "."),
            s.Units.Count > Math.Max(1, Math.Max(byVolume, byWeight))
                ? $"Pourquoi pas {N(s.Units.Count - 1)} ? Les bornes ne l'interdisent pas, mais aucune pose n'y fait tenir tous les produits en respectant les règles (appui, charge reçue, lourd sous léger, formes) : " +
                  $"le volume des enveloppes n'est jamais rempli à 100 % ({Pct(100 * volume / Math.Max(1, s.Units.Count * unitVolume))} % ici, les vides entre formes différentes sont inévitables)."
                : s.Units.Count <= 1
                    ? $"Pourquoi pas moins ? Une seule {container} : c'est le minimum possible."
                    : $"Pourquoi pas moins ? {(byWeight >= byVolume && byWeight > 0 ? $"le poids ({Kg(weight)} kg) dépasse ce que {Count(s.Units.Count - 1)} peuvent porter." : $"le volume des produits dépasse celui de {Count(s.Units.Count - 1)}.")}"
        }.Where(l => l.Length > 0).ToList());

        yield return new("Règles de pose", new List<string>
        {
            "Chaque produit est posé au sol ou sur le dessus d'autres produits, en points extrêmes (coins libres), sans chevauchement.",
            $"Appui minimal : {Pct(c.MinSupportPercent)} % de l'empreinte du produit (sauf au sol).",
            "Charge reçue : chaque produit porte au plus sa capacité (saisie, ou déduite de son type, de sa densité et de sa hauteur).",
            "Lourd sous léger et zones conseillées (bas, milieu, haut) selon le profil de gerbage de chaque article.",
            $"Un article qui remplit seul une {container} donne une {container} complète, sauf si le mélange en demande moins."
        });

        var strategyLines = new List<string>
        {
            "Trois stratégies sont calculées : couches homogènes (couches complètes par article, reliquat au-dessus), piles par article (murs successifs), densité maximale (plusieurs ordres de pose, recherche élargie pour les commandes courantes).",
            $"Retenue : {s.Title} — {s.Description}",
            "Classement : solution conforme, puis tous les produits placés, puis le moins d'unités, puis le score (35 % remplissage, 30 % stabilité, 20 % ordre lourd / léger, 15 % regroupement)."
        };
        if (s.Recommendation != null)
        {
            strategyLines.Add(s.Recommendation);
        }

        if (s.Units.Count > 0)
        {
            var fill = s.Units.Average(u => u.Metrics.FillRate);
            var support = s.Units.Average(u => u.Metrics.SupportAvg);
            var cog = s.Units.Average(u => Math.Min(100, u.Metrics.CogOffset));
            var order = s.Units.Average(u => u.Metrics.OrderRespect);
            var homogeneity = s.Units.Average(u => u.Metrics.Homogeneity);
            var stab = 100 * s.StabilityScore;
            strategyLines.Add($"Calcul du score : 0,35 × {Pct(fill)} % (remplissage) + 0,30 × {Pct(stab)} % (stabilité) + 0,20 × {Pct(order)} % (ordre lourd / léger) + 0,15 × {Pct(homogeneity)} % (regroupement) = " +
                              $"{D(0.35 * fill)} + {D(0.30 * stab)} + {D(0.20 * order)} + {D(0.15 * homogeneity)} = {D(100 * s.Score)}, soit {(100 * s.Score).ToString("0", Fr)}/100.");
            strategyLines.Add($"Stabilité = support moyen × (1 − décentrage du centre de gravité) = {Pct(support)} % × (1 − {Pct(cog)} %)" +
                              (s.Units.Count > 1 ? " (moyenne des unités)" : "") +
                              " : le décentrage est l'écart du centre de gravité au centre de la palette, rapporté à la demi-dimension de la base (le plus fort des deux sens).");
        }

        yield return new("Stratégie", strategyLines);

        var loads = new List<string>();
        foreach (var u in s.Units.Where(u => u.Metrics.HeaviestLoad > 0))
        {
            var m = u.Metrics;
            var label = s.Units.Count > 1 ? $"{(container == "caisse" ? "Caisse" : "Unité")} {u.Index} : c" : "C";
            var label2 = s.Units.Count > 1 ? $"{(container == "caisse" ? "Caisse" : "Unité")} {u.Index} : p" : "P";
            string Cap(double? cap, double load) => cap is > 0 ? $"capacité retenue {Kg(cap.Value)} kg, utilisée à {Pct(100 * load / cap.Value)} %" : "capacité non limitée";
            loads.Add($"{label}harge la plus forte reçue : {Kg(m.HeaviestLoad!.Value)} kg sur un {find(m.HeaviestLoadArticleId ?? Guid.Empty)?.Code ?? "?"} ({Cap(m.HeaviestLoadCapacity, m.HeaviestLoad.Value)}).");
            if (m.MostLoadedLoad is { } load && (m.MostLoadedArticleId != m.HeaviestLoadArticleId || Math.Abs(load - m.HeaviestLoad.Value) > 1e-6))
            {
                loads.Add($"{label2}roduit le plus sollicité : {find(m.MostLoadedArticleId ?? Guid.Empty)?.Code ?? "?"}, {Kg(load)} kg reçus ({Cap(m.MostLoadedCapacity, load)}).");
            }
        }

        if (loads.Count > 0)
        {
            loads.Add("Charge reçue = poids de tout ce qui est posé au-dessus, réparti entre les appuis selon la surface de contact (calcul du haut vers le bas).");
            var deduced = false;
            foreach (var a in all.Select(p => p.ArticleId).Distinct().Select(find).OfType<Article>())
            {
                var profile = StackingProfile.For(a);
                deduced |= !profile.CapacityEntered && a.Kind == ArticleKind.Caisse;
                loads.Add($"{a.Code} : capacité {profile.CapacityText}.");
            }

            if (deduced)
            {
                loads.Add($"Capacités déduites : un carton est supposé supporter une palette de lui-même sur {Mm(StackingProfile.ReferenceLoadHeight)} mm (auto-gerbage). " +
                          "Ce n'est pas une mesure de sa résistance à l'écrasement : pour un contrôle réel, saisir sur l'article caisse la charge maxi sur le dessus " +
                          "(résistance à la compression BCT du carton divisée par un coefficient de sécurité, en général 3 à 5 selon la durée de stockage et l'humidité).");
            }

            yield return new("Charge reçue par les produits", loads);
        }

        yield return new($"Détail par {container}", s.Units.Select(u =>
            $"{(container == "caisse" ? "Caisse" : "Unité")} {u.Index} : {N(u.Items.Count)} {Units(u.Items, find)}, {Kg(u.Items.Sum(p => p.Weight))} kg, remplissage {Pct(u.Metrics.FillRate)} %" +
            (u.IsFullPallet ? " (complète, un seul article)." : ".")).ToList());
    }
}
