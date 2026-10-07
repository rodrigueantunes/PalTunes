# PalTunes – Format d'import CSV des articles et des clients

> À jour de PalTunes 0.1.5. Une ligne d'en-tête, puis une ligne par article (ou par client). Le même format est produit
> par « Exporter la base » (réimportable) et par « Modèle CSV » (une ligne d'exemple par type d'article). L'aide
> intégrée (F1) présente les mêmes colonnes et les données minimales par type.

## Règles générales

- Séparateur détecté automatiquement : `;` (recommandé, Excel français), `,`, tabulation ou `|`.
- Encodage UTF-8 (avec ou sans BOM) ; à défaut Windows-1252 (« CSV séparateur point-virgule » d'Excel).
- Décimales `,` ou `.` ; espaces de milliers ignorés. Poids unitaire jusqu'à 5 décimales (`0,00001`).
- En-têtes insensibles à la casse, aux accents et aux espaces (`Diamètre int.` = `DIAMETRE_INT`) ; synonymes acceptés.
- **Mise à jour** : un article existant (même **code pour le même client**) ou un client existant (même **code**) est
  mis à jour ; seules les colonnes présentes dans le fichier sont modifiées (voir « Mise à jour par l'import »).
- Rapport d'import ligne par ligne : créé, mis à jour, erreur avec motif, avertissement ; les colonnes inconnues sont
  signalées et ignorées.

## Colonnes des articles

| Colonne | Synonymes | Exigence | Contenu |
|---|---|---|---|
| `CODE` | CODE_ARTICLE, ARTICLE, REF, REFERENCE, ITEM | **Obligatoire** | Code de l'article, unique pour un même client (le même code peut exister chez deux clients) |
| `TYPE` | TYPE_ARTICLE, NATURE, KIND | **Obligatoire** (création) | CAISSE, BOBINE, TUBE, PLAQUE, SAC, FUT, BAC, AUTRE, et les autres types BIDON, SEAU, BOUTEILLE, CUVE (synonymes : CARTON, COLIS, ROULEAU, PROFILE, BARRE, PANNEAU, PLANCHE, SACHET, CAISSE_PLASTIQUE, TONNEAU, BARIL, JERRICAN, POT, FLACON, IBC, GRV…). Depuis 0.1.5, `BIDON` désigne un bidon / jerrican (il désignait un fût) |
| `LONGUEUR` | L, LONG, LENGTH | Selon type | mm – caisse, sac, bac, plaque, autre ; longueur du tube |
| `LARGEUR` | LARG, WIDTH, LAIZE | Selon type | mm – caisse, sac, bac, plaque, autre ; **laize** de la bobine |
| `HAUTEUR` | H, HAUT, HEIGHT, EPAISSEUR, EP | Selon type | mm – hauteur (caisse, sac, bac, fût, autre), **épaisseur** (plaque) ; tube : **épaisseur de paroi** (Ø intérieur = Ø − 2 × épaisseur, si `DIAMETRE_INT` est vide) |
| `LONGUEUR_PLIEE` | LONGUEUR_PLIE, L_PLIEE, LONGUEUR_A_PLAT, FOLDED_LENGTH | Facultatif | mm – carton livré plié : renseignée (> 0), elle remplace la longueur pour le conditionnement |
| `LARGEUR_PLIEE` | LARGEUR_PLIE, LARG_PLIEE, LARGEUR_A_PLAT, FOLDED_WIDTH | Facultatif | mm – carton plié : renseignée (> 0), elle remplace la largeur pour le conditionnement |
| `HAUTEUR_PLIEE` | HAUTEUR_PLIE, H_PLIEE, HAUTEUR_A_PLAT, EPAISSEUR_PLIEE, FOLDED_HEIGHT | Facultatif | mm – carton plié : renseignée (> 0), elle remplace la hauteur pour le conditionnement |
| `QTE_PAR_CAISSE` | QUANTITE_PAR_CAISSE, QTE_CAISSE, QUANTITE_CAISSE, PCB, UNITES_PAR_CAISSE, QTY_PER_CASE | Facultatif | Caisse / carton : nombre entier de produits contenus. Renseignée, la fiche palette indique aussi les produits contenus par palette ; vide, rien ne change. Cellule vide : quantité effacée ; colonne absente : quantité conservée |
| `DIAMETRE` | DIAM, D, DIAMETRE_EXT, DIAMETER, OD | Selon type | mm – diamètre extérieur (bobine, tube, fût). Absent pour un tube ou une bobine : le logiciel prend `DIAMETRE_INT` comme diamètre (article considéré plein), signalé « Avertissement » ; les données sont gardées telles quelles |
| `DIAMETRE_INT` | MANDRIN, DIAM_INT, ID, CORE | Facultatif | mm – mandrin de bobine ; diamètre intérieur d'un tube creux (bague, mandrin). Avec `DIAMETRE`, le creux est dessiné sur les schémas ; seul, il sert de diamètre |
| `POIDS` | POIDS_KG, MASSE, WEIGHT, KG | **Obligatoire** | kg pour **un** article. Un poids impossible pour les dimensions (matière plus dense que 20 kg/dm³) est importé mais signalé « Avertissement » |
| `ORIENTATION` | HAUT_IMPOSE, ROTATION | Facultatif | HAUT_IMPOSE (défaut) ou LIBRE – caisses et « autre » |
| `AXE` | AXE_BOBINE, AXE_TUBE, AXIS | Facultatif | VERTICAL, HORIZONTAL, INDIFFERENT – bobines (défaut VERTICAL), tubes (défaut INDIFFERENT : le meilleur est proposé) |
| `CHARGE_MAX` | CHARGE_MAX_DESSUS, GERBABILITE, LOAD_ON_TOP | Facultatif | kg supportables par un exemplaire |
| `COUCHES_MAX` | NB_COUCHES_MAX, MAX_LAYERS | Facultatif | couches superposées maximum |
| `FRAGILE` | – | Facultatif | OUI / NON (rien dessus) |
| `FORME_DESSUS` | DESSUS, FORME_DU_DESSUS, TOP_SHAPE | Facultatif | Bidon, seau, bouteille : DROIT ou ARRONDI (bombé) |
| `ANGLE_DESSUS` | ANGLE, PENTE_DESSUS, TOP_ANGLE | Facultatif | Bidon, seau, bouteille : angle du dessus par rapport à l'horizontale, en degrés (0 = plat ; bombé : angle au bord) |
| `POIGNEE` | ANSE, HANDLE | Facultatif | Bidon, seau, bouteille, fût : ENCASTREE, SAILLANTE, RABATTABLE ou AUCUNE |
| `POIGNEE_FORME` | FORME_POIGNEE, HANDLE_SHAPE | Facultatif | ARRONDIE ou DROITE |
| `POIGNEE_LONGUEUR` | LONGUEUR_POIGNEE, HANDLE_LENGTH | Facultatif | mm, au plus la longueur du produit (diamètre pour un fût, un seau, une bouteille). « 40 % » accepté : % de la longueur du produit |
| `POIGNEE_LARGEUR` | LARGEUR_POIGNEE, HANDLE_WIDTH | Facultatif | mm, au plus la largeur du produit (diamètre) |
| `POIGNEE_HAUTEUR` | HAUTEUR_POIGNEE, HANDLE_HEIGHT | Facultatif | mm, au plus la hauteur du produit : saillie, ou profondeur du puits |
| `POIGNEE_ANGLE_GAUCHE` | ANGLE_POIGNEE_GAUCHE, HANDLE_ANGLE_LEFT | Facultatif | Poignée arrondie : inclinaison du côté gauche par rapport à la verticale (0 à 80°) |
| `POIGNEE_ANGLE_DROIT` | ANGLE_POIGNEE_DROIT, HANDLE_ANGLE_RIGHT | Facultatif | Poignée arrondie : inclinaison du côté droit (0 à 80°) |
| `POIGNEE_PLEINE` | HANDLE_SOLID | Facultatif | OUI / NON : poignée moulée pleine, liée au corps |
| `TASSEMENT` | TASSABLE, COMPRESSION | Facultatif | Sac : OUI (10 %), un pourcentage (0 à 25) ou NON / vide. Tassement à la mise en caisse uniquement |
| `DESIGNATION` | LIBELLE, DESCRIPTION, NOM | Facultatif | Libellé |
| `CLIENT` | CODE_CLIENT, CLIENT_CODE, CUSTOMER | Facultatif | **Code du client** (base clients). Un code inconnu crée le client (nom à compléter) ; un nom de client existant est remplacé par son code. 1er niveau de l'arborescence par défaut |
| `FAMILLE` | FAMILY, GROUPE | Facultatif | |
| `SOUS_FAMILLE` | SOUSFAMILLE, SUB_FAMILY | Facultatif | |
| `REF_CLIENT` | REFERENCE_CLIENT, CUSTOMER_REF | Facultatif | Référence de l'article chez le client |
| `EAN` | GTIN, CODE_BARRE | Facultatif | |
| `COULEUR` | COLOR | Facultatif | #RRGGBB (affichage 3D / 2D) |
| `NOTES` | COMMENTAIRE, REMARQUES | Facultatif | |

## Données minimales par type

| Type | Obligatoire (en plus de CODE, TYPE, POIDS) | Facultatif |
|---|---|---|
| CAISSE | LONGUEUR, LARGEUR, HAUTEUR | LONGUEUR_PLIEE, LARGEUR_PLIEE, HAUTEUR_PLIEE (carton livré plié), QTE_PAR_CAISSE |
| SAC | LONGUEUR, LARGEUR, HAUTEUR | TASSEMENT |
| BAC, AUTRE | LONGUEUR, LARGEUR, HAUTEUR | |
| PLAQUE | LONGUEUR, LARGEUR, HAUTEUR (épaisseur) | |
| BOBINE | DIAMETRE (à défaut DIAMETRE_INT, avec avertissement), LARGEUR (laize) | DIAMETRE_INT (mandrin) |
| TUBE | DIAMETRE (à défaut DIAMETRE_INT, avec avertissement), LONGUEUR | DIAMETRE_INT, ou HAUTEUR = épaisseur de paroi (tube creux) |
| FUT | DIAMETRE, HAUTEUR | POIGNEE, POIGNEE_FORME, POIGNEE_LONGUEUR, POIGNEE_LARGEUR, POIGNEE_HAUTEUR, FORME_DESSUS, ANGLE_DESSUS |
| BIDON (jerrican) | LONGUEUR, LARGEUR, HAUTEUR (hors tout : poignée, bouchon) | FORME_DESSUS, ANGLE_DESSUS, POIGNEE |
| CUVE (IBC / GRV) | LONGUEUR, LARGEUR, HAUTEUR (hors tout, palette intégrée) | |
| SEAU, BOUTEILLE | DIAMETRE (le plus grand), HAUTEUR | FORME_DESSUS, ANGLE_DESSUS, POIGNEE |

Forme du dessus (bidon, seau, bouteille) : renseignée, elle décide du gerbage — direct, sur intercalaire sous chaque
couche, ou pas du tout — d'après le glissement (un produit glisse sur une pente de θ dès que tan θ dépasse le
frottement : 14° plastique sur plastique, 21,8° sur carton), l'appui réel du dessus et la poignée (une poignée
saillante interdit la pose directe). Vide : comportement d'avant (gerbage direct, intercalaire conseillé).

