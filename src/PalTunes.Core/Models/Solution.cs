namespace PalTunes.Core.Models;

/// <summary>
/// Produit placé. Repère : X selon la longueur de la base, Y selon la largeur, Z vers le haut,
/// origine au coin de la base, Z = 0 sur le dessus de la palette. Enveloppe (DX, DY, DZ) en mm.
/// </summary>
public sealed class Placement
{
    public Guid ArticleId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double DX { get; set; }
    public double DY { get; set; }
    public double DZ { get; set; }
    public ShapeKind Shape { get; set; }

    /// <summary>Tube ou bobine creux : diamètre intérieur (mm), dessiné en creux sur les schémas. 0 = plein.</summary>
    public double InnerDiameter { get; set; }

    /// <summary>Numéro de couche (1 = bas).</summary>
    public int Layer { get; set; }

    /// <summary>Ordre de pose (1 = premier).</summary>
    public int Sequence { get; set; }

    public double Weight { get; set; }

    public double MaxX => X + DX;
    public double MaxY => Y + DY;
    public double MaxZ => Z + DZ;

    public Placement Clone() => (Placement)MemberwiseClone();
}

/// <summary>Couche d'une unité de charge.</summary>
public sealed class LayerInfo
{
    public int Index { get; set; }
    public double Z { get; set; }
    public double Height { get; set; }

    /// <summary>« A », « B » (variante croisée), « Mixte »…</summary>
    public string Pattern { get; set; } = "A";

    public Guid? ArticleId { get; set; }
    public int Count { get; set; }
    public bool SlipSheetBelow { get; set; }
}

/// <summary>Base : assemblage de palettes physiques identiques (étude §5.3).</summary>
public sealed class BaseInfo
{
    public Guid PalletId { get; set; }
    public string PalletCode { get; set; } = "";
    public string PalletName { get; set; } = "";
    public PalletConstruction Construction { get; set; }
    public PalletMaterial Material { get; set; }
    public string Color { get; set; } = "#C8A165";
    public bool Rotated { get; set; }

    /// <summary>Dimensions d'une palette, orientée dans la base.</summary>
    public double PalletLength { get; set; }

    public double PalletWidth { get; set; }
    public double PalletHeight { get; set; }
    public int CountAlongLength { get; set; } = 1;
    public int CountAlongWidth { get; set; } = 1;

    /// <summary>Base = intérieur d'une caisse (analyse de colisage) : pas de palette dessinée.</summary>
    public bool IsCase { get; set; }

    public double PalletTare { get; set; }
    public double PalletDynamicLoad { get; set; }
    public double PalletStaticLoad { get; set; }

    public double Length => PalletLength * CountAlongLength;
    public double Width => PalletWidth * CountAlongWidth;
    public int PhysicalCount => CountAlongLength * CountAlongWidth;
    public double Tare => PalletTare * PhysicalCount;
    public double DynamicCapacity => PalletDynamicLoad * PhysicalCount;
    public double StaticCapacity => PalletStaticLoad * PhysicalCount;

    public string Label => PhysicalCount == 1
        ? $"{PalletCode} {Length:0} × {Width:0}"
        : $"{PhysicalCount} × {PalletCode} → {Length:0} × {Width:0}";

    public static BaseInfo From(PalletType p, bool rotated, int countL, int countW) => new()
    {
        PalletId = p.Id,
        PalletCode = p.Code,
        PalletName = p.Name,
        Construction = p.Construction,
        Material = p.Material,
        Color = p.Color,
        Rotated = rotated,
        PalletLength = rotated ? p.Width : p.Length,
        PalletWidth = rotated ? p.Length : p.Width,
        PalletHeight = p.Height,
        CountAlongLength = Math.Max(1, countL),
        CountAlongWidth = Math.Max(1, countW),
        PalletTare = p.Tare,
        PalletDynamicLoad = p.DynamicLoad,
        PalletStaticLoad = p.StaticLoad
    };
}

/// <summary>Indicateurs d'une unité de charge (étude §6, §10).</summary>
public sealed class UnitMetrics
{
    public int ItemCount { get; set; }
    public double LoadLength { get; set; }
    public double LoadWidth { get; set; }
    public double LoadHeight { get; set; }
    public double EnclosureLength { get; set; }
    public double EnclosureWidth { get; set; }
    public double EnclosureHeight { get; set; }
    public double LoadWeight { get; set; }

    /// <summary>Intercalaires, coiffe, cornières.</summary>
    public double AccessoriesWeight { get; set; }

    public double TotalWeight { get; set; }
    public int SlipSheetCount { get; set; }

    /// <summary>Volume des produits / (surface utile × hauteur de charge), en %.</summary>
    public double FillRate { get; set; }

    /// <summary>Surface au sol des produits de la couche la plus remplie / surface de base, en %.</summary>
    public double AreaRate { get; set; }

    public double SupportMin { get; set; }
    public double SupportAvg { get; set; }

    /// <summary>Part des produits reposant sur au moins deux produits (%), étude §4.7.</summary>
    public double Interlock { get; set; }

    /// <summary>Décalage du centre de gravité / demi-dimension de la base (%).</summary>
    public double CogOffsetX { get; set; }

    public double CogOffsetY { get; set; }
    public double CogHeight { get; set; }
    public double Slenderness { get; set; }

