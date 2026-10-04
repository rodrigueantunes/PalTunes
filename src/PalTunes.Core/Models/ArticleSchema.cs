namespace PalTunes.Core.Models;

public enum ArticleField
{
    Length,
    Width,
    Height,
    Diameter,
    InnerDiameter
}

/// <summary>Champ dimensionnel d'un type d'article : libellé métier et caractère obligatoire.</summary>
public sealed record FieldSpec(ArticleField Field, string Label, bool Required, string Hint);

/// <summary>
/// Données minimales par type d'article (étude §2) : quels champs afficher, avec quel libellé, lesquels sont obligatoires.
/// Le poids est toujours obligatoire.
/// </summary>
public static class ArticleSchema
{
    public static IReadOnlyList<ArticleKind> Kinds { get; } = Enum.GetValues<ArticleKind>();

    public static string KindLabel(ArticleKind kind) => kind switch
    {
        ArticleKind.Caisse => "Caisse / carton",
        ArticleKind.Bobine => "Bobine",
        ArticleKind.Tube => "Tube",
        ArticleKind.Plaque => "Plaque",
        ArticleKind.Sac => "Sac",
        ArticleKind.Fut => "Fût",
        ArticleKind.Bac => "Bac / caisse plastique",
        _ => "Autre"
    };

    public static string KindDescription(ArticleKind kind) => kind switch
    {
        ArticleKind.Caisse => "Pavé. Haut imposé par défaut ; la résistance à la compression oriente vers la colonne.",
        ArticleKind.Bobine => "Cylindre. Axe vertical (standard) ou horizontal (calage obligatoire).",
        ArticleKind.Tube => "Cylindre. Axe horizontal (lits carrés ou en quinconce) et/ou vertical (debout) : le meilleur est proposé.",
        ArticleKind.Plaque => "Pavé plat posé à plat. Plus grande que la palette : palettes multiples.",
        ArticleKind.Sac => "Pavé déformable posé à plat, toujours en croisé.",
        ArticleKind.Fut => "Cylindre debout uniquement.",
        ArticleKind.Bac => "Pavé rigide, haut imposé, empilé en colonne.",
        _ => "Pavé générique."
    };

    /// <summary>Glyphe Segoe MDL2 Assets associé au type (arborescence, listes).</summary>
    public static string KindGlyph(ArticleKind kind) => kind switch
    {
        ArticleKind.Caisse => "",
        ArticleKind.Bobine => "",
        ArticleKind.Tube => "",
        ArticleKind.Plaque => "",
        ArticleKind.Sac => "",
        ArticleKind.Fut => "",
        ArticleKind.Bac => "",
        _ => ""
    };

    public static IReadOnlyList<FieldSpec> Fields(ArticleKind kind) => kind switch
    {
        ArticleKind.Bobine =>
        [
            new(ArticleField.Diameter, "Diamètre extérieur (mm)", true, "Ø hors tout de la bobine"),
            new(ArticleField.Width, "Laize (mm)", true, "Largeur de la bobine = longueur selon l'axe"),
            new(ArticleField.InnerDiameter, "Diamètre mandrin (mm)", false, "Facultatif : Ø intérieur, déduit du volume")
        ],
        ArticleKind.Tube =>
        [
            new(ArticleField.Diameter, "Diamètre extérieur (mm)", true, "Ø hors tout du tube"),
            new(ArticleField.Length, "Longueur (mm)", true, "Longueur du tube")
        ],
        ArticleKind.Fut =>
        [
            new(ArticleField.Diameter, "Diamètre (mm)", true, "Ø hors tout (fût 200 L : 585)"),
            new(ArticleField.Height, "Hauteur (mm)", true, "Hauteur du fût debout")
        ],
        ArticleKind.Plaque =>
        [
            new(ArticleField.Length, "Longueur (mm)", true, "Plus grande dimension à plat"),
            new(ArticleField.Width, "Largeur (mm)", true, "Plus petite dimension à plat"),
            new(ArticleField.Height, "Épaisseur (mm)", true, "Épaisseur d'une plaque (ou d'un paquet)")
        ],
        _ =>
        [
            new(ArticleField.Length, "Longueur (mm)", true, "Dimension au sol la plus grande"),
            new(ArticleField.Width, "Largeur (mm)", true, "Dimension au sol la plus petite"),
            new(ArticleField.Height, "Hauteur (mm)", true, "Hauteur, haut en haut")
        ]
    };

    public static bool UsesOrientation(ArticleKind kind) => kind is ArticleKind.Caisse or ArticleKind.Autre;

    /// <summary>Tubes et bobines : axe vertical, horizontal ou indifférent (le meilleur est proposé).</summary>
    public static bool UsesCoilAxis(ArticleKind kind) => kind is ArticleKind.Bobine or ArticleKind.Tube;

    /// <summary>Axe par défaut à la création : bobine debout, tube indifférent (les deux axes sont calculés).</summary>
    public static CoilAxis DefaultAxis(ArticleKind kind) => kind == ArticleKind.Tube ? CoilAxis.Indifferent : CoilAxis.Vertical;

    public static double Get(Article a, ArticleField field) => field switch
    {
        ArticleField.Length => a.Length,
        ArticleField.Width => a.Width,
        ArticleField.Height => a.Height,
        ArticleField.Diameter => a.Diameter,
        _ => a.InnerDiameter
    };

    /// <summary>Contrôle les données minimales ; une liste vide signifie « article calculable ».</summary>
    public static List<string> Validate(Article a)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(a.Code))
        {
            errors.Add("Le code article est obligatoire.");
        }

        foreach (var spec in Fields(a.Kind))
        {
            var v = Get(a, spec.Field);
            if (spec.Required && !(v > 0))
            {
                errors.Add($"{spec.Label} : valeur > 0 obligatoire.");
            }
            else if (v < 0)
            {
                errors.Add($"{spec.Label} : valeur négative.");
            }
        }

        if (!(a.Weight > 0))
        {
            errors.Add("Poids (kg) : valeur > 0 obligatoire.");
        }

        if (a.Kind == ArticleKind.Bobine && a.InnerDiameter > 0 && a.InnerDiameter >= a.Diameter)
        {
            errors.Add("Le diamètre mandrin doit être inférieur au diamètre extérieur.");
        }

        if (a.MaxLoadOnTop is < 0)
        {
            errors.Add("Charge maxi sur le dessus : valeur négative.");
        }

        if (a.MaxLayers is < 1)
        {
            errors.Add("Couches maxi : au moins 1.");
        }

        return errors;
    }
}
