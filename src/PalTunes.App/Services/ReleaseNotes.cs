namespace PalTunes.App.Services;

public sealed record ReleaseNote(string Version, string Title, IReadOnlyList<string> Items);

/// <summary>Notes de version affichées une fois à chaque nouvelle version (et sur clic de la pastille de version).</summary>
public static class ReleaseNotes
{
    public static IReadOnlyList<ReleaseNote> All { get; } =
    [
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