    /// <summary>Part des produits voisins d'un produit du même article (%).</summary>
    public double Homogeneity { get; set; }

    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }

    public double CogOffset => Math.Max(CogOffsetX, CogOffsetY);

    /// <summary>Hétérogène : part des appuis « dans le bon sens » (étude hétérogène §9), en %.</summary>
    public double OrderRespect { get; set; } = 100;

    /// <summary>Hétérogène : plus forte charge reçue / capacité portante, en %.</summary>
    public double CapacityUseMax { get; set; }

    /// <summary>Hétérogène : produit le plus sollicité (charge reçue / capacité la plus forte) : article, charge et capacité (kg).</summary>
    public Guid? MostLoadedArticleId { get; set; }

    public double? MostLoadedLoad { get; set; }
    public double? MostLoadedCapacity { get; set; }

    /// <summary>Hétérogène : plus forte charge reçue en kg (souvent un produit du bas), article et capacité retenue.</summary>
    public Guid? HeaviestLoadArticleId { get; set; }

    public double? HeaviestLoad { get; set; }
    public double? HeaviestLoadCapacity { get; set; }
}

/// <summary>Rectangle autour duquel sont posées les cornières (par défaut : l'emprise de la charge).</summary>
public sealed class CornerFrame
{
    public double X0 { get; set; }
    public double Y0 { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
}

public sealed class LoadUnit
{
    public int Index { get; set; } = 1;

    /// <summary>Cornières au niveau de la palette (tubes en débord) : leur cadre ; null = emprise de la charge.</summary>
    public CornerFrame? CornerFrame { get; set; }

    /// <summary>Tubes retirés pour laisser la place aux cornières restées au niveau de la palette.</summary>
    public int RemovedForCorners { get; set; }

    /// <summary>Hétérogène : palette complète d'un seul article (solution homogène), posée telle quelle.</summary>
    public bool IsFullPallet { get; set; }

    /// <summary>Cadre effectif des cornières.</summary>
    public (double X0, double Y0, double X1, double Y1) CornerBounds(UnitMetrics m) =>
        CornerFrame is { } f ? (f.X0, f.Y0, f.X1, f.Y1) : (m.MinX, m.MinY, m.MaxX, m.MaxY);
    [System.Text.Json.Serialization.JsonConverter(typeof(Storage.PlacementListConverter))]
    public List<Placement> Items { get; set; } = [];
    public List<LayerInfo> Layers { get; set; } = [];
    public UnitMetrics Metrics { get; set; } = new();
}

/// <summary>Solution de palettisation : une ou plusieurs unités de charge sur la même base.</summary>
public sealed class Solution
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public PackagingKind Kind { get; set; }
    public MixedStrategy? Strategy { get; set; }
    public StackPattern Pattern { get; set; }
    public BaseInfo Base { get; set; } = new();
    public List<LoadUnit> Units { get; set; } = [];

    // Homogène
    public Guid? ArticleId { get; set; }
    public int ItemsPerLayer { get; set; }
    public int LayerCount { get; set; }
    public int ItemsPerUnit { get; set; }
    public int UpperBound { get; set; }
    public string LayerPatternKind { get; set; } = "";

    /// <summary>Description de l'orientation retenue (dimension verticale, axe…).</summary>
    public string OrientationText { get; set; } = "";

    /// <summary>Facteur de résistance du schéma (§6.2) : 1 colonne, 0,5 croisé, × 0,7 si débord.</summary>
    public double StrengthFactor { get; set; } = 1;

    /// <summary>Nombre de niveaux de la pile retenu (§10.4) : 1 = non gerbable.</summary>
    public int StackLevels { get; set; } = 1;

    /// <summary>Nombre de gerbages : conditionnements gerbés sur le premier (0 = non gerbable).</summary>
    public int Stackings => Math.Max(0, StackLevels - 1);

    public string StackLimitReason { get; set; } = "";
    public string LayerLimitReason { get; set; } = "";
    public int SlipSheets { get; set; }

    /// <summary>Dernière couche incomplète (quantité imposée ou poids).</summary>
    public bool PartialTopLayer { get; set; }
    public double CapHeight { get; set; }

    // Colisage : palettisation de la caisse sur la palette de destination
    public int CasesPerPallet { get; set; }
    public int ItemsPerPallet { get; set; }
    public string? DestinationPallet { get; set; }

    /// <summary>Colisage hétérogène : palettes nécessaires pour toutes les caisses de la solution.</summary>
    public int PalletCount { get; set; }

    // Hétérogène
    public int RequestedItems { get; set; }
    public int UnplacedItems { get; set; }

    /// <summary>Articles exclus du calcul car ils ne tiennent pas seuls sur la base (code × quantité : raison).</summary>
    public List<string> ExcludedArticles { get; set; } = [];

    public double Score { get; set; }
    public double StabilityScore { get; set; }
    public bool Recommended { get; set; }
    public string? Recommendation { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> Violations { get; set; } = [];

    public bool IsCompliant => Violations.Count == 0;
    public bool ProvenOptimal => Kind == PackagingKind.Homogene && UpperBound > 0 && ItemsPerLayer >= UpperBound;
    public LoadUnit? FirstUnit => Units.Count > 0 ? Units[0] : null;
    public int UnitCount => Units.Count;
    public int TotalItems => Units.Sum(u => u.Items.Count);

    /// <summary>Densité de transport (§11.2) : produits × gerbages / m² d'encombrement.</summary>
    public double TransportDensity
    {
        get
        {
            if (FirstUnit is not { } u || u.Metrics.EnclosureLength <= 0 || u.Metrics.EnclosureWidth <= 0)
            {
                return 0;
            }

            return u.Items.Count * StackLevels / (u.Metrics.EnclosureLength * u.Metrics.EnclosureWidth / 1e6);
        }
    }

    public string PatternLabel => Pattern switch
    {
        StackPattern.Croise => "Croisé",
        StackPattern.Quinconce => "Quinconce",
        StackPattern.Mixte => "Mixte",
        _ => "Colonne"
    };
}
