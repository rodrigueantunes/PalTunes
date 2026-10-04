# PalTunes – Format d'import CSV des articles et des clients

> À jour de PalTunes 0.0.8. Une ligne d'en-tête, puis une ligne par article (ou par client). Le même format est produit
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
| `TYPE` | TYPE_ARTICLE, NATURE, KIND | **Obligatoire** (création) | CAISSE, BOBINE, TUBE, PLAQUE, SAC, FUT, BAC, AUTRE (synonymes : CARTON, COLIS, ROULEAU, PROFILE, BARRE, PANNEAU, PLANCHE, SACHET, BIDON, CAISSE_PLASTIQUE…) |
| `LONGUEUR` | L, LONG, LENGTH | Selon type | mm – caisse, sac, bac, plaque, autre ; longueur du tube |
| `LARGEUR` | LARG, WIDTH, LAIZE | Selon type | mm – caisse, sac, bac, plaque, autre ; **laize** de la bobine |
| `HAUTEUR` | H, HAUT, HEIGHT, EPAISSEUR, EP | Selon type | mm – hauteur (caisse, sac, bac, fût, autre), **épaisseur** (plaque) ; tube : **épaisseur de paroi** (Ø intérieur = Ø − 2 × épaisseur, si `DIAMETRE_INT` est vide) |
| `LONGUEUR_PLIEE` | LONGUEUR_PLIE, L_PLIEE, LONGUEUR_A_PLAT, FOLDED_LENGTH | Facultatif | mm – carton livré plié : renseignée (> 0), elle remplace la longueur pour le conditionnement |
| `LARGEUR_PLIEE` | LARGEUR_PLIE, LARG_PLIEE, LARGEUR_A_PLAT, FOLDED_WIDTH | Facultatif | mm – carton plié : renseignée (> 0), elle remplace la largeur pour le conditionnement |
| `HAUTEUR_PLIEE` | HAUTEUR_PLIE, H_PLIEE, HAUTEUR_A_PLAT, EPAISSEUR_PLIEE, FOLDED_HEIGHT | Facultatif | mm – carton plié : renseignée (> 0), elle remplace la hauteur pour le conditionnement |
| `DIAMETRE` | DIAM, D, DIAMETRE_EXT, DIAMETER, OD | Selon type | mm – diamètre extérieur (bobine, tube, fût) |
| `DIAMETRE_INT` | MANDRIN, DIAM_INT, ID, CORE | Facultatif | mm – mandrin de bobine ; diamètre intérieur d'un tube creux (bague, mandrin) |
| `POIDS` | POIDS_KG, MASSE, WEIGHT, KG | **Obligatoire** | kg pour **un** article. Un poids impossible pour les dimensions (matière plus dense que 20 kg/dm³) est importé mais signalé « Avertissement » |
| `ORIENTATION` | HAUT_IMPOSE, ROTATION | Facultatif | HAUT_IMPOSE (défaut) ou LIBRE – caisses et « autre » |
| `AXE` | AXE_BOBINE, AXE_TUBE, AXIS | Facultatif | VERTICAL, HORIZONTAL, INDIFFERENT – bobines (défaut VERTICAL), tubes (défaut INDIFFERENT : le meilleur est proposé) |
| `CHARGE_MAX` | CHARGE_MAX_DESSUS, GERBABILITE, LOAD_ON_TOP | Facultatif | kg supportables par un exemplaire |
| `COUCHES_MAX` | NB_COUCHES_MAX, MAX_LAYERS | Facultatif | couches superposées maximum |
| `FRAGILE` | – | Facultatif | OUI / NON (rien dessus) |
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
| CAISSE | LONGUEUR, LARGEUR, HAUTEUR | LONGUEUR_PLIEE, LARGEUR_PLIEE, HAUTEUR_PLIEE (carton livré plié) |
| SAC, BAC, AUTRE | LONGUEUR, LARGEUR, HAUTEUR | |
| PLAQUE | LONGUEUR, LARGEUR, HAUTEUR (épaisseur) | |
| BOBINE | DIAMETRE, LARGEUR (laize) | DIAMETRE_INT (mandrin) |
| TUBE | DIAMETRE, LONGUEUR | DIAMETRE_INT, ou HAUTEUR = épaisseur de paroi (tube creux) |
| FUT | DIAMETRE, HAUTEUR | |

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
