using System.Globalization;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.Core.Export;

/// <summary>Section de l'explication d'un calcul : titre et lignes (une étape ou une règle par ligne).</summary>
public sealed record DetailSection(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// Détails du calcul, pas à pas et avec les chiffres de la solution affichée : données retenues, surface et hauteur
/// utiles, plan de couche, nombre de couches et facteur limitant, poids, gerbage ; hétérogène : stratégie, bornes et
/// règles ; colisage : caisse, limites de poids, palettisation des caisses.
/// </summary>
public static class CalculationDetails
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static string Mm(double v) => v.ToString("#,0", Fr);
    private static string Kg(double v) => v.ToString("#,0.###", Fr);
    private static string N(int v) => v.ToString("#,0", Fr);
    private static string Pct(double v) => v.ToString("0.#", Fr);

    // ------------------------------------------------------------------ Palette

    /// <summary>Conditionnement sur palette (homogène ou hétérogène), unité affichée.</summary>
    public static List<DetailSection> Pallet(Solution s, LoadUnit unit, PackagingConstraints c, Article? article, Func<Guid, Article?> find)
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
            sections.AddRange(Homogeneous(s, unit, c, article, usableH, capacity, "palette"));
        }
        else
        {
            sections.AddRange(Heterogeneous(s, c, usableX, usableY, usableH, capacity, find, "palette"));
        }

        var m = unit.Metrics;
        sections.Add(new("Poids et encombrement", new List<string>
        {
            $"Poids de la charge = {N(unit.Items.Count)} produits = {Kg(m.LoadWeight)} kg{(capacity > 0 ? $" (≤ {Kg(capacity)} kg)" : "")}.",
            $"Poids total = charge + accessoires + palette(s) = {Kg(m.LoadWeight)} + {Kg(m.AccessoriesWeight)} + {Kg(b.Tare)} = {Kg(m.TotalWeight)} kg.",
            $"Hauteur d'encombrement = plancher + charge{(c.CapHeight > 0 ? " + coiffe" : "")} = {Mm(m.EnclosureHeight)} mm (maxi {Mm(c.MaxTotalHeight)} mm).",
            $"Encombrement au sol : {Mm(m.EnclosureLength)} × {Mm(m.EnclosureWidth)} mm (charge {Mm(m.LoadLength)} × {Mm(m.LoadWidth)} mm).",
            $"Remplissage = volume des produits / (surface utile × hauteur de charge) = {Pct(m.FillRate)} % ; surface au sol couverte {Pct(m.AreaRate)} %."
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

        if (s.Kind == PackagingKind.Homogene && article != null)
        {
            sections.AddRange(Homogeneous(s, unit, c, article, spec.InnerHeight, weightLimit, "caisse"));
        }
        else
        {
            sections.AddRange(Heterogeneous(s, c, spec.InnerLength, spec.InnerWidth, spec.InnerHeight, weightLimit, find, "caisse"));
        }

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

        return sections;
    }

    // ------------------------------------------------------------------ Homogène (palette ou caisse)

    private static IEnumerable<DetailSection> Homogeneous(Solution s, LoadUnit unit, PackagingConstraints c, Article article, double usableH, double capacity, string container)
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
                ? s.ProvenOptimal
                    ? $"Borne théorique : {N(s.UpperBound)} par couche (surface utile / surface du produit, côtés réduits aux combinaisons de longueurs possibles) : le plan l'atteint, il est optimal."
                    : $"Borne théorique : {N(s.UpperBound)} par couche ; le plan retenu en place {N(n)}."
                : ""
        };
        yield return new("Plan de couche", planLines.Where(l => l.Length > 0).ToList());

        var layerH = unit.Layers.Count > 0 ? unit.Layers[0].Height : first.Count > 0 ? first.Max(p => p.DZ) : a.Height;
        var sheet = c.SlipSheetThickness > 0 ? c.SlipSheetThickness : 0;
        var byHeight = layerH > 0 ? (int)Math.Floor((usableH + 1e-6) / (layerH + sheet)) : 0;
        var layerLines = new List<string>
        {
            $"Hauteur d'une couche : {Mm(layerH)} mm{(sheet > 0 ? $" + intercalaire {sheet.ToString("0.#", Fr)} mm" : "")}.",
            $"Par la hauteur : ⌊{Mm(usableH)} / {Mm(layerH + sheet)}⌋ = {N(byHeight)} couche(s)."
        };
        if (capacity > 0 && n > 0 && article.Weight > 0)
        {
            layerLines.Add($"Par le poids : ⌊{Kg(capacity)} / ({N(n)} × {Kg(article.Weight)})⌋ = {N((int)Math.Floor(capacity / (n * article.Weight) + 1e-9))} couche(s).");
        }

        if (article.MaxLayers is { } ml)
        {
            layerLines.Add($"Couches maxi de l'article : {N(ml)}.");
        }

        if (article.EffectiveMaxLoadOnTop is { } top)
        {
            layerLines.Add($"Résistance : charge maxi sur un produit {Kg(top)} kg → {N(1 + (int)Math.Floor(top * s.StrengthFactor / article.Weight + 1e-9))} couche(s) au plus" +
                           (Math.Abs(s.StrengthFactor - 1) > 1e-9 ? $" (facteur de répartition {s.StrengthFactor.ToString("0.##", Fr)} : produits en appui sur plusieurs)." : "."));
        }

        layerLines.Add($"Retenu : {N(s.LayerCount)} couche(s){(string.IsNullOrEmpty(s.LayerLimitReason) ? "" : $", limite : {s.LayerLimitReason}")}" +
                       (s.PartialTopLayer ? " (dernière couche incomplète)." : "."));
        layerLines.Add($"Produits = {N(n)} par couche × {N(s.LayerCount)} couche(s){(s.PartialTopLayer ? " (dernière incomplète)" : "")} = {N(s.ItemsPerUnit)}.");
        yield return new("Nombre de couches", layerLines);

        if (s.Recommendation != null)
        {
            yield return new("Choix de la solution", [s.Recommendation, "Classement : solutions conformes d'abord, puis le plus de produits, puis la stabilité (colonne, croisé, imbrication)."]);
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

        yield return new("Composition", all.GroupBy(p => p.ArticleId)
            .Select(g => $"{find(g.Key)?.DisplayName ?? "?"} : {N(g.Count())} produit(s), {Kg(g.Sum(p => p.Weight))} kg.")
            .Prepend($"{N(all.Count)} produits, {Kg(weight)} kg, volume des enveloppes {(volume / 1e9).ToString("0.###", Fr)} m³.").ToList());

        yield return new("Bornes", new List<string>
        {
            $"Par le volume : ⌈{(volume / 1e9).ToString("0.###", Fr)} m³ / {(unitVolume / 1e9).ToString("0.###", Fr)} m³ utiles⌉ = {N(byVolume)} {plural} au moins.",
            capacity > 0 ? $"Par le poids : ⌈{Kg(weight)} / {Kg(capacity)} kg⌉ = {N(byWeight)} {plural} au moins." : "Par le poids : non limité.",
            $"Solution : {N(s.Units.Count)} {plural}" + (s.Units.Count <= Math.Max(1, Math.Max(byVolume, byWeight)) ? " : la borne est atteinte, minimum prouvé." : ".")
        });

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

        yield return new("Stratégie", strategyLines);

        yield return new($"Détail par {container}", s.Units.Select(u =>
            $"{(container == "caisse" ? "Caisse" : "Unité")} {u.Index} : {N(u.Items.Count)} produits, {Kg(u.Items.Sum(p => p.Weight))} kg, remplissage {Pct(u.Metrics.FillRate)} %" +
            (u.IsFullPallet ? " (complète, un seul article)." : ".")).ToList());
    }
}
