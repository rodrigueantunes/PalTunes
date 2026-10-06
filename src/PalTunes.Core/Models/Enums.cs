namespace PalTunes.Core.Models;

/// <summary>Type d'article (étude §2) : détermine la forme de calcul et les données obligatoires.</summary>
public enum ArticleKind
{
    Caisse,
    Bobine,
    Tube,
    Plaque,
    Sac,
    Fut,
    Bac,
    Autre,

    // Autres types courants hors des formes de base (enregistrés par leur nom : ajoutés en fin de liste).
    /// <summary>Bidon / jerrican plastique à poignée : pavé, haut imposé, poignée et bouchon sur le dessus.</summary>
    Bidon,

    /// <summary>Seau / pot à anse : cylindre debout.</summary>
    Seau,

    /// <summary>Bouteille / flacon : cylindre debout, col et bouchon sur le dessus.</summary>
    Bouteille,

    /// <summary>Cuve IBC / GRV (1000 L) : pavé sur palette intégrée, cage métallique.</summary>
    Cuve
}

/// <summary>Orientation d'un produit pavé : haut imposé (seule rotation dans le plan) ou libre (6 orientations).</summary>
public enum OrientationRule
{
    HautImpose,
    Libre
}

/// <summary>Position de l'axe d'une bobine : verticale (« eye to sky »), horizontale (« eye to wall ») ou indifférente.</summary>
public enum CoilAxis
{
    Vertical,
    Horizontal,
    Indifferent
}

public enum PalletMaterial
{
    Bois,
    Plastique,
    Carton,
    Metal,
    BoisMoule
}

/// <summary>Construction de la palette, utilisée pour la représentation 3D / 2D (étude §8).</summary>
public enum PalletConstruction
{
    /// <summary>9 blocs, 3 semelles (EUR / EPAL).</summary>
    Blocs9Semelles3,

    /// <summary>9 blocs sans semelles (palette perdue).</summary>
    Blocs9,

    /// <summary>Plateau + 3 patins (plastique type H1, palettes CP).</summary>
    Patins3,

    /// <summary>Plateau + 9 pieds (plastique emboîtable).</summary>
    Pieds9,

    /// <summary>Bloc plein (carton, bois moulé).</summary>
    Plein
}

public enum PackagingKind
{
    Homogene,
    Heterogene
}

/// <summary>Schéma de gerbage interne des couches (étude §4.7).</summary>
public enum StackPattern
{
    Colonne,
    Croise,
    Quinconce,
    Mixte
}

/// <summary>Forme d'un placement : pavé, ou cylindre dont l'axe est selon Z, X ou Y.</summary>
public enum ShapeKind
{
    Box,
    CylinderZ,
    CylinderX,
    CylinderY
}

/// <summary>Stratégie de construction hétérogène (étude §7.1).</summary>
public enum MixedStrategy
{
    CouchesHomogenes,
    PilesParArticle,
    DensiteMaximale
}
