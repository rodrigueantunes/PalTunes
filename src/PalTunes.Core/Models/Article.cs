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
    public double Diameter { get; set; }
    public double InnerDiameter { get; set; }
    public double Weight { get; set; }

    public OrientationRule Orientation { get; set; } = OrientationRule.HautImpose;
    public CoilAxis CoilAxis { get; set; } = CoilAxis.Vertical;

    /// <summary>Poids maximal supportable par un exemplaire (kg). Null = non limité.</summary>
    public double? MaxLoadOnTop { get; set; }

    /// <summary>Nombre maximal de couches superposées de ce produit. Null = non limité.</summary>
    public int? MaxLayers { get; set; }

    public bool Fragile { get; set; }

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

    public bool IsCylinder => Kind is ArticleKind.Bobine or ArticleKind.Tube or ArticleKind.Fut;

    /// <summary>Longueur d'axe d'un cylindre (laize, longueur de tube, hauteur de fût).</summary>
    public double AxisLength => Kind switch
    {
        ArticleKind.Bobine => Width,
        ArticleKind.Tube => Length,
        ArticleKind.Fut => Height,
        _ => 0
    };

    /// <summary>Volume réel en mm³ (cylindres : π r² × axe, bobines : mandrin déduit).</summary>
    public double Volume => IsCylinder
        ? Math.PI * (Diameter * Diameter - (Kind == ArticleKind.Bobine ? InnerDiameter * InnerDiameter : 0)) / 4 * AxisLength
        : Length * Width * Height;

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
                ArticleKind.Bobine => $"Ø{F(Diameter)} × laize {F(Width)}",
                ArticleKind.Tube => $"Ø{F(Diameter)} × {F(Length)}",
                ArticleKind.Fut => $"Ø{F(Diameter)} × h {F(Height)}",
                ArticleKind.Plaque => $"{F(Length)} × {F(Width)} × ép. {F(Height)}",
                _ => $"{F(Length)} × {F(Width)} × {F(Height)}"
            };
        }
    }

    public override string ToString() => DisplayName;
}
