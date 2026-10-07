using System.Globalization;

namespace PalTunes.Core.Models;

/// <summary>
/// Article de la base (étude §2, §9.1). Les dimensions utilisées dépendent du type :
/// pavés (L, l, h), bobine (Ø, laize = <see cref="Width"/>), tube (Ø, <see cref="Length"/>), fût (Ø, <see cref="Height"/>),
/// plaque (L, l, épaisseur = <see cref="Height"/>). Dimensions en mm, poids en kg.
/// </summary>
public sealed class Article
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public ArticleKind Kind { get; set; } = ArticleKind.Caisse;

    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// Carton livré plié (à plat) : longueur, largeur et hauteur une fois plié. Chacune, renseignée (> 0), remplace la
    /// dimension montée pour le conditionnement (palettisation, colisage).
    /// </summary>
    public double FoldedLength { get; set; }

    public double FoldedWidth { get; set; }

    public double FoldedHeight { get; set; }

    public double Diameter { get; set; }
    public double InnerDiameter { get; set; }
    public double Weight { get; set; }

    /// <summary>
    /// Caisse / carton : nombre de produits contenus (facultatif). Renseignée, la spécification de palettisation indique
    /// aussi les produits par caisse et par palette ; vide, rien ne change.
    /// </summary>
    public int? QuantityPerCase { get; set; }

    /// <summary>Caisse créée au colisage : produit contenu et caisse, pour réimprimer la fiche de colisage.</summary>
    public CaseContent? CaseContent { get; set; }

    public OrientationRule Orientation { get; set; } = OrientationRule.HautImpose;
    public CoilAxis CoilAxis { get; set; } = CoilAxis.Vertical;

    /// <summary>Poids maximal supportable par un exemplaire (kg). Null = non limité.</summary>
    public double? MaxLoadOnTop { get; set; }

    /// <summary>Nombre maximal de couches superposées de ce produit. Null = non limité.</summary>
    public int? MaxLayers { get; set; }

    public bool Fragile { get; set; }

    /// <summary>Bidon, seau, bouteille : forme du dessus (droit ou arrondi). Null = non renseignée.</summary>
    public TopShape? TopShape { get; set; }

    /// <summary>Angle du dessus par rapport à l'horizontale (°) : pente d'un dessus droit, bord d'un dessus bombé.</summary>
    public double? TopAngle { get; set; }

    /// <summary>Poignée ou anse : encastrée, saillante, rabattable, aucune.</summary>
    public HandleKind? Handle { get; set; }

    /// <summary>Poignée arrondie (section ronde : contact sur une ligne) ; null ou faux = poignée à dessus plat.</summary>
    public bool? HandleRounded { get; set; }

    /// <summary>Longueur de la poignée (mm), au plus la longueur du produit (diamètre pour un fût, un seau, une bouteille). Null = proportions usuelles.</summary>
    public double? HandleLength { get; set; }

    /// <summary>Largeur de la poignée (mm), au plus la largeur du produit.</summary>
    public double? HandleWidth { get; set; }

    /// <summary>Hauteur de la poignée (mm), au plus la hauteur du produit : saillie au-dessus du corps, ou profondeur du puits.</summary>
    public double? HandleHeight { get; set; }

    /// <summary>Ancien format (0.2.1) : dimensions de poignée en % du produit, converties en mm à l'ouverture de la base.</summary>
    public double? HandleLengthPercent { get; set; }

    public double? HandleWidthPercent { get; set; }

    public double? HandleHeightPercent { get; set; }

    /// <summary>
    /// Dimensions de référence de la poignée : longueur, largeur et hauteur du produit (diamètre, diamètre, hauteur pour
    /// un fût, un seau, une bouteille).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (double Length, double Width, double Height) HandleReference => Kind is ArticleKind.Fut or ArticleKind.Seau or ArticleKind.Bouteille
        ? (Diameter, Diameter, Height)
        : (Length, Width, Height);

    /// <summary>Convertit les dimensions de poignée en % (0.2.1) en mm. Renvoie vrai si quelque chose a changé.</summary>
    public bool ConvertHandlePercents()
    {
        if (HandleLengthPercent == null && HandleWidthPercent == null && HandleHeightPercent == null)
        {
            return false;
        }

        var (l, w, h) = HandleReference;
        static double? Mm(double? pct, double reference) => pct is { } p && reference > 0 ? Math.Round(Math.Clamp(p, 0, 100) / 100 * reference, 1) : null;
        HandleLength ??= Mm(HandleLengthPercent, l);
        HandleWidth ??= Mm(HandleWidthPercent, w);
        HandleHeight ??= Mm(HandleHeightPercent, h);
        HandleLengthPercent = null;
        HandleWidthPercent = null;
        HandleHeightPercent = null;
        return true;
    }

    /// <summary>Poignée arrondie : inclinaison du côté gauche par rapport à la verticale (°, 0 = côté droit vertical).</summary>
    public double? HandleAngleLeft { get; set; }

    /// <summary>Poignée arrondie : inclinaison du côté droit par rapport à la verticale (°).</summary>
    public double? HandleAngleRight { get; set; }

    /// <summary>Poignée pleine : moulée d'un seul tenant avec le corps, sans ouverture (décoché par défaut).</summary>
    public bool HandleSolid { get; set; }

    /// <summary>
    /// Sac : tassement accepté à la mise en caisse (air chassé en appuyant). Décoché par défaut : épaisseur saisie
    /// conservée.
    /// </summary>
    public bool Compressible { get; set; }

    /// <summary>Tassement maxi de l'épaisseur du sac (%) ; null = valeur usuelle (10 %).</summary>
    public double? CompressionPercent { get; set; }

    /// <summary>Taux de tassement appliqué à la mise en caisse (0 si non coché ou si ce n'est pas un sac).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double CompressionRate => Kind == ArticleKind.Sac && Compressible
        ? Math.Clamp(CompressionPercent ?? ArticleSchema.DefaultCompressionPercent, 0, ArticleSchema.MaxCompressionPercent) / 100
        : 0;

    // Champs facultatifs (classement, recherche, export)
    public string? Designation { get; set; }
    public string? Client { get; set; }
    public string? Family { get; set; }
    public string? SubFamily { get; set; }
    public string? CustomerRef { get; set; }
    public string? Ean { get; set; }
    public string? Color { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    public Article Clone() => (Article)MemberwiseClone();

    /// <summary>Quantité par caisse renseignée (caisse / carton seulement).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? CaseQuantity => Kind == ArticleKind.Caisse && QuantityPerCase is > 0 ? QuantityPerCase : null;

    /// <summary>Carton plié : sa hauteur pliée remplace la hauteur pour la palettisation et le colisage.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFolded => Kind == ArticleKind.Caisse && (FoldedLength > 0 || FoldedWidth > 0 || FoldedHeight > 0);

    /// <summary>Dimensions prises en compte pour le conditionnement (pliées si renseignées).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public (double Length, double Width, double Height) PackedDimensions => IsFolded
        ? (FoldedLength > 0 ? FoldedLength : Length, FoldedWidth > 0 ? FoldedWidth : Width, FoldedHeight > 0 ? FoldedHeight : Height)
        : (Length, Width, Height);

    /// <summary>Tube ou bobine sans diamètre extérieur mais avec un diamètre intérieur : celui-ci sert de diamètre.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesInnerAsDiameter => Kind is ArticleKind.Tube or ArticleKind.Bobine && !(Diameter > 0) && InnerDiameter > 0;

    /// <summary>Tube ou bobine creux (diamètres extérieur et intérieur) : diamètre de l'alésage, 0 si plein.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double HollowDiameter => Kind is ArticleKind.Tube or ArticleKind.Bobine && Diameter > 0 && InnerDiameter > 0 && InnerDiameter < Diameter
        ? InnerDiameter
        : 0;

    /// <summary>Diamètre pris en compte : le diamètre extérieur, à défaut le diamètre intérieur (tube, bobine).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveDiameter => UsesInnerAsDiameter ? InnerDiameter : Diameter;

    /// <summary>Article avec son diamètre pris en compte (diamètre intérieur à défaut d'extérieur, article alors plein).</summary>
    public Article WithEffectiveDiameter()
    {
        if (!UsesInnerAsDiameter)
        {
            return this;
        }

        var a = Clone();
        a.Diameter = InnerDiameter;
        a.InnerDiameter = 0;
        return a;
    }

    /// <summary>
    /// Article tel qu'il est conditionné (même identifiant) : diamètre pris en compte pour un tube ou une bobine,
    /// dimensions pliées pour un carton plié.
    /// </summary>
    public Article ForPalletizing()
    {
        var effective = WithEffectiveDiameter();
        if (effective.IsFolded)
        {
            effective = effective.Clone();
            (effective.Length, effective.Width, effective.Height) = PackedDimensions;
        }

        // Bidon, seau, bouteille, cuve : forme de base pour les moteurs (bac rigide ou fût debout).
        var engineKind = ArticleSchema.EngineKind(Kind);
        if (engineKind != Kind)
        {
            effective = ReferenceEquals(effective, this) ? Clone() : effective;
            effective.Kind = engineKind;
            effective.Orientation = OrientationRule.HautImpose;
        }

        return effective;
    }

    public bool IsCylinder => Kind is ArticleKind.Bobine or ArticleKind.Tube or ArticleKind.Fut or ArticleKind.Seau or ArticleKind.Bouteille;

    /// <summary>Longueur d'axe d'un cylindre (laize, longueur de tube, hauteur de fût).</summary>
    public double AxisLength => Kind switch
    {
        ArticleKind.Bobine => Width,
        ArticleKind.Tube => Length,
        ArticleKind.Fut or ArticleKind.Seau or ArticleKind.Bouteille => Height,
        _ => 0
    };

    /// <summary>Volume réel en mm³ (cylindres : π r² × axe, bobines : mandrin déduit).</summary>
    public double Volume => IsCylinder
        ? Math.PI * (EffectiveDiameter * EffectiveDiameter - (Kind == ArticleKind.Bobine && !UsesInnerAsDiameter ? InnerDiameter * InnerDiameter : 0)) / 4 * AxisLength
        : Length * Width * Height;

    /// <summary>Volume de matière en mm³ : enveloppe moins l'alésage des tubes et bobines creux (contrôle du poids).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double MaterialVolume => IsCylinder && Kind is ArticleKind.Tube or ArticleKind.Bobine && !UsesInnerAsDiameter && InnerDiameter > 0 && InnerDiameter < Diameter
        ? Math.PI * (Diameter * Diameter - InnerDiameter * InnerDiameter) / 4 * AxisLength
        : Volume;

    /// <summary>Charge admissible sur le dessus : 0 si fragile, null si non limitée.</summary>
    public double? EffectiveMaxLoadOnTop => Fragile ? 0 : MaxLoadOnTop;

    public string DisplayName => string.IsNullOrWhiteSpace(Designation) ? Code : $"{Code} – {Designation}";

    public string KindLabel => ArticleSchema.KindLabel(Kind);

    public string DimensionsText
    {
        get
        {
            static string F(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);
            return Kind switch
            {
                ArticleKind.Bobine => $"Ø{F(EffectiveDiameter)}{(UsesInnerAsDiameter ? " (Ø int.)" : "")} × laize {F(Width)}",
                ArticleKind.Tube => $"Ø{F(EffectiveDiameter)}{(UsesInnerAsDiameter ? " (Ø int.)" : "")} × {F(Length)}",
                ArticleKind.Fut or ArticleKind.Seau or ArticleKind.Bouteille => $"Ø{F(Diameter)} × h {F(Height)}",
                ArticleKind.Plaque => $"{F(Length)} × {F(Width)} × ép. {F(Height)}",
                ArticleKind.Caisse when IsFolded =>
                    $"{F(Length)} × {F(Width)} × {F(Height)} (plié {F(PackedDimensions.Length)} × {F(PackedDimensions.Width)} × {F(PackedDimensions.Height)})",
                _ => $"{F(Length)} × {F(Width)} × {F(Height)}"
            };
        }
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Contenu d'un article caisse créé au colisage : produit, caisse du catalogue (code, ou caisse spécifique) et
/// caractéristiques utilisées, pour recalculer et réimprimer la fiche de colisage. Caisse mixte (plusieurs articles) :
/// <see cref="Lines"/> donne chaque article et sa quantité, <see cref="ArticleId"/> l'article le plus nombreux.
/// </summary>
public sealed record CaseContent(
    Guid ArticleId,
    string? CaseTypeCode,
    double InnerLength,
    double InnerWidth,
    double InnerHeight,
    double WallThickness,
    double Tare,
    double Gap,
    CoilAxis? Axis,
    List<CaseContentLine>? Lines = null)
{
    /// <summary>Caisse mixte : plusieurs articles dans la même caisse.</summary>
    public bool IsMixed => Lines is { Count: > 1 };
}

/// <summary>Article d'une caisse mixte et sa quantité dans la caisse.</summary>
public sealed record CaseContentLine(Guid ArticleId, int Quantity);
