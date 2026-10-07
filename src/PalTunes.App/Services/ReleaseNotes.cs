namespace PalTunes.App.Services;

public sealed record ReleaseNote(string Version, string Title, IReadOnlyList<string> Items);

/// <summary>Notes de version affichées une fois à chaque nouvelle version (et sur clic de la pastille de version).</summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
        new("0.2.4", "Animations soignées : la palette se construit sous vos yeux",
        [
            "Construction de la palette : à chaque nouvelle solution, la palette apparaît, puis les couches sont déposées une à une du bas vers le haut (intercalaires avec leur couche), la coiffe et les cornières sont posées, et le film et le cerclage se déroulent du bas vers le haut. Bouton « Construction » pour la rejouer, et à côté le choix de la vitesse : Lente, Normale (≈ 2 s), Rapide, Très rapide — mémorisé ; la changer rejoue aussitôt la construction.",
            "Colisage : la caisse s'ouvre (rabats qui se relèvent l'un après l'autre) puis les produits sont déposés couche par couche ; « Ouvrir la caisse » anime l'ouverture des rabats, « Fermer la caisse » rabat les petits puis les grands rabats sur les produits, les parois deviennent opaques et le ruban adhésif se déroule (patte, bande, patte) jusqu'à l'image exacte de la caisse fermée — aucun saut ; l'ouverture fait l'inverse (ruban retiré, rabats relevés), caméra recadrée en douceur ; bouton « Mise en caisse » pour rejouer, avec le même choix de vitesse.",
            "Caméra : les vues 3/4, Dessus, Côté, Face et Ajuster passent de l'une à l'autre par une rotation fluide autour de la palette, au lieu d'un saut.",
            "Interface : écrans qui glissent légèrement à l'apparition, fenêtres (recherche, raccourcis, nouveautés, aide, rapport) qui s'ouvrent en fondu avec un léger grossissement, cartes de liste qui montent en fondu, contenu des onglets en fondu, boutons qui s'enfoncent à l'appui, notification qui glisse.",
            "Sans ralentir : seules des transformations et des opacités sont animées (rendu par la carte graphique), une seule transformation par couche quelle que soit la taille de la palette, construction complète en 2,2 s environ à vitesse normale. Un clic ou la molette dans la vue 3D termine aussitôt l'animation ; le curseur des couches et le survol ne la rejouent pas.",
            "Vue 3D plus lisible : la barre de boutons passe à la ligne si la place manque (vitesse, « Couleur d'origine ») et l'aide (clic droit, molette, survol) est affichée en bas sur une pastille lisible, plus derrière les boutons.",
            "Fiche article : « Voir le colisage » et « Ses conditionnements (n) » ne s'affichent que s'il en existe. « Voir le colisage » vaut aussi pour un produit : il retrouve les caisses créées pour lui (seul ou dans une caisse mixte) ; un seul colisage s'ouvre directement, plusieurs s'affichent dans une petite liste à filtrer (↑ ↓ Entrée). La rangée de boutons passe à la ligne si la fenêtre est étroite.",
            "Mode sombre : le texte du contenu des onglets (caisse choisie au colisage, listes, tableaux) restait noir ; il suit désormais la couleur du thème. Tous les écrans et onglets ont été vérifiés.",
            "« Animations : activées / désactivées » en bas de la navigation (mémorisé) ; elles sont aussi coupées si Windows n'affiche pas les animations (Accessibilité › Effets visuels)."
        ]),
        new("0.2.3", "Mode sombre, colonnes à la largeur voulue",
        [
            "Mode sombre : « Mode sombre » en bas de la navigation, ou Ctrl+Maj+D. Fonds anthracite, cartes un ton au-dessus, textes clairs, vues 3D sur fond sombre, listes, grilles, menus et champs adaptés. La bascule est immédiate : même écran, même sélection, saisie en cours conservée. Le choix est mémorisé pour les prochaines ouvertures.",
            "Colonnes redimensionnables : un séparateur à glisser entre chaque colonne de chaque écran (navigation, arborescences, fiches, aperçus, vues 3D et 2D, spécification) pour élargir ce dont on a besoin et réduire le reste. Le curseur change au survol, la largeur choisie est mémorisée par écran ; double-clic sur le séparateur : largeur d'origine.",
            "Fiches imprimées inchangées (toujours sur fond blanc), quel que soit le thème."
        ]),
        new("0.2.2", "Poignées en millimètres, angles au curseur",
        [
            "Poignée : longueur, largeur et hauteur se saisissent en mm. Chacune est limitée à la dimension du produit (longueur, largeur, hauteur ; diamètre pour un fût, un seau, une bouteille) : le maximum s'affiche dans le libellé (« Longueur (mm ≤ 290) ») et une valeur plus grande est refusée.",
            "Poignée arrondie : angle au bord de chaque côté réglé au curseur (0 à 80°), comme l'angle au bord du dessus, avec la valeur exacte modifiable à côté.",
            "Poignées saisies en % en 0.2.1 : converties en mm à l'ouverture de la base (copie de sauvegarde avant). Import CSV : POIGNEE_LONGUEUR, POIGNEE_LARGEUR, POIGNEE_HAUTEUR en mm ; « 40 % » reste accepté et converti."
        ]),
        new("0.2.1", "Sacs tassables en caisse, poignées dimensionnées",
        [
            "Sacs : nouvelle case « Tassable en caisse » (décochée par défaut). Cochée, on peut appuyer pour en mettre plus en caisse : l'épaisseur du sac diminue du taux saisi (10 % par défaut, 25 % au plus), l'empreinte ne change pas — c'est l'air qui est chassé (entre les plis d'une liasse de sacs vides, entre les grains d'une poudre). Exemple : sacs de 130 mm dans 500 mm de hauteur intérieure, 3 → 4 par caisse.",
            "Le tassement ne vaut qu'à la mise en caisse (colisage d'un ou de plusieurs articles) : sur palette, l'épaisseur saisie est conservée. Un avertissement rappelle de fermer la caisse en appuyant ; les détails du calcul expliquent pourquoi c'est possible, la limite (au-delà, sac plein éclaté ou caisse bombée ; sacs pleins : rester sous 15 %) et le gain de couches.",
            "Poignées (bidon, seau, bouteille et maintenant fût) : on ajoute ou on retire la poignée, on choisit son type (encastrée, saillante, anse rabattable), sa forme (droite ou arrondie) et ses dimensions en % du produit — longueur, largeur, hauteur, 100 % au plus chacune. L'aperçu 3D suit ces dimensions.",
            "Prise en compte en palettisation et en colisage : poignée encastrée — le puits (longueur × largeur) est retiré de la surface d'appui du dessus ; poignée saillante — avec un intercalaire, l'appui se fait sur le dessus des poignées (arrondie : sur une ligne) ; sous 5 % de contact, appui quasi ponctuel et charge admissible × 0,3 au lieu de × 0,5. Dimensions non saisies : calcul identique à la 0.2.0.",
            "Poignée arrondie : angle de chaque côté (inclinaison par rapport à la verticale, 0 à 80°) ; le dessus de la poignée raccourcit d'autant, ce qui réduit l'appui sur un intercalaire. L'aperçu 3D dessine une anse continue, côtés inclinés et coins arrondis.",
            "Case « Poignée pleine » (décochée par défaut) : poignée moulée d'un seul tenant avec le corps, dessinée pleine et liée au bidon ou au fût dans l'aperçu et les vues 3D. Le calcul retient le même appui qu'une poignée ouverte de mêmes dimensions.",
            "Import / export CSV : colonnes facultatives POIGNEE_FORME, POIGNEE_LONGUEUR, POIGNEE_LARGEUR, POIGNEE_HAUTEUR (%), POIGNEE_ANGLE_GAUCHE, POIGNEE_ANGLE_DROIT (°), POIGNEE_PLEINE et TASSEMENT (OUI, un pourcentage, ou NON)."
        ]),
        new("0.2.0", "Forme des bidons, quantités sans questions, navigation plus rapide",
        [
            "Bidons, seaux, bouteilles : on décrit la forme du dessus — droit ou arrondi (bombé), son angle, la poignée (encastrée, saillante, anse rabattable, aucune). Le calcul en déduit si l'on peut gerber directement, seulement sur un intercalaire sous chaque couche, ou pas du tout, et l'aperçu 3D suit la forme saisie.",
            "Règles fondées sur la physique du gerbage : un produit posé sur une pente glisse dès que tan θ dépasse le frottement (14° plastique sur plastique, 21,8° sur intercalaire carton) ; l'appui réel du dessus diminue avec la pente (dessus bombé : seule la partie haute porte) ; une poignée saillante interdit la pose directe et la charge admissible est réduite quand l'appui est partiel. Le verdict et son raisonnement s'affichent sur la fiche article (« Pourquoi ? »).",
            "Effet dans tous les calculs : nombre de couches limité par la forme (intercalaire obligatoire, non gerbable), charge admissible réduite, palettes gerbées seulement avec coiffe, rien posé sur ces produits dans un mélange ; un message indique quoi ajouter (intercalaire sous chaque couche) pour monter plus haut. Forme non renseignée : rien ne change.",
            "Détails du calcul : nouvelle section « Forme du dessus et gerbage » (frottement, angle de glissement, appui, verdict et ce qu'il change pour ce conditionnement).",
            "Détails du calcul, quantités : chaque contrainte donne son nombre maximal de couches (hauteur, poids, résistance, couches maxi, forme du dessus) avec son opération, et la plus petite est signalée ; nouvelle section « Pourquoi pas plus ? » : ce qu'une couche de plus dépasserait (hauteur de +110 mm, poids de 18 kg > 10 kg…), pourquoi pas un produit de plus par couche, pourquoi la dernière couche est incomplète ; contrôle final du poids et de la hauteur ; en hétérogène, pourquoi pas une palette de moins.",
            "Recherche globale (Ctrl+K ou « Rechercher… » en haut de la navigation) : articles, colisages, conditionnements, clients, palettes, caisses et espaces en une seule saisie, sans se soucier des accents ; flèches et Entrée pour ouvrir le résultat dans son espace ; sans saisie, les éléments récemment modifiés.",
            "Navigation Précédent / Suivant entre les écrans : flèches sous le logo, Alt+← / Alt+→, boutons latéraux de la souris.",
            "Raccourcis clavier réunis dans un panneau (Ctrl+F1 ou « Raccourcis clavier » en bas de la navigation) ; « Copier le détail » sur l'onglet Détails du calcul pour coller l'explication dans un courriel.",
            "Import / export CSV : colonnes facultatives FORME_DESSUS, ANGLE_DESSUS, POIGNEE."
        ]),
        new("0.1.9", "Écrans reliés, quantités expliquées, fiches de colisage multiples",
        [
            "Gestion des conditionnements : « Voir le colisage » sur une palette de caisses créées au colisage ouvre l'espace Colisage réglé sur ce colisage (produit, caisse, quantité par caisse, position) ; palette à plusieurs caisses : « Voir les colisages (n) » propose le choix. La fiche résumée affiche le colisage de chaque caisse ; « Voir l'article » ouvre la fiche de l'article d'une palette homogène.",
            "Écrans reliés entre eux : fiche article → « Mettre en caisse », « Voir le colisage » (article caisse créé au colisage) et « Ses conditionnements » (gestion filtrée sur l'article, palettes de ses caisses comprises) ; Conditionnements → « Voir le colisage » et « Fiche article » sous l'article, icône colisage sur chaque ligne caisse d'une palette hétérogène ; Colisage → « Fiche article » et « Ses conditionnements » sous le produit.",
            "Détails du calcul, nouvelle section « Plan de couche pas à pas » : surface utile et empreinte du produit, grilles simples comparées (tout en long, tout en travers ; produits ronds : rangées alignées et en quinconce avec leurs opérations), plan retenu décomposé en blocs ou en rangées (3 × 2 + 2 × 1…), gain sur la meilleure grille simple, borne par la surface et surface couverte.",
            "Détails du calcul : tout en bas, « Les opérations, en bref » reprend le calcul en opérations simples numérotées (surface, hauteur utile, couches = 1 656 ÷ 250 = 6,62 → 6, produits = 8 × 6 = 48, poids, hauteur ; au colisage, produits par caisse, poids brut, caisses et produits par palette).",
            "Détails du calcul, nouvelle section « Quantité par palette » : produits par couche × nombre de couches (détail de la dernière couche incomplète), produits contenus quand la palette porte des caisses (colis × quantité par caisse) ; en hétérogène, quantité de chaque article sur chaque palette.",
            "Détails du calcul, nouvelle section « Quantité par colisage » : au Colisage, produits par couche × nombre de couches et facteur limitant (ou composition de chaque caisse) ; aux Conditionnements, colisage de chaque caisse de la palette recalculé (caisse, dimensions intérieures, produits par couche × couches), ou quantité par caisse saisie.",
            "Palette hétérogène avec des caisses : le bouton devient « Imprimer les fiches de colisage » dès que plusieurs colisages sont disponibles et imprime la fiche de chaque caisse, chacune sur une nouvelle page.",
            "Fiche de conditionnement d'une palette à plusieurs caisses : fiche palette, puis toutes les fiches de colisage à la suite, en un seul document.",
            "Caisses mixtes (plusieurs articles) : la caisse enregistre son contenu détaillé (articles × quantités, caisse) ; sa fiche de colisage est complète, comme celle d'un article : composition (quantité et poids de chaque article), stratégie et règles de remplissage, caisse, poids brut, puis page de schémas (niveaux, côté, face) et vue 3D de la caisse ouverte avec la légende des couleurs. « Voir le colisage » rouvre le colisage en « Plusieurs articles ».",
            "Caisses mixtes créées avant la 0.1.9 : à l'ouverture de la base, articles × quantités et caisse sont repris de leur fiche (désignation « … mixte : 350 × A + 100 × B », code « CAI-MIX-caisse », notes) ; copie de sauvegarde de la base avant cette conversion.",
            "Fiches imprimées : la première page (en-tête, cartouche, spécification complète, recommandation et avertissements) tient toujours sur une seule page. Si elle déborde, elle se resserre d'elle-même par paliers — images de la palette et de la caisse plus petites, puis texte et marges plus serrés, puis spécification sur deux colonnes — sans retirer aucune information.",
            "Fiche palette d'une palette hétérogène à plusieurs unités : la spécification donne les produits et les couches de l'unité montrée (« unité 1 / 3 · 119 au total »), comme le cartouche et les indicateurs.",
            "Colisage de plusieurs articles : « Imprimer la fiche de colisage » imprime la caisse affichée (fiche mixte complète) ; détails du calcul des palettes de caisses mixtes : contenu de chaque caisse et produits contenus par article."
        ]),
        new("0.1.8", "Fiches mises en page et détails du calcul",
        [
            "Fiches imprimées : première page avec un cartouche (informations clés, image de la palette choisie et de la caisse si elle vient du catalogue) et la spécification ; schémas (couches, côté, face) et vue 3D sur une page à part ; plan de palettisation ensuite ; en-tête souligné et pied de page numéroté « page x / n » sur chaque page.",
            "Nouvel onglet « Détails du calcul » dans les Conditionnements : base et limites (surface et hauteur utiles, charge), produit retenu, plan de couche et borne théorique, couches par la hauteur, le poids et la résistance avec le facteur limitant, poids et encombrement, gerbage ; en hétérogène, bornes de volume et de poids, règles de pose, stratégie retenue et détail par palette.",
            "Nouvel onglet « Détails du calcul » au Colisage : caisse et limites (charge maxi, manutention à la main), plan et couches, poids brut, palettisation des caisses (caisses et produits par palette) ; en colisage de plusieurs articles, bornes, règles et détail par caisse.",
            "Impression selon ce qui existe : fiche palette si un conditionnement existe (affiché, sélectionné ou enregistré), fiche de colisage si un colisage existe (affiché, caisse créée au colisage ou au moins une quantité par caisse), fiche de conditionnement si les deux existent. Rien n'est plus calculé à la place de l'utilisateur ; le survol d'un bouton grisé en donne la raison.",
            "Caisse dont seule la quantité par caisse est connue (importée) : fiche de colisage résumée (quantité, caisse, palettisation enregistrée).",
            "Caisses créées au colisage avant la 0.1.3 : à l'ouverture de la base, quantité par caisse, produit contenu, caisse du catalogue, dimensions intérieures et position sont repris de leur fiche (désignation, code, notes) — leur fiche de colisage et leur fiche de conditionnement deviennent imprimables. Copie de sauvegarde de la base avant cette conversion.",
            "Arborescences (articles, gestion des conditionnements) : plus de défilement horizontal qui coupait le début des libellés."
        ]),
        new("0.1.7", "Moteurs plus performants : plus de produits, moins d'unités",
        [
            "Produits ronds (fûts, bobines et tubes debout, seaux, bouteilles) : nouvelles mailles mixtes — quelques rangées alignées glissées dans une quinconce gagnent une rangée. Jusqu'à 2 produits de plus par couche, par exemple Ø107 sur EUR : 84 → 86 ; Ø250 : 13 → 14 ; Ø170 sur 1200 × 1000 : 39 → 41.",
            "Palettes et caisses hétérogènes : un article qui remplit à lui seul une palette (ou une caisse) ne la monopolise plus quand il y reste de la place pour les autres. Exemple : 12 colis 294 × 238 + 10 colis 171 × 132 en carton 600 × 400 : 7 → 6 caisses.",
            "Palettes et caisses hétérogènes, commandes courantes (jusqu'à 400 produits) : recherche élargie des ordres de pose, souvent une palette ou une caisse de moins. Exemple : 6 + 6 colis de deux formats en carton 600 × 400 : 5 → 3 caisses. Résultat identique à chaque calcul de la même commande.",
            "Les plans de couche des cartons, des produits ronds et des compositions hétérogènes ont été revérifiés sur plusieurs milliers de cas : chaque plan reste dans la palette, sans chevauchement, appuis et charges contrôlés."
        ]),
        new("0.1.6", "Colisage hétérogène et colisage validé",
        [
            "Colisage de plusieurs articles (« Plusieurs articles » dans l'espace Colisage) : articles × quantités dans une même caisse, avec les règles d'une palette multi-articles (appui, charge, lourd sous léger), autant de caisses que nécessaire. Mêmes choix qu'en colisage simple : meilleure caisse du catalogue en fonction de la palette de destination, caisse imposée du catalogue, caisse spécifique.",
            "Meilleure caisse : manutentionnable à la main (25 kg brut) d'abord, puis le moins de palettes, puis le plus petit volume de caisses (meilleur remplissage), puis le moins de caisses. Chaque caisse respecte sa charge maxi et les 25 kg brut.",
            "Résultat : n caisses (sélecteur « Caisse 1 / n » pour les vues 3D et 2D), contenu et poids brut de chacune, caisses par palette et palettes nécessaires ; couleur distincte par article. « Créer l'article caisse » crée l'article de la caisse affichée (caisse mixte : contenu dans la désignation, quantité totale par caisse).",
            "Colisage validé contre des références indépendantes : un cas par type de produit (bornes de Barnes, mailles de cercles, carton de 12 bouteilles…) et sept caisses hétérogènes multi-types (bornes de volume et de poids). Résultats : docs/VALIDATION_MOTEUR.md §6–7.",
            "Correction : une caisse dont une seule couche dépasse la charge maxi (bouteilles lourdes, carton de 30 kg) était déclarée impossible ; la dernière couche est maintenant remplie jusqu'au poids.",
            "Fiche article : l'ascenseur des types passe sous les pastilles (plus de recouvrement) ; « Autre » est rangé en dernier."
        ]),
        new("0.1.5", "Moteur validé, bidons et autres types, conditionnements en cours",
        [
            "Validation du moteur contre des références indépendantes : un cas homogène par type de produit (optimums publiés du pallet loading problem, bornes de surface et de Barnes, mailles de cercles, usages industriels) et huit compositions hétérogènes multi-types (bornes de volume et de poids), chaque solution passant le contrôle indépendant. Résultats : docs/VALIDATION_MOTEUR.md.",
            "Plans de couche des cartons améliorés par la validation : moulinets récursifs (chaque bloc peut lui-même être un moulinet). Sur 7 965 tailles de cartons, 43 plans gagnent une boîte par couche (exemples sur EUR : 271 × 201 : 14 → 15 ; 208 × 165 : 26 → 27), aucun n'est dégradé.",
            "Nouveaux types (catégorie « Autre ») : Bidon / jerrican (poignée plastique), Seau / pot, Bouteille / flacon, Cuve IBC / GRV. Calculés comme leur forme de base (bac rigide ou fût debout) en homogène comme en hétérogène, dessinés avec leur forme réelle (poignée et bouchon, col, rebord, cage et palette intégrée).",
            "Bidons, seaux et bouteilles gerbés sur plusieurs couches sans intercalaire : conseil d'intercalaire (dessus non plat).",
            "Import : TYPE BIDON, JERRICAN, SEAU, POT, BOUTEILLE, FLACON, CUVE, IBC, GRV. Attention : BIDON désigne désormais un bidon / jerrican (il était lu comme un fût) ; les articles déjà importés ne changent pas.",
            "Écran Conditionnements : liste repliable « En cours » au-dessus de « Récents » — le conditionnement en cours de création y apparaît aussitôt ; on passe d'un conditionnement à l'autre sans perdre la saisie non enregistrée (gardée pendant la session), la croix l'abandonne. Les listes s'empilent en haut, sans grand vide."
        ]),
        new("0.1.4", "Fiches liées à l'article, pages bien séparées",
        [
            "Boutons d'impression liés à l'article, quel que soit le menu : actifs si la fiche est possible pour l'article affiché, grisés et non cliquables sinon. Le survol donne la raison (article incomplet, ne tient dans aucune caisse, caisse non palettisable, produit contenu inconnu…).",
            "Article des fiches : article sélectionné (Articles), article du conditionnement (Conditionnements, Gestion des conditionnements), produit du colisage (Colisage) ; dans les autres espaces, le dernier article affiché. Il est rappelé au-dessus des boutons (« Fiches de … »).",
            "Plus besoin de calculer avant d'imprimer : la solution affichée ou enregistrée est reprise, sinon elle est calculée en tâche de fond dès que l'article change (palette de son dernier conditionnement, à défaut la palette de destination du colisage).",
            "Fiche de colisage d'un produit : meilleure caisse du catalogue ; d'un article caisse créé au colisage : son produit dans sa caisse.",
            "Fiche de conditionnement : la fiche palette d'abord, puis la fiche de colisage qui repart en haut d'une nouvelle page (aucune page partagée entre les deux)."
        ]),
        new("0.1.3", "Quantité par caisse et trois fiches d'impression",
        [
            "Quantité par caisse sur l'article caisse / carton (facultative) : renseignée automatiquement quand une caisse est créée au colisage, modifiable sur la fiche article. Non renseignée, rien ne change.",
            "Renseignée, la fiche palette d'une caisse indique la quantité par caisse et les produits contenus par palette (caisses × quantité) ; l'export des conditionnements aussi (deux colonnes en fin de ligne).",
            "Import / export CSV : colonne facultative QTE_PAR_CAISSE (synonymes PCB, QTE_CAISSE…), entier > 0 ; cellule vide = quantité effacée, colonne absente = conservée.",
            "« Imprimer la fiche » devient « Imprimer la fiche palette » (Ctrl+P), même fonctionnement.",
            "Nouvelle « Imprimer la fiche de colisage » : produit, caisse (référence, dimensions intérieures et extérieures, paroi, matière), quantité par caisse, couches, poids brut, palettisation des caisses, plans de couche, côté, face et vue 3D de la caisse ouverte. Aussi depuis l'espace Colisage (bouton sous le calcul).",
            "Nouvelle « Imprimer la fiche de conditionnement » : fiche de colisage puis fiche palette, en un seul document.",
            "Les boutons d'impression sont grisés et non cliquables quand la fiche n'est pas possible pour l'article (survol : explication). Espace Colisage : le colisage affiché, la fiche palette montrant les caisses sur la palette de destination. Ailleurs : le conditionnement affiché ; la fiche de colisage demande un article caisse créé au colisage.",
            "Une caisse créée au colisage retient son produit et sa caisse : sa fiche article l'indique (« Créée au colisage : … »)."
        ]),
        new("0.1.2", "Vues 3D sans déformation",
        [
            "Zoom avant limité : la caméra s'arrête avant d'entrer dans le volume de la palette. Plus de perspective déformée (palette en pointe, produits étirés) ni de produits coupés en zoomant de près ; le zoom arrière est limité aussi pour ne pas perdre la palette.",
            "Le zoom se fait vers le point sous la souris : on s'approche directement du détail visé.",
            "Champ de vision fixe (30°) : il ne peut plus être modifié par erreur au clavier et à la souris.",
            "Plans de découpe recalculés à chaque mouvement : rien n'est coupé, profondeur plus précise (moins de scintillement entre faces voisines).",
            "Visuel : ombre douce au sol sous la palette, la caisse, l'article ou la palette du catalogue ; fond en dégradé clair commun à toutes les vues 3D, fiche imprimée comprise pour l'ombre."
        ]),
        new("0.1.1","Gestion des conditionnements, couleurs distinctes, nouveau thème",
        [
            "Nouvel espace « Gestion des conditionnements » (Ctrl+7) : tous les conditionnements rangés comme les articles (client › famille › sous-famille, famille › type, type › client ou liste), recherche, fiche résumée (client, contenu, palette, solution enregistrée, dates) et aperçu 3D ; Ouvrir (ou double-clic), Dupliquer, Supprimer.",
            "Écran Conditionnements allégé : seuls les 10 derniers conditionnements modifiés sont listés (« Récents », repliable par la flèche, ouvert par défaut) ; une recherche porte sur tous ; « Tout voir » ouvre l'espace de gestion.",
            "Couleurs : chaque article d'un conditionnement reçoit une couleur bien distincte des autres (vues 3D, 2D, légende, pastille de chaque ligne). La case « Couleur d'origine » (décochée par défaut, mémorisée) reprend les couleurs des fiches articles.",
            "Hétérogène : des produits posés un à un pouvaient ne pas trouver place sur le dessus de tubes debout (palettes à moitié remplies, sans explication). Les centres des dessus libres sont maintenant proposés : exemple 1 000 + 3 000 bagues et 100 tubes → 2 palettes conformes au lieu de 3 à 49 %.",
            "Hétérogène : les trois stratégies sont calculées jusqu'à 20 000 produits à mélanger (3 000 auparavant) ; « Piles par article » donne souvent nettement moins de palettes quand les références sont nombreuses.",
            "Profil de gerbage d'un tube « position indifférente » : le profil debout est affiché, avec la règle couchée (« couché : identiques seulement »).",
            "Thème visuel retravaillé : bleu plus soutenu et plus lisible, fond plus clair, cartes blanches à liseré et ombre douce, navigation en dégradé avec barre d'accent sur l'espace actif, onglets actifs soulignés, tuiles et cartes sélectionnées en bleu clair (sans décalage), champs avec survol et focus marqués, lignes de grille sélectionnées plus nettes."
        ]),
        new("0.1.0","Hétérogène rapide, grandes quantités, articles exclus",
        [
            "Palettisation hétérogène fortement accélérée : 600 bagues mélangées passent de 188 s à 0,3 s, 2 500 petits cartons de 2,9 s à 0,5 s. La charge reçue par chaque produit est propagée en une seule passe, par colonnes de produits alignés, au lieu de suivre chaque chemin jusqu'à la palette.",
            "Grandes quantités (100 000 bagues et plus) : les articles qui remplissent une palette donnent des palettes complètes mono-article, le reste est posé par couches entières puis en reliquat sur le dessus. Exemple : 100 000 bagues + 2 300 autres produits → 20 palettes en 3 secondes.",
            "Hétérogène : un article qui ne tient pas seul sur la palette (dimensions dans toutes les orientations, hauteur utile, poids) est exclu ; la meilleure solution est calculée avec les autres articles et l'exclusion est signalée (article × quantité : raison).",
            "Au-delà de 3 000 produits à mélanger, seule la stratégie « Couches homogènes » est calculée (les deux autres posent les produits un à un) ; un message l'indique.",
            "Tubes debout posés sur des tubes : surface d'appui réelle (intersection des disques) au lieu des carrés englobants ; empilages plus justes, souvent moins de palettes. Même règle pour le contrôle indépendant et l'indicateur de support.",
            "Couches de tubes et bobines couchés dans la stratégie « Couches homogènes » ; la disposition debout ou couchée la plus dense est retenue.",
            "Plans de couche de très petits produits (bagues de quelques mm) calculés instantanément (deux blocs au lieu de la recherche exacte, à une rangée près de l'optimum).",
            "Colisage calculé en tâche de fond (« Calcul en cours… ») : la fenêtre reste utilisable ; les caisses possibles sont déterminées sans calcul complet.",
            "Base : solutions enregistrées environ 7 fois plus compactes (une ligne par produit, valeurs calculées non écrites) ; les anciennes bases restent lues.",
            "Fiche imprimée : une couche de plus de 150 produits est décrite par rangées au lieu d'une ligne par produit ; ordre de pose et recherche d'articles accélérés ; grandes quantités affichées avec séparateur de milliers."
        ]),
        new("0.0.9", "Diamètre des tubes et tubes creux sur les schémas",
        [
            "Tube ou bobine sans diamètre extérieur mais avec un diamètre intérieur : le logiciel prend le diamètre intérieur comme diamètre (article considéré plein), partout — palettisation, colisage, profil de gerbage, aperçu 3D. Les données restent telles que saisies ou importées (aucune conversion) ; un tube peut avoir l'un, l'autre ou les deux.",
            "Ce cas est signalé en avertissement : sur la fiche article, au colisage, au conditionnement et dans le rapport d'import (par ligne et total). La fiche est enregistrable ; les listes affichent « Ø50 (Ø int.) × 1000 ».",
            "Tubes et bobines creux (diamètre extérieur et intérieur) : le creux est dessiné sur les schémas — en 3D (paroi intérieure, extrémités en couronne), en 2D (cercle intérieur vu en bout, alésage en traits interrompus vu de côté) et dans l'aperçu 3D de la fiche."
        ]),
        new("0.0.8", "Cartons pliés et import avec mise à jour",
        [
            "Cartons livrés pliés : longueur, hauteur et largeur pliées sur la fiche article (caisse / carton). Chacune, renseignée (> 0), remplace la dimension montée pour le conditionnement — palettisation homogène et hétérogène, assistant « Proposer », colisage ; les autres restent celles du carton monté.",
            "La spécification indique les dimensions prises en compte (« carton plié, monté : … ») ; les listes affichent « 400 × 300 × 250 (plié 700 × 550 × 5) ».",
            "Import CSV : colonnes LONGUEUR_PLIEE, LARGEUR_PLIEE, HAUTEUR_PLIEE (export et modèle compris).",
            "Import des articles avec mise à jour : un article est identifié par son code pour son client — même code chez le même client : mis à jour ; chez un autre client : nouvel article (le même code peut exister chez deux clients). Sans colonne CLIENT, le code seul suffit s'il est unique.",
            "Import des clients : un code existant est mis à jour ; deux clients peuvent porter le même nom (plus de refus).",
            "Fiche article : le code doit être unique pour un même client (et non plus dans toute la base)."
        ]),
        new("0.0.7", "Colisage des tubes",
        [
            "Tubes, bagues et bobines en caisse comme en palettisation : axe horizontal (couché) et vertical (debout) calculés, chaque caisse proposée dans les deux positions et la meilleure recommandée ; choix « Axe des tubes dans la caisse » pour imposer un axe.",
            "Correction (0.0.6) : les listes de clients et d'articles prenaient leur premier élément au lieu de la valeur enregistrée (fiche article marquée « Modifié » avec « (Aucun client) », produit du colisage remplacé). Corrigé ; vérifiez le client des articles enregistrés depuis la 0.0.6.",
            "Recherche dans les listes : les lettres tapées rapidement à l'ouverture de la liste sont toutes prises en compte.",
            "Colisage : quand aucune caisse ne convient, la cause est expliquée — trop lourd (poids unitaire au-delà de la charge maxi des caisses où il tient, avec la plage des charges) ou trop grand (plus grande caisse indiquée) — avec un bouton « Corriger la fiche article ».",
            "Poids unitaire suspect signalé (matière plus dense que 20 kg/dm³, impossible : acier 7,8 ; plomb 11,3) : fiche article, colisage, conditionnement et rapport d'import (« Avertissement » par ligne et total). Cas typique : 700 kg saisis pour une bague Ø76 × 50.",
            "Tubes creux (bagues, mandrins) : diamètre intérieur sur la fiche article ; à l'import, la HAUTEUR (ou EPAISSEUR) d'un tube est lue comme l'épaisseur de paroi (Ø intérieur = Ø − 2 × épaisseur) quand DIAMETRE_INT est vide. Les tubes déjà importés sont convertis à l'ouverture de la base.",
            "Solutions en caisse : plus de conseils propres à la palette (cornières, cales, roulement des tubes couchés) — les parois de la caisse tiennent les produits.",
            "Sauvegarde automatique de la base (fichier .sauvegarde-date.json) avant toute conversion à l'ouverture par une nouvelle version."
        ]),
        new("0.0.6", "Codes clients et listes avec recherche",
        [
            "Import des articles : la colonne CLIENT est le code client. Un code inconnu crée le client (nom à compléter dans l'espace Clients) ; un nom de client existant est accepté et remplacé par son code.",
            "Les articles sont rattachés au client par son code : un changement de nom ne touche plus les articles, un changement de code est répercuté. Les bases existantes sont converties automatiquement (nom → code) à l'ouverture.",
            "Quand seul le client est affiché (arborescence des articles, fiche article, listes, export, fiche imprimée), il apparaît « CODE - Nom ». Là où le code et le nom sont déjà visibles (espace Clients), rien ne change.",
            "Fiche article : le client se choisit dans la liste des clients (« CODE - Nom »).",
            "Choix d'un article : la liste Client vient d'abord et limite les articles proposés (conditionnement homogène, lignes hétérogènes, colisage) ; « Tous les clients » par défaut. Un article déjà choisi reste toujours dans la liste.",
            "Listes déroulantes avec recherche : clients, articles, palettes, caisses… Un champ « Rechercher… » en haut de la liste filtre sur tous les mots saisis, sans tenir compte des accents ni des majuscules ; taper une lettre sur la liste fermée l'ouvre et lance la recherche, Entrée choisit le premier résultat, ↓ descend dans la liste. Familles et sous-familles : le texte saisi filtre la liste.",
            "Désignation affichée à côté du code dans les listes d'articles ; exemple de base clients (samples/clients_exemple.csv).",
            "Grandes bases (milliers d'articles et de clients) : démarrage immédiat. Le colisage n'est plus calculé au démarrage mais à l'ouverture de son espace ; clients indexés ; listes d'articles et de clients virtualisées.",
            "Petits produits (bagues, bouchons… des dizaines de milliers par palette) : contrôles de chevauchement et d'appui et indicateurs calculés par grille spatiale (2 min → 3 s) ; vue 3D limitée aux produits visibles de l'extérieur, vues de côté aux rangées de devant."
        ]),
        new("0.0.5", "Espace Caisses, plan de palettisation, cornières des tubes",
        [
            "Correction du plantage au démarrage (hostpolicy.dll, 0xc0000005) : il survenait quand la base était enregistrée sous un nom réservé à l'application (ex. PalTunes.deps.json), qui remplaçait un fichier de démarrage. Ces noms sont désormais refusés, le contenu d'un fichier est vérifié avant de l'ouvrir et une base illisible au démarrage est remplacée par la base par défaut, avec un message.",
            "« Réinitialiser la base… » (Base de données) : sauvegarde automatique de la base actuelle (fichier .sauvegarde-date.json), puis base vierge avec les catalogues de palettes et de caisses par défaut ; tous les espaces sont rechargés.",
            "Nouvel espace « Caisses », à l'image des palettes : catalogue par défaut (cartons modulaires ISO, cartons standard, bacs plastiques, caisses bois), ajout, modification, activation, aperçu 3D. Le colisage y renvoie par « Gérer le catalogue des caisses ».",
            "Impression / PDF : plan de palettisation en pages supplémentaires à la fin, avec une vue 3D, le tableau des couches (plan, nombre de produits, cote, intercalaire dessous) et, pour chaque plan de couche distinct, une vue de dessus numérotée et le tableau des positions (X, Y depuis le coin de la palette, empreinte, sens).",
            "Export CSV : colonnes « Plan par couche » (ex. « C1-6 : plan A, 8 produit(s) ») et « Intercalaires (couches) ».",
            "Tubes en débord avec cornières : les cornières restent au niveau de la palette (aux coins, à fleur des bords) ; les tubes qui les gêneraient sont retirés selon l'épaisseur et l'aile des cornières, ainsi que ceux qui n'auraient plus d'appui. La perte est comptée et indiquée (avertissement, récapitulatif) et les schémas 2D / 3D le montrent.",
            "Case « Tubes en débord : les cornières suivent les tubes » (décochée par défaut) : cochée, les cornières entourent la charge comme avant.",
            "Raccourcis : Ctrl+1 Clients, Ctrl+2 Articles, Ctrl+3 Palettes, Ctrl+4 Caisses, Ctrl+5 Conditionnements, Ctrl+6 Colisage."
        ]),
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
