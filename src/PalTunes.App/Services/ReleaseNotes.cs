namespace PalTunes.App.Services;

public sealed record ReleaseNote(string Version, string Title, IReadOnlyList<string> Items);

/// <summary>Notes de version affichées une fois à chaque nouvelle version (et sur clic de la pastille de version).</summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new("0.0.4", "Palettisation hétérogène approfondie",
        [
            "Étude approfondie de la palette hétérogène (docs/ETUDE_HETEROGENE.md) : 11 règles de métier, profil de gerbage, ordre de pose, score de placement, contraintes et cas de contrôle.",
            "Profil de gerbage de chaque article, déduit sans aucune donnée obligatoire en plus : classe (carton, rigide, souple, roulant), zone conseillée (bas, milieu, haut) et poids qu'il peut porter (auto-gerbage sur 1 656 mm ; × 0,5 pour un carton en appui décalé, × 1 en colonne alignée). La charge maxi saisie ou « fragile » priment. Affiché sur la fiche article.",
            "Ordre de pose : zone, capacité portante, poids, surface au sol, hauteur (couches de niveau), article. Lourd et résistant en bas, léger et fragile en haut.",
            "Contraintes réalistes : plaques portées sur 95 % de leur surface, appui de 90 % sur un sac, tubes et bobines couchés ne portant que leurs semblables, dessus circulaires des fûts et bobines debout, capacité propagée jusqu'à la palette.",
            "Placement : cote la plus basse, couches de niveau, centre de gravité rapproché du centre, charge recentrée sur la palette.",
            "Nouveaux indicateurs : ordre lourd / léger respecté, capacité portante utilisée maxi, poids par palette physique ; nouveau score (35 % remplissage, 30 % stabilité, 20 % ordre, 15 % homogénéité).",
            "Lignes hétérogènes : zone conseillée, capacité et sous-total de poids par ligne ; total de la commande et rappel de la charge admissible.",
            "Onglet « Ordre de pose » : la liste numérotée des produits à poser (article, couche, position, poids).",
            "Gerbage : 0 = non gerbable (premier niveau seul), 1 = un conditionnement gerbé dessus, etc. (saisie, récapitulatif, export, clients). Les bases existantes sont converties automatiquement.",
            "Débord palette autorisé (longueur, largeur, 0 par défaut) : libellés clarifiés dans les contraintes ; ajouté aussi à la palette de destination du colisage."
        ]),
        new("0.0.3", "Catalogue de caisses et caisse ouvrable",
        [
            "Colisage : catalogue de 15 caisses par défaut (cartons modulaires ISO 300 × 200 à 600 × 400, cartons standard, bacs plastiques, caisses bois), modifiable comme les palettes (onglet « Catalogue des caisses »).",
            "Trois modes : « La meilleure » compare toutes les caisses actives et recommande la meilleure composition (volume intérieur le mieux rempli) ; « Du catalogue » ne propose que les caisses possibles pour l'article ; « Spécifique » conserve la saisie libre des caractéristiques.",
            "Caisse ouverte : contenu visible en 3D (rabats relevés, parois avant translucides) et plan intérieur en 2D. Caisse fermée : vue extérieure en 3D et en 2D, avec les dimensions extérieures.",
            "Palette de destination (EUR 1200 × 800 par défaut, modifiable, hauteur maxi réglable) : chaque caisse est palettisée et « La meilleure » retient celle qui donne le plus de produits par palette (caisses / palette affichées sur chaque solution).",
            "« Forcer la caisse » (décoché par défaut) : tout le catalogue est proposé, même les caisses non recommandées ou trop petites, la caisse choisie est imposée et le choix de la palette de destination est alors désactivé.",
            "L'article caisse créé reprend le nom, la couleur et les dimensions extérieures de la caisse choisie ; « Créer et palettiser » utilise la palette de destination.",
            "Conditionnements : option « Cornières contenues dans la palette » (décochée par défaut) : la charge est mise en retrait de l'épaisseur des cornières, l'encombrement ne dépasse plus la palette."
        ]),
        new("0.0.2", "Clients, encombrement expliqué, film et cerclages",
        [
            "Espace Clients (en haut à gauche) : coordonnées, exigences de conditionnement (palette imposée, hauteur maxi, gerbage) appliquées en palettisant ses articles, liste de ses articles, import / export CSV.",
            "Les clients cités par les articles (saisie ou import) sont créés automatiquement ; un renommage est répercuté sur les articles.",
            "Récapitulatif : quand une cote d'encombrement diffère de la palette et de la charge, le calcul est donné entre parenthèses (ex. « charge 1200 + cornières 2 × 5 = +10 mm ») ; hauteur détaillée (bois + charge + coiffe).",
            "Schémas 2D : cotes d'encombrement (orange) en longueur, largeur et hauteur, en plus de la palette et de la charge.",
            "Film étirable et cerclages représentés en 3D et en 2D ; le film est compté dans l'encombrement.",
            "Poids unitaire des articles jusqu'à 5 décimales (0,00001 kg), à la saisie, à l'import et à l'export.",
            "Raccourcis : Ctrl+1 Clients, Ctrl+2 Articles, Ctrl+3 Palettes, Ctrl+4 Conditionnements, Ctrl+5 Colisage."
        ]),
        new("0.0.1", "Première version",
        [
            "Base articles : caisse / carton, bobine, tube, plaque, sac, fût, bac, autre. Seules les données utiles au calcul sont obligatoires selon le type ; désignation, client, famille, sous-famille, réf. client, EAN, couleur et notes sont facultatifs.",
            "Arborescence de la base (Client › Famille › Sous-famille, Famille › Type, Type › Client ou liste) et recherche plein texte.",
            "Import CSV des articles (séparateur et décimales détectés, synonymes d'en-têtes, mise à jour par code, rapport ligne par ligne), modèle CSV et aide des colonnes (F1).",
            "Catalogue de 27 palettes : EUR / EPAL, ISO, US, Asie, CP1 à CP9, plastiques grises (9 pieds, 3 patins, demi, quart), perdue, carton. Représentation 3D selon la construction.",
            "Conditionnements homogènes : plans de couche optimaux (guillotine exacte + moulinet), borne théorique et preuve d'optimalité, schémas colonne / croisé, cylindres en maille carrée ou quinconce, solutions classées et recommandation argumentée.",
            "Tubes et bobines : axe horizontal et/ou vertical, le meilleur est proposé ; axe forçable par conditionnement.",
            "Palettes physiques multiples (ex. 2 × 800 × 1200 pour une charge 1600 × 1200) et assistant « Proposer » qui compare toutes les palettes.",
            "Accessoires : intercalaires (aussi sur la palette), coiffe, cornières anti-chute, film, cerclages, avec leur poids et leur effet sur l'encombrement.",
            "Colisage (caisses) : nombre maximal de produits dans une caisse, création de l'article caisse et palettisation en un clic.",
            "Conditionnements hétérogènes : couches homogènes, piles par article ou densité maximale ; support, charge sur le dessus, fragiles et poids contrôlés.",
            "Vues 3D (couches visibles, survol) et 2D cotées (dessus couche par couche, côté, face), spécification complète.",
            "Export CSV des conditionnements mono-article (produits par palette, palettes physiques, gerbages, palette, charge, encombrement) et fiche de palettisation imprimable / PDF."
        ])
    ];
}
