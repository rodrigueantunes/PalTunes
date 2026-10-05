using System.Globalization;
using System.Text;
using PalTunes.Core.Import;
using PalTunes.Core.Models;

namespace PalTunes.Core.Export;

public sealed record SpecRow(string Group, string Label, string Value, string Unit = "");

/// <summary>
/// Spécification de palettisation d'un conditionnement (étude §12) : mêmes données pour l'écran, la fiche imprimée
/// et l'export CSV des conditionnements mono-article.
/// </summary>
public static class PackagingSpec
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public const string GroupSpec = "Spécification de palettisation";
    public const string GroupPallet = "Dimensions de la palette";
    public const string GroupLoad = "Dimensions de la charge";
    public const string GroupEnclosure = "Encombrement de palettisation";
    public const string GroupAccessories = "Accessoires";
    public const string GroupQuality = "Indicateurs";

    private static string Mm(double v) => v.ToString("0", Fr);
    private static string Kg(double v) => v.ToString(Formats.TotalWeight, Fr);
    private static string Pct(double v) => v.ToString("0.#", Fr);

    public static List<SpecRow> Rows(Solution s, PackagingConstraints c, Article? article = null)
    {
        var rows = new List<SpecRow>();
        var u = s.FirstUnit;
        var m = u?.Metrics ?? new UnitMetrics();
        var b = s.Base;

        rows.Add(new(GroupSpec, "Nombre de produits conditionnés par palette produit", s.ItemsPerUnit.ToString(Fr)));
        rows.Add(new(GroupSpec, "Nombre de palettes physiques par conditionnement", b.PhysicalCount.ToString(Fr),
            b.PhysicalCount > 1 ? $"{b.CountAlongLength} × {b.CountAlongWidth}" : ""));
        rows.Add(new(GroupSpec, "Nombre de gerbages", s.Stackings.ToString(Fr),
            (s.Stackings == 0 ? "non gerbable" : $"{s.Stackings} conditionnement(s) gerbé(s) sur le premier") +
            (string.IsNullOrEmpty(s.StackLimitReason) ? "" : $" · limite : {s.StackLimitReason}")));
        if (s.Kind == PackagingKind.Homogene)
        {
            rows.Add(new(GroupSpec, "Produits par couche", s.ItemsPerLayer.ToString(Fr)));
            if (article?.CaseQuantity is { } perCase)
            {
                rows.Add(new(GroupSpec, "Quantité par caisse", perCase.ToString("#,0", Fr), "produits par caisse"));
                rows.Add(new(GroupSpec, "Produits contenus par palette produit", ((long)s.ItemsPerUnit * perCase).ToString("#,0", Fr),
                    $"{s.ItemsPerUnit.ToString("#,0", Fr)} caisses × {perCase.ToString("#,0", Fr)}"));
            }
            if (article is { IsFolded: true })
            {
                var (fl, fw, fh) = article.PackedDimensions;
                rows.Add(new(GroupSpec, "Dimensions du produit prises en compte", $"{Mm(fl)} × {Mm(fw)} × {Mm(fh)} mm",
                    $"carton plié (monté : {Mm(article.Length)} × {Mm(article.Width)} × {Mm(article.Height)} mm)"));
            }
        }
        else
        {
            rows.Add(new(GroupSpec, "Unités de charge", s.UnitCount.ToString(Fr)));
        }

        rows.Add(new(GroupSpec, "Nombre de couches", s.LayerCount.ToString(Fr), s.LayerLimitReason));
        rows.Add(new(GroupSpec, "Schéma", s.PatternLabel, s.OrientationText));

        rows.Add(new(GroupPallet, "Type de palette", b.PalletCode, b.PalletName));
        rows.Add(new(GroupPallet, "Longueur de la palette", Mm(b.Length), "mm"));
        rows.Add(new(GroupPallet, "Largeur de la palette", Mm(b.Width), "mm"));
        rows.Add(new(GroupPallet, "Hauteur de bois de la palette", Mm(b.PalletHeight), "mm"));

        rows.Add(new(GroupLoad, "Longueur de la charge", Mm(m.LoadLength), "mm"));
        rows.Add(new(GroupLoad, "Largeur de la charge", Mm(m.LoadWidth), "mm"));
        rows.Add(new(GroupLoad, "Hauteur de la charge", Mm(m.LoadHeight), "mm"));

        rows.Add(new(GroupEnclosure, "Longueur d'encombrement de la charge", Mm(m.EnclosureLength),
            "mm" + EnclosureNote(m.EnclosureLength, b.Length, m.LoadLength, m.MinX, m.MaxX, c)));
        rows.Add(new(GroupEnclosure, "Largeur d'encombrement de la charge", Mm(m.EnclosureWidth),
            "mm" + EnclosureNote(m.EnclosureWidth, b.Width, m.LoadWidth, m.MinY, m.MaxY, c)));
        rows.Add(new(GroupEnclosure, "Hauteur d'encombrement de la charge", Mm(m.EnclosureHeight), "mm" + HeightNote(b, m, c)));
        if (s.StackLevels > 1)
        {
            rows.Add(new(GroupEnclosure, "Hauteur gerbée", Mm(m.EnclosureHeight * s.StackLevels), $"mm (1 + {s.Stackings} gerbage(s) × {Mm(m.EnclosureHeight)})"));
        }

        if (c.SlipSheetThickness > 0)
        {
            rows.Add(new(GroupAccessories, "Intercalaires", m.SlipSheetCount.ToString(Fr), $"ép. {Mm(c.SlipSheetThickness)} mm"));
        }

        if (c.CapHeight > 0)
        {
            rows.Add(new(GroupAccessories, "Coiffe", Mm(c.CapHeight), "mm"));
        }

        if (c.Corners)
        {
            rows.Add(new(GroupAccessories, "Cornières", "4", $"{Mm(c.CornerLeg)} × {Mm(c.CornerLeg)} × ép. {Mm(c.CornerThickness)}, h {Mm(c.CornerHeight > 0 ? c.CornerHeight : m.LoadHeight)} mm" +
                (c.CornersInside ? " · contenues dans la palette (charge en retrait de " + Mm(c.CornerThickness) + " mm par côté)" : " · à l'extérieur de la charge") +
                (u?.CornerFrame != null ? $" · au niveau de la palette (tubes en débord) : {u.RemovedForCorners} tube(s) retiré(s)" : "")));
        }

        if (c.FilmThickness > 0)
        {
            rows.Add(new(GroupAccessories, "Film étirable", Mm(c.FilmThickness), "mm par côté, ajouté à la longueur et à la largeur d'encombrement"));
        }

        if (c.Straps > 0)
        {
            rows.Add(new(GroupAccessories, "Cerclages", c.Straps.ToString(Fr), "dans le sens de la longueur, sous le plateau de la palette (épaisseur négligeable)"));
        }

        rows.Add(new(GroupQuality, "Poids de la charge", Kg(m.LoadWeight), "kg"));
        if (m.AccessoriesWeight > 0)
        {
            rows.Add(new(GroupQuality, "Poids des accessoires", Kg(m.AccessoriesWeight), "kg"));
        }

        rows.Add(new(GroupQuality, "Poids total (palette comprise)", Kg(m.TotalWeight), "kg"));
        rows.Add(new(GroupQuality, "Taux de remplissage volumique", Pct(m.FillRate), "%"));
        rows.Add(new(GroupQuality, "Taux de surface au sol", Pct(m.AreaRate), "%"));
        rows.Add(new(GroupQuality, "Support minimal / moyen", $"{Pct(m.SupportMin)} / {Pct(m.SupportAvg)}", "%"));
        if (m.Interlock > 0)
        {
            rows.Add(new(GroupQuality, "Imbrication (pontage)", Pct(m.Interlock), "%"));
        }

        if (s.Kind == PackagingKind.Heterogene)
        {
            rows.Add(new(GroupQuality, "Ordre lourd / léger respecté", Pct(m.OrderRespect), "% des appuis (plus léger dessus, ou appui rigide)"));
            rows.Add(new(GroupQuality, "Capacité portante utilisée maxi", Pct(m.CapacityUseMax), "% de la capacité (saisie ou déduite) du produit le plus sollicité"));
        }

        if (b.PhysicalCount > 1)
        {
            rows.Add(new(GroupQuality, "Poids par palette physique", Kg(m.LoadWeight / b.PhysicalCount), $"kg (charge dynamique {Kg(b.PalletDynamicLoad)} kg)"));
        }

        rows.Add(new(GroupQuality, "Décalage du centre de gravité", Pct(m.CogOffset), "%"));
        rows.Add(new(GroupQuality, "Hauteur du centre de gravité", Mm(m.CogHeight), "mm"));
        rows.Add(new(GroupQuality, "Élancement (hauteur / base)", m.Slenderness.ToString("0.00", Fr)));
        if (s.Kind == PackagingKind.Homogene && s.UpperBound > 0)
        {
            rows.Add(new(GroupQuality, "Borne théorique par couche", s.UpperBound.ToString(Fr), s.ProvenOptimal ? "optimal prouvé" : ""));
        }

        return rows;
    }

    /// <summary>
    /// Explication d'une cote d'encombrement quand elle diffère à la fois de la palette et de la charge :
    /// « (charge 1200 + cornières 2 × 5 = +10 mm) ». Rien si elle est égale à l'une des deux.
    /// </summary>
    public static string EnclosureNote(double enclosure, double palletSize, double loadSize, double loadMin, double loadMax, PackagingConstraints c)
    {
        if (Math.Abs(enclosure - palletSize) < 0.5 || Math.Abs(enclosure - loadSize) < 0.5 || loadSize <= 0)
        {
            return "";
        }

        var corner = c.Corners ? c.CornerThickness : 0;
        var film = c.FilmThickness;
        var wrap = corner + film;
        var parts = new List<string>();
        if (corner > 0)
        {
            parts.Add($"cornières 2 × {Mm(corner)}");
        }

        if (film > 0)
        {
            parts.Add($"film 2 × {Mm(film)}");
        }

        // Cas usuel : charge centrée qui dépasse (avec ses accessoires) des deux côtés de la palette.
        if (Math.Abs(enclosure - (loadSize + 2 * wrap)) < 0.5 && parts.Count > 0)
        {
            return $" (charge {Mm(loadSize)} + {string.Join(" + ", parts)} = +{Mm(enclosure - loadSize)} mm)";
        }

        // Charge décentrée ou débordant d'un seul côté : on donne les dépassements de chaque côté de la palette.
        var before = Math.Max(0, -(loadMin - wrap));
        var after = Math.Max(0, loadMax + wrap - palletSize);
        var detail = parts.Count > 0 ? $", {string.Join(" + ", parts)} compris" : "";
        return $" (palette {Mm(palletSize)} + dépassement {Mm(before)} + {Mm(after)}{detail} = +{Mm(enclosure - palletSize)} mm)";
    }

    private static string HeightNote(BaseInfo b, UnitMetrics m, PackagingConstraints c)
    {
        if (m.LoadHeight <= 0)
        {
            return "";
        }

        var parts = new List<string>();
        if (b.PalletHeight > 0)
        {
            parts.Add($"bois {Mm(b.PalletHeight)}");
        }

        parts.Add($"charge {Mm(m.LoadHeight)}");
        if (c.CapHeight > 0)
        {
            parts.Add($"coiffe {Mm(c.CapHeight)}");
        }

        return parts.Count > 1 ? $" ({string.Join(" + ", parts)})" : "";
    }

    public static readonly string[] ExportHeaders =
    [
        "Code conditionnement", "Désignation conditionnement", "Code article", "Désignation article", "Client", "Type article", "Palette",
        "Nombre de produits conditionnés par palette produit", "Nombre de palettes physiques par conditionnement", "Nombre de gerbages",
        "Longueur de la palette (mm)", "Largeur de la palette (mm)", "Hauteur de bois de la palette (mm)",
        "Longueur de la charge (mm)", "Largeur de la charge (mm)", "Hauteur de la charge (mm)",
        "Longueur d'encombrement de la charge (mm)", "Largeur d'encombrement de la charge (mm)", "Hauteur d'encombrement de la charge (mm)",
        "Produits par couche", "Nombre de couches", "Schéma", "Orientation", "Intercalaires", "Cornières",
        "Poids de la charge (kg)", "Poids total (kg)", "Taux de remplissage (%)",
        "Plan par couche", "Intercalaires (couches)",
        "Quantité par caisse", "Produits contenus par palette produit"
    ];

    /// <summary>Export CSV (« ; », UTF-8 BOM) des conditionnements mono-article ayant une solution retenue.</summary>
    public static string ExportCsv(IEnumerable<(Packaging Packaging, Article? Article)> rows, out int skipped) =>
        ExportCsv(rows, out skipped, null);

    /// <param name="clientLabel">Libellé du client cité par l'article (« CODE - Nom ») ; null = code brut.</param>
    public static string ExportCsv(IEnumerable<(Packaging Packaging, Article? Article)> rows, out int skipped, Func<Guid, string>? codeOf,
        Func<string?, string>? clientLabel = null)
    {
        skipped = 0;
        var sb = new StringBuilder();
        sb.AppendLine(Csv.Line(ExportHeaders));
        foreach (var (p, a) in rows)
        {
            if (p.Kind != PackagingKind.Homogene || p.Solution is not { FirstUnit: not null } s)
            {
                skipped++;
                continue;
            }

            var m = s.FirstUnit!.Metrics;
            var b = s.Base;
            sb.AppendLine(Csv.Line(
            [
                p.Code, p.Name, a?.Code, a?.Designation, clientLabel != null ? clientLabel(a?.Client) : a?.Client, a?.KindLabel, $"{b.PalletCode} – {b.PalletName}",
                s.ItemsPerUnit.ToString(Fr), b.PhysicalCount.ToString(Fr), s.Stackings.ToString(Fr),
                Mm(b.Length), Mm(b.Width), Mm(b.PalletHeight),
                Mm(m.LoadLength), Mm(m.LoadWidth), Mm(m.LoadHeight),
                Mm(m.EnclosureLength), Mm(m.EnclosureWidth), Mm(m.EnclosureHeight),
                s.ItemsPerLayer.ToString(Fr), s.LayerCount.ToString(Fr), s.PatternLabel, s.OrientationText,
                m.SlipSheetCount.ToString(Fr), p.Constraints.Corners ? "4" : "0",
                m.LoadWeight.ToString("0.###", Fr), m.TotalWeight.ToString("0.###", Fr), Pct(m.FillRate),
                PalletizationPlan.Summary(s.FirstUnit!, id => codeOf?.Invoke(id) ?? a?.Code ?? ""), PalletizationPlan.SlipSheetSummary(s.FirstUnit!),
                a?.CaseQuantity?.ToString(Fr) ?? "", a?.CaseQuantity is { } q ? ((long)s.ItemsPerUnit * q).ToString(Fr) : ""
            ]));
        }

        return sb.ToString();
    }
}
