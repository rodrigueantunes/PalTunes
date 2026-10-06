namespace PalTunes.Core.Models;

public enum ArticleField
{
    Length,
    Width,
    Height,
    Diameter,
    InnerDiameter,
    FoldedLength,
    FoldedWidth,
    FoldedHeight
}

/// <summary>Champ dimensionnel d'un type d'article : libellé métier et caractère obligatoire.</summary>
public sealed record FieldSpec(ArticleField Field, string Label, bool Required, string Hint);

/// <summary>
/// Données minimales par type d'article (étude §2) : quels champs afficher, avec quel libellé, lesquels sont obligatoires.
/// Le poids est toujours obligatoire.
/// </summary>
public static class ArticleSchema
{
    /// <summary>Types proposés, « Autre » (générique) en dernier, après les autres types courants.</summary>
    public static IReadOnlyList<ArticleKind> Kinds { get; } = [.. Enum.GetValues<ArticleKind>().Where(k => k != ArticleKind.Autre), ArticleKind.Autre];

    public static string KindLabel(ArticleKind kind) => kind switch
    {
        ArticleKind.Caisse => "Caisse / carton",
        ArticleKind.Bobine => "Bobine",
        ArticleKind.Tube => "Tube",
        ArticleKind.Plaque => "Plaque",
        ArticleKind.Sac => "Sac",
        ArticleKind.Fut => "Fût",
        ArticleKind.Bac => "Bac / caisse plastique",
        ArticleKind.Bidon => "Bidon / jerrican",
        ArticleKind.Seau => "Seau / pot",
        ArticleKind.Bouteille => "Bouteille / flacon",
        ArticleKind.Cuve => "Cuve IBC / GRV",
        _ => "Autre"
    };

    /// <summary>Types de la catégorie « Autre » : générique et formes courantes hors norme (bidon, seau, bouteille, cuve).</summary>
    public static bool IsOther(ArticleKind kind) => kind is ArticleKind.Autre or ArticleKind.Bidon or ArticleKind.Seau or ArticleKind.Bouteille or ArticleKind.Cuve;

    /// <summary>
    /// Forme de base utilisée par les moteurs (homogène, hétérogène, colisage, profil de gerbage) : bidon et cuve se
    /// palettisent comme un bac rigide (haut imposé, colonne), seau et bouteille comme un fût (cylindre debout).
    /// </summary>
    public static ArticleKind EngineKind(ArticleKind kind) => kind switch
    {
        ArticleKind.Bidon or ArticleKind.Cuve => ArticleKind.Bac,
        ArticleKind.Seau or ArticleKind.Bouteille => ArticleKind.Fut,
        _ => kind
    };

    /// <summary>Produits dont le dessus n'est pas plat (poignée, col, anse) : intercalaire conseillé pour les gerber.</summary>
    public static bool NeedsSlipSheetToStack(ArticleKind kind) => kind is ArticleKind.Bidon or ArticleKind.Seau or ArticleKind.Bouteille;

    public static string KindDescription(ArticleKind kind) => kind switch
    {
        ArticleKind.Caisse => "Pavé. Haut imposé par défaut ; la résistance à la compression oriente vers la colonne.",
        ArticleKind.Bobine => "Cylindre. Axe vertical (standard) ou horizontal (calage obligatoire).",
        ArticleKind.Tube => "Cylindre. Axe horizontal (lits carrés ou en quinconce) et/ou vertical (debout) : le meilleur est proposé.",
        ArticleKind.Plaque => "Pavé plat posé à plat. Plus grande que la palette : palettes multiples.",
        ArticleKind.Sac => "Pavé déformable posé à plat, toujours en croisé.",
        ArticleKind.Fut => "Cylindre debout uniquement.",
        ArticleKind.Bac => "Pavé rigide, haut imposé, empilé en colonne.",
        ArticleKind.Bidon => "Bidon plastique à poignée (jerrican) : pavé hors tout, haut imposé, en colonne ; intercalaire conseillé pour gerber (poignée, bouchon).",
        ArticleKind.Seau => "Seau ou pot à anse : cylindre debout (diamètre du haut), en colonne ; intercalaire conseillé pour gerber.",
        ArticleKind.Bouteille => "Bouteille ou flacon : cylindre debout, en colonne ; intercalaire conseillé pour gerber (col, bouchon).",
        ArticleKind.Cuve => "Cuve IBC / GRV : pavé hors tout sur sa palette intégrée, haut imposé, en colonne.",
        _ => "Pavé générique."
    };