Carton plié : chaque dimension pliée renseignée remplace **uniquement** la dimension montée correspondante pour la
palettisation et le colisage ; une dimension pliée vide (ou 0) garde la dimension montée.

## Mise à jour par l'import

- **Articles** : un article est identifié par son **code pour son client**. Même code chez le même client : l'article
  est mis à jour ; même code chez un autre client : un nouvel article est créé. Sans colonne `CLIENT`, le code seul
  suffit s'il est unique dans la base (sinon la ligne est refusée : ajoutez la colonne `CLIENT`).
- **Clients** : un client est identifié par son **code** ; un code existant est mis à jour. Deux clients peuvent porter
  le même nom.
- Seules les colonnes présentes dans le fichier sont modifiées.

## Exemples

- `samples/articles_minimal.csv` : uniquement les colonnes obligatoires, un article par type.
- `samples/articles_exemple.csv` : toutes les colonnes, codes clients et familles (arborescence complète).
- `samples/clients_exemple.csv` : les clients cités par l'exemple (à importer d'abord pour avoir leurs noms).

## Import CSV des clients

Mêmes règles (séparateur détecté, mise à jour par **CODE**). Les articles se rattachent au client par son **CODE**
(colonne `CLIENT` des articles) : un changement de nom ne touche pas les articles, un changement de code depuis la
fiche client leur est répercuté. Quand seul le client est affiché (arborescence, listes, fiche, export), il apparaît
sous la forme « CODE - Nom ».

