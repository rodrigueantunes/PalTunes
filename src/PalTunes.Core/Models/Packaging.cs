namespace PalTunes.Core.Models;

/// <summary>Contraintes d'un conditionnement (étude §5, §9.3). Dimensions en mm, poids en kg.</summary>
public sealed class PackagingConstraints
{
    /// <summary>Hauteur maximale totale, palette comprise.</summary>
    public double MaxTotalHeight { get; set; } = 1800;

    /// <summary>Poids maximal de la charge. 0 = charge dynamique des palettes de la base.</summary>
    public double MaxLoadWeight { get; set; }

    /// <summary>Débord par côté dans le sens de la longueur (négatif = retrait).</summary>
    public double OverhangLength { get; set; }

    /// <summary>Débord par côté dans le sens de la largeur (négatif = retrait).</summary>
    public double OverhangWidth { get; set; }

    /// <summary>Jeu entre produits voisins.</summary>
    public double Gap { get; set; }

    public double SlipSheetThickness { get; set; }

    /// <summary>Un intercalaire toutes les n couches (1 = entre chaque couche).</summary>
    public int SlipSheetEvery { get; set; } = 1;

    /// <summary>Intercalaire aussi posé sur la palette, sous la première couche.</summary>
    public bool SlipSheetOnPallet { get; set; }

    /// <summary>Poids d'un intercalaire (kg).</summary>
    public double SlipSheetWeight { get; set; }

    /// <summary>Hauteur de la coiffe / planche de dessus.</summary>
    public double CapHeight { get; set; }

    public double CapWeight { get; set; }

    /// <summary>Cornières (coins) verticales aux 4 angles de la charge : maintien latéral, anti-chute des tubes.</summary>
    public bool Corners { get; set; }

    /// <summary>Épaisseur des cornières (ajoutée à l'encombrement de chaque côté).</summary>
    public double CornerThickness { get; set; } = 5;

    /// <summary>
    /// Cornières contenues dans la palette : la surface utile de la charge est réduite de leur épaisseur de chaque côté,
    /// pour qu'elles ne dépassent pas de la base. Décoché par défaut (cornières à l'extérieur de la charge).
    /// </summary>
    public bool CornersInside { get; set; }

    /// <summary>
    /// Tubes couchés en débord : coché, les cornières suivent les tubes comme pour les autres produits ; décoché (défaut),
    /// elles restent au niveau de la palette (elles ne tiendraient pas au bout des tubes) et les tubes qui les gênent sont retirés.
    /// </summary>
    public bool CornersFollowTubes { get; set; }

    /// <summary>Retrait de la charge dû aux cornières contenues (mm par côté).</summary>
    public double CornerInset => Corners && CornersInside ? Math.Max(0, CornerThickness) : 0;

    /// <summary>Largeur d'aile des cornières.</summary>
    public double CornerLeg { get; set; } = 50;

    /// <summary>Hauteur des cornières (0 = hauteur de la charge).</summary>
    public double CornerHeight { get; set; }

    /// <summary>Poids d'une cornière (kg).</summary>
    public double CornerWeight { get; set; }

    /// <summary>Épaisseur du film étirable (ajoutée à l'encombrement de chaque côté). 0 = pas de film.</summary>
    public double FilmThickness { get; set; }

    /// <summary>Nombre de cerclages (information de la fiche).</summary>
    public int Straps { get; set; }

    /// <summary>Axe imposé aux tubes et bobines (null = automatique : le meilleur est proposé).</summary>
    public CoilAxis? ForcedAxis { get; set; }

    /// <summary>Taux de support minimal (%) en hétérogène.</summary>
    public double MinSupportPercent { get; set; } = 80;

    /// <summary>
    /// Nombre maximal de niveaux de la pile (stockage interne : 1 = non gerbable). La saisie et l'affichage utilisent
    /// <see cref="MaxStacking"/> : nombre de conditionnements gerbés sur le premier (0 = non gerbable).
    /// </summary>
    public int MaxStackLevels { get; set; } = 1;

    /// <summary>Gerbages maxi : 0 = non gerbable (premier niveau seul), 1 = un conditionnement gerbé dessus, etc.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int MaxStacking
    {
        get => Math.Max(0, MaxStackLevels - 1);
        set => MaxStackLevels = Math.Max(0, value) + 1;
    }

    /// <summary>Hauteur maximale de la pile de conditionnements gerbés (camion : 2700).</summary>
    public double MaxStackedHeight { get; set; } = 2700;

    public bool CenterLoad { get; set; } = true;

    /// <summary>Autorise une dernière couche incomplète quand le poids est limitant.</summary>
    public bool AllowPartialLayer { get; set; }

    public PackagingConstraints Clone() => (PackagingConstraints)MemberwiseClone();
}

public sealed class PackagingLine
{
    public Guid ArticleId { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>Conditionnement (unité de charge) : base, contraintes, contenu et solution retenue (étude §9.3).</summary>
public sealed class Packaging
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public PackagingKind Kind { get; set; } = PackagingKind.Homogene;

    public Guid? PalletId { get; set; }

    /// <summary>Palette tournée de 90° : sa largeur est placée dans le sens de la longueur de la base.</summary>
    public bool PalletRotated { get; set; }

    public int CountAlongLength { get; set; } = 1;
    public int CountAlongWidth { get; set; } = 1;

    public PackagingConstraints Constraints { get; set; } = new();

    /// <summary>Article (conditionnement homogène).</summary>
    public Guid? ArticleId { get; set; }

    /// <summary>Quantité imposée par conditionnement (homogène). Null = maximum.</summary>
    public int? TargetQuantity { get; set; }

    /// <summary>Lignes article × quantité (conditionnement hétérogène).</summary>
    public List<PackagingLine> Lines { get; set; } = [];

    /// <summary>Solution retenue, figée à l'enregistrement et exportée telle quelle.</summary>
    public Solution? Solution { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    public int PhysicalPalletCount => Math.Max(1, CountAlongLength) * Math.Max(1, CountAlongWidth);

    public string KindLabel => Kind == PackagingKind.Homogene ? "Homogène" : "Hétérogène";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : $"{Code} – {Name}";

    public Packaging Clone()
    {
        var copy = (Packaging)MemberwiseClone();
        copy.Constraints = Constraints.Clone();
        copy.Lines = Lines.Select(l => new PackagingLine { ArticleId = l.ArticleId, Quantity = l.Quantity }).ToList();
        return copy;
    }

    public override string ToString() => DisplayName;
}
