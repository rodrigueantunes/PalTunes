# PalTunes – Format d'import CSV des articles

Une ligne d'en-tête, puis une ligne par article. Le même format est produit par « Exporter la base » (réimportable)
et par « Modèle CSV » (une ligne d'exemple par type). L'aide intégrée (F1) reprend ce document.

## Règles générales

- Séparateur détecté automatiquement : `;` (recommandé, Excel français), `,`, tabulation ou `|`.
- Encodage UTF-8 (avec ou sans BOM) ; à défaut Windows-1252 (« CSV séparateur point-virgule » d'Excel).
- Décimales `,` ou `.` ; espaces de milliers ignorés.
- En-têtes insensibles à la casse, aux accents et aux espaces (`Diamètre int.` = `DIAMETRE_INT`) ; synonymes acceptés.
- **Mise à jour par code** : un code existant est modifié, seules les colonnes présentes dans le fichier sont touchées.
- Rapport d'import ligne par ligne (créé, mis à jour, erreur avec motif) ; les colonnes inconnues sont signalées et ignorées.

## Colonnes

| Colonne | Synonymes | Exigence | Contenu |
|---|---|---|---|
| `CODE` | CODE_ARTICLE, ARTICLE, REF, REFERENCE, ITEM | **Obligatoire** | Code unique |
| `TYPE` | TYPE_ARTICLE, NATURE, KIND | **Obligatoire** (création) | CAISSE, BOBINE, TUBE, PLAQUE, SAC, FUT, BAC, AUTRE (synonymes : CARTON, COLIS, ROULEAU, PROFILE, PANNEAU, SACHET, BIDON, CAISSE_PLASTIQUE…) |
| `LONGUEUR` | L, LONG, LENGTH | Selon type | mm – caisse, sac, bac, plaque, autre ; longueur du tube |
| `LARGEUR` | LARG, WIDTH, LAIZE | Selon type | mm – caisse, sac, bac, plaque, autre ; **laize** de la bobine |
| `HAUTEUR` | H, HAUT, HEIGHT, EPAISSEUR, EP | Selon type | mm – hauteur (caisse, sac, bac, fût, autre), **épaisseur** (plaque) |
| `DIAMETRE` | DIAM, D, DIAMETRE_EXT, OD | Selon type | mm – diamètre extérieur (bobine, tube, fût) |
| `DIAMETRE_INT` | MANDRIN, DIAM_INT, ID, CORE | Facultatif | mm – mandrin de bobine |
| `POIDS` | POIDS_KG, MASSE, WEIGHT, KG | **Obligatoire** | kg par article |
| `ORIENTATION` | HAUT_IMPOSE, ROTATION | Facultatif | HAUT_IMPOSE (défaut) ou LIBRE – caisses et « autre » |
| `AXE` | AXE_BOBINE, AXE_TUBE, AXIS | Facultatif | VERTICAL, HORIZONTAL, INDIFFERENT – bobines (défaut VERTICAL), tubes (défaut INDIFFERENT : le meilleur est proposé) |
| `CHARGE_MAX` | CHARGE_MAX_DESSUS, GERBABILITE, LOAD_ON_TOP | Facultatif | kg supportables par un exemplaire |
| `COUCHES_MAX` | NB_COUCHES_MAX, MAX_LAYERS | Facultatif | couches superposées maximum |
| `FRAGILE` | – | Facultatif | OUI / NON (rien dessus) |
| `DESIGNATION` | LIBELLE, DESCRIPTION, NOM | Facultatif | Libellé |
| `CLIENT` | NOM_CLIENT, CUSTOMER | Facultatif | 1er niveau de l'arborescence par défaut |
| `FAMILLE` | FAMILY, GROUPE | Facultatif | |
| `SOUS_FAMILLE` | SOUSFAMILLE, SUB_FAMILY | Facultatif | |
| `REF_CLIENT` | REFERENCE_CLIENT, CUSTOMER_REF | Facultatif | |
| `EAN` | GTIN, CODE_BARRE | Facultatif | |
| `COULEUR` | COLOR | Facultatif | #RRGGBB (affichage 3D / 2D) |
| `NOTES` | COMMENTAIRE, REMARQUES | Facultatif | |

## Données minimales par type

| Type | Obligatoire (en plus de CODE, TYPE, POIDS) |
|---|---|
| CAISSE, SAC, BAC, AUTRE | LONGUEUR, LARGEUR, HAUTEUR |
| PLAQUE | LONGUEUR, LARGEUR, HAUTEUR (épaisseur) |
| BOBINE | DIAMETRE, LARGEUR (laize) |
| TUBE | DIAMETRE, LONGUEUR |
| FUT | DIAMETRE, HAUTEUR |

## Exemples

- `samples/articles_minimal.csv` : uniquement les colonnes obligatoires, un article par type.
- `samples/articles_exemple.csv` : toutes les colonnes, clients et familles (arborescence complète).

## Import CSV des clients

Mêmes règles (séparateur détecté, mise à jour par **CODE**). Les articles se rattachent au client par son **NOM**
(colonne `CLIENT` des articles) ; un renommage par import est répercuté sur les articles.

| Colonne | Synonymes | Exigence | Contenu |
|---|---|---|---|
| `CODE` | CODE_CLIENT, CLIENT_CODE, REF | Obligatoire (déduit du nom si absent) | Code unique |
| `NOM` | CLIENT, RAISON_SOCIALE, NAME, LIBELLE | Obligatoire | Nom affiché |
| `ADRESSE`, `CODE_POSTAL`, `VILLE`, `PAYS` | ADDRESS, CP, CITY, COUNTRY | Facultatif | Coordonnées |
| `CONTACT`, `TELEPHONE`, `EMAIL` | TEL, PHONE, MAIL | Facultatif | |
| `PALETTE` | PALETTE_DEFAUT, PALLET | Facultatif | Code de la palette imposée (EUR1, CP3, PLA-EUR3…) |
| `HAUTEUR_MAX` | HAUTEUR_MAXI, MAX_HEIGHT | Facultatif | Hauteur totale maxi (mm, palette comprise) |
| `GERBAGE_MAX` | NIVEAUX_GERBAGE, MAX_STACK | Facultatif | Niveaux de gerbage acceptés |
| `NOTES` | COMMENTAIRE | Facultatif | |

Le poids unitaire des articles (`POIDS`) accepte jusqu'à 5 décimales (`0,00001`).