    /// <summary>Glyphe Segoe MDL2 Assets associé au type (arborescence, listes).</summary>
    public static string KindGlyph(ArticleKind kind) => kind switch
    {
        ArticleKind.Bidon or ArticleKind.Cuve => KindGlyph(ArticleKind.Bac),
        ArticleKind.Seau or ArticleKind.Bouteille => KindGlyph(ArticleKind.Fut),
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
            new(ArticleField.Length, "Longueur (mm)", true, "Longueur du tube"),
            new(ArticleField.InnerDiameter, "Diamètre intérieur (mm)", false, "Facultatif : tube creux (mandrin, bague) = Ø extérieur − 2 × épaisseur")
        ],
        ArticleKind.Fut =>
        [
            new(ArticleField.Diameter, "Diamètre (mm)", true, "Ø hors tout (fût 200 L : 585)"),
            new(ArticleField.Height, "Hauteur (mm)", true, "Hauteur du fût debout")
        ],
        ArticleKind.Caisse =>
        [
            new(ArticleField.Length, "Longueur (mm)", true, "Dimension au sol la plus grande"),
            new(ArticleField.Width, "Largeur (mm)", true, "Dimension au sol la plus petite"),
            new(ArticleField.Height, "Hauteur (mm)", true, "Hauteur, haut en haut"),
            new(ArticleField.FoldedLength, "Longueur pliée (mm)", false, "Facultatif : carton livré plié (à plat). Renseignée, elle remplace la longueur pour le conditionnement"),
            new(ArticleField.FoldedWidth, "Largeur pliée (mm)", false, "Facultatif : carton plié. Renseignée, elle remplace la largeur pour le conditionnement"),
            new(ArticleField.FoldedHeight, "Hauteur pliée (mm)", false, "Facultatif : carton plié. Renseignée, elle remplace la hauteur pour le conditionnement")
        ],
        ArticleKind.Bidon =>
        [
            new(ArticleField.Length, "Longueur (mm)", true, "Hors tout, dimension au sol la plus grande (jerrican 20 L : environ 290)"),
            new(ArticleField.Width, "Largeur (mm)", true, "Hors tout, dimension au sol la plus petite (jerrican 20 L : environ 190)"),
            new(ArticleField.Height, "Hauteur hors tout (mm)", true, "Poignée et bouchon compris")
        ],
        ArticleKind.Cuve =>
        [
            new(ArticleField.Length, "Longueur (mm)", true, "Hors tout (GRV 1000 L : 1200)"),
            new(ArticleField.Width, "Largeur (mm)", true, "Hors tout (GRV 1000 L : 1000)"),
            new(ArticleField.Height, "Hauteur hors tout (mm)", true, "Palette intégrée et bouchon compris (GRV 1000 L : 1160)")
        ],
        ArticleKind.Seau =>
        [
            new(ArticleField.Diameter, "Diamètre du haut (mm)", true, "Plus grand diamètre, rebord compris"),
            new(ArticleField.Height, "Hauteur (mm)", true, "Anse rabattue, couvercle compris")
        ],
        ArticleKind.Bouteille =>
        [
            new(ArticleField.Diameter, "Diamètre (mm)", true, "Plus grand diamètre du corps"),
            new(ArticleField.Height, "Hauteur (mm)", true, "Bouchon compris")
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
        ArticleField.FoldedLength => a.FoldedLength,
        ArticleField.FoldedWidth => a.FoldedWidth,
        ArticleField.FoldedHeight => a.FoldedHeight,
        _ => a.InnerDiameter
    };

    /// <summary>Densité au-delà de laquelle un poids unitaire est physiquement impossible (kg/dm³ ; tungstène 19,3).</summary>
    public const double MaxPlausibleDensity = 20;

    /// <summary>Densité apparente de la matière (kg/dm³) : poids / volume de matière (alésage des tubes creux déduit).</summary>
    public static double Density(Article a) => a.MaterialVolume > 0 ? a.Weight / (a.MaterialVolume / 1e6) : 0;

    /// <summary>
    /// Poids unitaire impossible pour les dimensions saisies (plus dense que tout matériau courant) : erreur de saisie
    /// ou d'unité (g au lieu de kg, poids d'un lot…). Null si le poids est plausible.
    /// </summary>
    public static string? WeightWarning(Article a)
    {
        var density = Density(a);
        if (a.Weight <= 0 || density <= MaxPlausibleDensity)
        {
            return null;
        }

        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        return $"Poids unitaire suspect : {a.Weight.ToString("0.#####", fr)} kg pour {a.DimensionsText} mm, soit {density.ToString("#,0", fr)} kg/dm³ de matière " +
               "(acier 7,8 ; plomb 11,3) : vérifiez le poids, en kg pour un article.";
    }

    /// <summary>Tube ou bobine sans diamètre extérieur : le diamètre intérieur est pris comme diamètre (à vérifier).</summary>
    public static string? DiameterWarning(Article a) => a.UsesInnerAsDiameter
        ? $"Diamètre extérieur absent : le diamètre intérieur ({a.InnerDiameter.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} mm) " +
          "est pris comme diamètre (article considéré plein) ; renseignez le diamètre extérieur."
        : null;

    /// <summary>Avertissements de la fiche (diamètre de secours, poids suspect), un par ligne ; null s'il n'y en a pas.</summary>
    public static string? Warnings(Article a)
    {
        var list = new[] { DiameterWarning(a), WeightWarning(a) }.Where(w => w != null).ToList();
        return list.Count == 0 ? null : string.Join(Environment.NewLine, list);
    }

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
            if (spec.Field == ArticleField.Diameter && a.UsesInnerAsDiameter)
            {
                continue; // diamètre intérieur pris comme diamètre (avertissement, pas d'erreur)
            }

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

        if (a.Kind == ArticleKind.Caisse && a.QuantityPerCase is <= 0)
        {
            errors.Add("Quantité par caisse : nombre entier > 0, ou vide.");
        }

        if (a.Kind == ArticleKind.Bobine && a.Diameter > 0 && a.InnerDiameter > 0 && a.InnerDiameter >= a.Diameter)
        {
            errors.Add("Le diamètre mandrin doit être inférieur au diamètre extérieur.");
        }

        if (a.Kind == ArticleKind.Tube && a.Diameter > 0 && a.InnerDiameter > 0 && a.InnerDiameter >= a.Diameter)
        {
            errors.Add("Le diamètre intérieur doit être inférieur au diamètre extérieur.");
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