| Colonne | Synonymes | Exigence | Contenu |
|---|---|---|---|
| `CODE` | CODE_CLIENT, CLIENT_CODE, REF | Obligatoire (déduit du nom si absent) | Code du client, unique ; un code existant est mis à jour |
| `NOM` | CLIENT, RAISON_SOCIALE, NAME, LIBELLE | Obligatoire | Nom affiché (deux clients peuvent porter le même nom) |
| `ADRESSE`, `CODE_POSTAL`, `VILLE`, `PAYS` | ADDRESS, RUE, CP, ZIP, CITY, COUNTRY | Facultatif | Coordonnées |
| `CONTACT`, `TELEPHONE`, `EMAIL` | TEL, PHONE, MAIL, COURRIEL | Facultatif | |
| `PALETTE` | PALETTE_DEFAUT, PALLET | Facultatif | Code de la palette imposée (EUR1, CP3, PLA-EUR3…) |
| `HAUTEUR_MAX` | HAUTEUR_MAXI, MAX_HEIGHT | Facultatif | Hauteur totale maxi (mm, palette comprise) |
| `GERBAGE_MAX` | GERBAGES, NB_GERBAGES, MAX_STACK | Facultatif | Nombre de gerbages acceptés : 0 = non gerbable, 1 = un conditionnement gerbé dessus, etc. |
| `NOTES` | COMMENTAIRE, REMARQUES | Facultatif | |

Sans colonne `CODE`, le code est déduit du nom : deux lignes au même nom désignent alors le même client.
