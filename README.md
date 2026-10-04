<div align="center">

<img src="src/PalTunes.App/Assets/logo.png" width="150" alt="PalTunes">

# PalTunes

### PALLET / PACK / OPTIMIZE

Palettisation, conditionnement et optimisation de chargement.

Caisses · Bobines · Tubes · Plaques · Sacs · Fûts · Bacs · Formats spécifiques

`v0.0.4` · Windows · .NET 10 · WPF

</div>

---

```text
Un produit.
Une contrainte.
Une palette.

PalTunes cherche le plan.
```

PalTunes est un outil de palettisation et de conditionnement conçu pour traiter aussi bien les cas simples que les chargements plus contraints : produits rectangulaires, cylindriques, lourds, fragiles, gerbables ou non, palettes mono-article ou compositions multi-articles.

L'objectif n'est pas simplement de remplir une palette.

Il faut construire une solution exploitable.

---

# 01 / LE PRINCIPE

Une palettisation correcte doit répondre simultanément à plusieurs questions :

```text
                     ┌──────────────────┐
                     │     ARTICLE      │
                     └────────┬─────────┘
                              │
              dimensions / poids / contraintes
                              │
                              ▼
┌─────────────────┐    ┌───────────────┐    ┌─────────────────┐
│     PALETTE     │───▶│    PalTunes   │◀───│   CONTRAINTES   │
└─────────────────┘    └───────┬───────┘    └─────────────────┘
                               │
                               ▼
                     ┌──────────────────┐
                     │     SOLUTIONS    │
                     ├──────────────────┤
                     │ plan de couche   │
                     │ nombre/couche    │
                     │ nombre couches   │
                     │ poids            │
                     │ dimensions       │
                     │ remplissage      │
                     │ gerbage          │
                     │ ordre de pose    │
                     └────────┬─────────┘
                              │
                  ┌───────────┴───────────┐
                  ▼                       ▼
             VUE 2D / 3D            SPÉCIFICATION
```

PalTunes calcule plusieurs configurations possibles, contrôle leur validité puis classe les solutions afin de faire ressortir la plus pertinente.

---

# 02 / CE QUE PALTUNES SAIT TRAITER

| Produit         | Géométrie prise en compte | Exemples d'utilisation      |
| --------------- | ------------------------- | --------------------------- |
| Caisse / carton | parallélépipède           | colis, cartons, caisses     |
| Bobine          | cylindre                  | papier, film, rouleau       |
| Tube            | cylindre long             | tubes, profilés             |
| Plaque          | parallélépipède mince     | panneaux, feuilles, plaques |
| Sac             | volume rectangulaire      | sacs industriels            |
| Fût             | cylindre                  | fûts, bidons                |
| Bac             | parallélépipède           | bacs logistiques            |
| Autre           | dimensions libres         | cas spécifiques             |

Chaque famille conserve ses propres règles.

Une bobine n'est pas traitée comme un carton auquel on aurait simplement ajouté un diamètre.

---

# 03 / HOMOGÈNE

Mode mono-article.

Une référence est répétée sur une ou plusieurs couches d'une même palette.

PalTunes recherche notamment :

```text
surface disponible
      ↓
plans de couche possibles
      ↓
orientation des produits
      ↓
quantité par couche
      ↓
nombre de couches admissible
      ↓
hauteur / poids / contraintes
      ↓
comparaison des solutions
```

Le moteur peut travailler sur plusieurs familles de plans, notamment les organisations régulières et les dispositions combinées adaptées aux dimensions du produit et de la palette.

Pour les produits cylindriques, un moteur spécifique gère les implantations circulaires.

---

# 04 / HÉTÉROGÈNE

Mode multi-articles.

Plusieurs références peuvent partager une même palette.

Le problème devient alors différent : il ne suffit plus de trouver le meilleur plan pour un article, il faut trouver une combinaison compatible entre plusieurs articles.

PalTunes prend notamment en compte :

* les dimensions de chaque référence ;
* les quantités demandées ;
* le poids ;
* la position disponible ;
* la charge supportable ;
* les articles fragiles ;
* les limites de gerbage ;
* l'ordre lourd / léger ;
* les surfaces encore exploitables ;
* les incompatibilités verticales ;
* les zones disponibles au fur et à mesure du chargement.

Le moteur hétérogène utilise une recherche par points extrêmes pour construire progressivement le chargement.

### Exemple fictif

```text
Palette P-01
1200 × 800 mm

┌──────────────────────────────────────────┐
│ REF-C03 │ REF-C03 │      REF-B11        │
│         │         │                     │
├─────────┴─────────┼─────────────────────┤
│      REF-A01      │      REF-A01        │
│                   │                     │
└──────────────────────────────────────────┘

REF-A01     400 × 300 × 220 mm     8,4 kg
REF-B11     600 × 400 × 180 mm    12,1 kg
REF-C03     300 × 200 × 150 mm     3,2 kg
```

Les références de cet exemple sont volontairement fictives.

---

# 05 / GERBAGE

Le gerbage n'est pas traité comme un simple multiplicateur.

PalTunes distingue notamment :

```text
GERBABLE
│
├─ charge maximale supportée
├─ nombre maximum de couches
├─ limites de hauteur
├─ limites de poids
└─ compatibilité avec ce qui est placé au-dessus


NON GERBABLE
│
└─ aucune charge autorisée au-dessus
```

Un article peut également être marqué fragile.

Dans ce cas, le calcul interdit les configurations incompatibles avec cette contrainte.

---

# 06 / ORIENTATION

Selon le produit, plusieurs orientations peuvent être autorisées ou imposées.

### Produits rectangulaires

```text
HAUT_IMPOSE
└─ le produit conserve son orientation verticale

LIBRE
└─ les orientations compatibles peuvent être étudiées
```

### Tubes et bobines

```text
VERTICAL
HORIZONTAL
INDIFFERENT
```

En mode `INDIFFERENT`, PalTunes peut comparer les possibilités et retenir la configuration la plus intéressante.

---

# 07 / PALETTES

PalTunes embarque un catalogue de palettes prêt à être utilisé et entièrement modifiable.

Il comprend notamment différentes familles :

```text
EUR
ISO
CP
palettes plastiques
palettes pleines
formats industriels
```

Pour chaque palette peuvent être définis :

* code ;
* désignation ;
* longueur ;
* largeur ;
* hauteur ;
* poids à vide ;
* charge dynamique ;
* charge statique ;
* représentation ;
* disponibilité pour les calculs.

La construction visuelle des palettes peut également représenter différentes structures : blocs, semelles, patins, pieds ou plateau plein.

Plusieurs palettes physiques peuvent être associées pour construire une base de chargement plus importante.

---

# 08 / DÉBORDS

Toutes les marchandises ne rentrent pas forcément exactement dans les dimensions du support.

PalTunes peut travailler avec des débords lorsqu'ils sont autorisés.

```text
            CHARGE
       ┌───────────────┐
       │               │
   ┌───┴───────────────┴───┐
   │        PALETTE        │
   └───────────────────────┘
     ← débord →   ← débord →
```

La surface réellement disponible est recalculée avant la recherche des plans.

---

# 09 / HAUTEUR ET POIDS

Chaque solution est confrontée aux limites du conditionnement.

Les calculs distinguent notamment :

```text
hauteur produit
+
hauteur des couches
+
éléments éventuels
+
hauteur palette
=
hauteur totale
```

Le poids est également contrôlé par rapport aux capacités du support et aux contraintes définies.

---

# 10 / COLISAGE

PalTunes ne s'arrête pas à la palette.

Le module de colisage permet de rechercher un contenant adapté à un produit ou à un ensemble de produits avant leur palettisation.

Le catalogue comprend différents types de contenants :

* cartons ;
* bacs ;
* caisses ;
* formats standards ;
* dimensions personnalisées.

PalTunes peut :

1. rechercher les caisses compatibles ;
2. comparer les possibilités ;
3. proposer le meilleur contenant ;
4. afficher son contenu ;
5. visualiser le plan intérieur ;
6. créer l'article correspondant au colis ;
7. lancer directement sa palettisation.

### Chaîne possible

```text
ARTICLE
   │
   ▼
COLISAGE
   │
   ▼
CAISSE
   │
   ▼
PALETTISATION
   │
   ▼
PALETTE
```

---

# 11 / LES VUES

Une solution n'a d'intérêt que si elle peut être comprise rapidement.

PalTunes dispose de plusieurs représentations complémentaires.

### 3D

Visualisation spatiale de la palette, du support et des produits.

La scène permet de contrôler visuellement :

* la disposition ;
* les couches ;
* les orientations ;
* les volumes occupés ;
* la cohérence globale de la solution.

### 2D

Trois lectures sont disponibles :

```text
DESSUS        CÔTÉ        FACE
```

Elles permettent de lire précisément le plan sans dépendre de la perspective 3D.

---

# 12 / INDICATEURS

Chaque solution peut être décrite à partir de plusieurs métriques.

```text
┌──────────────────────────────────────┐
│ QUANTITÉ / PALETTE                  │
│ QUANTITÉ / COUCHE                   │
│ NOMBRE DE COUCHES                   │
│ POIDS TOTAL                         │
│ HAUTEUR TOTALE                      │
│ DIMENSIONS DE CHARGE                │
│ DIMENSIONS D'ENCOMBREMENT           │
│ REMPLISSAGE                         │
│ SCHÉMA DE POSE                      │
│ NOMBRE DE PALETTES PHYSIQUES        │
└──────────────────────────────────────┘
```

Ces informations permettent de comparer les configurations sans se limiter à la quantité maximale.

---

# 13 / ORDRE DE POSE

Pour les solutions qui le nécessitent, PalTunes ne conserve pas uniquement la géométrie finale.

Un ordre de placement peut être généré pour rendre la solution exploitable sur le terrain.

```text
01 → positionner
02 → positionner
03 → positionner
...
```

L'objectif est de passer d'un résultat mathématique à une instruction compréhensible.

---

# 14 / IMPORT ARTICLES

Les articles peuvent être importés depuis un fichier CSV.

Formats de séparateur reconnus :

```text
;
,
TAB
|
```

Les décimales peuvent utiliser `,` ou `.`.

Les noms de colonnes sont tolérants à la casse, aux accents et plusieurs synonymes sont reconnus.

### Format minimal

```csv
CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS
ART-001;CAISSE;400;300;250;;12
ART-002;BOBINE;;700;;1000;380
ART-003;TUBE;1200;;;110;4,5
ART-004;PLAQUE;1600;1200;10;;15
ART-005;SAC;600;400;120;;25
ART-006;FUT;;;880;585;220
ART-007;BAC;600;400;300;;8
```

Ces données sont fictives et servent uniquement à illustrer le format attendu.

---

# 15 / FORMAT ARTICLE ÉTENDU

Des informations complémentaires peuvent enrichir les calculs et l'organisation du catalogue.

```text
CODE
TYPE
LONGUEUR
LARGEUR
HAUTEUR
DIAMETRE
DIAMETRE_INT
POIDS
ORIENTATION
AXE
CHARGE_MAX
COUCHES_MAX
FRAGILE
DESIGNATION
CLIENT
FAMILLE
SOUS_FAMILLE
REF_CLIENT
EAN
COULEUR
NOTES
```

Les champs nécessaires dépendent du type de produit.

### Minimum par géométrie

| Type   | Dimensions nécessaires         |
| ------ | ------------------------------ |
| Caisse | longueur × largeur × hauteur   |
| Sac    | longueur × largeur × hauteur   |
| Bac    | longueur × largeur × hauteur   |
| Plaque | longueur × largeur × épaisseur |
| Bobine | diamètre × laize               |
| Tube   | diamètre × longueur            |
| Fût    | diamètre × hauteur             |

Le code, le type et le poids complètent ces informations minimales.

---

# 16 / MISE À JOUR PAR IMPORT

Le code article sert d'identifiant.

Lorsqu'un article importé existe déjà :

```text
CODE EXISTANT
      │
      ├─ colonne présente dans le CSV → mise à jour
      │
      └─ colonne absente              → valeur conservée
```

L'import produit un rapport permettant de distinguer :

* les créations ;
* les mises à jour ;
* les erreurs ;
* les colonnes inconnues.

---

# 17 / ORGANISATION DES ARTICLES

Le catalogue peut être parcouru sous plusieurs angles.

Exemples :

```text
Client
└── Famille
    └── Sous-famille
        └── Article
```

ou

```text
Famille
└── Type
    └── Article
```

ou encore :

```text
Type
└── Client
    └── Article
```

Une recherche permet également d'accéder directement aux références.

---

# 18 / CONTRAINTES PAR CLIENT

Une configuration peut être associée à un client sans modifier les caractéristiques physiques de l'article.

Il est notamment possible de définir :

* une palette imposée ;
* une hauteur maximale ;
* une limite de gerbage ;
* des règles de conditionnement particulières.

### Exemple fictif

```text
CLIENT-001

Palette préférée : PAL-01
Hauteur max      : 1 650 mm
Gerbage max      : 2
```

Aucune donnée réelle de client n'est nécessaire au fonctionnement des exemples du projet.

---

# 19 / EXPORT

PalTunes peut produire une spécification exploitable de la solution calculée.

Pour une palettisation mono-article, l'export CSV peut notamment contenir :

* quantité de produits ;
* quantité par palette ;
* nombre de palettes physiques ;
* gerbage ;
* dimensions de la palette ;
* dimensions de charge ;
* dimensions d'encombrement ;
* nombre de couches ;
* schéma ;
* intercalaires ;
* cornières ;
* poids ;
* taux de remplissage.

Une fiche de palettisation peut également être imprimée ou enregistrée au format PDF via le système d'impression Windows.

---

# 20 / VALIDATION

Trouver une solution et démontrer qu'elle respecte les règles sont deux opérations différentes.

PalTunes possède donc un validateur indépendant du moteur de recherche.

```text
MOTEUR
  │
  └── produit une solution
              │
              ▼
        ┌─────────────┐
        │ VALIDATEUR  │
        └──────┬──────┘
               │
       ┌───────┴────────┐
       ▼                ▼
    VALIDE          REJETÉE
```

Cette séparation permet de contrôler les configurations générées avant de les présenter comme utilisables.

---

# 21 / ARCHITECTURE

Le projet sépare volontairement le moteur métier de l'interface.

```text
PalTunes
│
├── src
│   │
│   ├── PalTunes.Core
│   │   │
│   │   ├── Catalog
│   │   ├── Engine
│   │   ├── Export
│   │   ├── Import
│   │   ├── Models
│   │   ├── Storage
│   │   └── Validation
│   │
│   └── PalTunes.App
│       │
│       ├── Assets
│       ├── Converters
│       ├── Services
│       ├── Themes
│       ├── ViewModels
│       └── Views
│
├── tests
│   └── PalTunes.Tests
│
├── samples
│
└── docs
    ├── ETUDE_PALETTISATION.md
    ├── ETUDE_HETEROGENE.md
    └── FORMAT_IMPORT_ARTICLES.md
```

`PalTunes.Core` ne dépend pas de l'interface graphique.

Les moteurs de calcul peuvent donc évoluer indépendamment de WPF.

---

# 22 / MOTEURS

Le cœur de PalTunes regroupe plusieurs composants spécialisés.

```text
RectLayerSolver
│
├─ produits rectangulaires
├─ recherche de plans
└─ optimisation des couches


CircleLayerSolver
│
├─ produits cylindriques
└─ placement circulaire


HomogeneousEngine
│
└─ palettisation mono-article


HeterogeneousEngine
│
└─ palettisation multi-articles


CaseEngine
│
└─ colisage


MetricsCalculator
│
└─ métriques des solutions


PackagingCalculator
│
└─ calcul global du conditionnement


SolutionValidator
│
└─ contrôle indépendant
```

---

# 23 / TECHNOLOGIES

```text
Language      C#
Runtime       .NET 10
Desktop       WPF
Architecture  MVVM
MVVM Toolkit  CommunityToolkit.Mvvm
3D            HelixToolkit.Wpf
Tests         xUnit
Storage       JSON
```

La base PalTunes est stockée localement dans un fichier JSON.

L'écriture est réalisée de manière atomique afin de limiter les risques de corruption lors d'une sauvegarde.

---

# 24 / INSTALLATION

Les versions prêtes à utiliser, les fichiers nécessaires et les indications associées à chaque version sont disponibles directement dans les Releases GitHub :

https://github.com/rodrigueantunes/PalTunes/releases

---

# 25 / UTILISATION

Le point d'entrée pour utiliser PalTunes est également la page Releases :

https://github.com/rodrigueantunes/PalTunes/releases

Choisir la version souhaitée puis suivre les indications fournies avec la release correspondante.

---

# 26 / DOCUMENTATION TECHNIQUE

Le dépôt contient également les études utilisées comme référence pour le moteur de calcul.

### Étude générale

[`docs/ETUDE_PALETTISATION.md`](docs/ETUDE_PALETTISATION.md)

Elle couvre notamment :

* vocabulaire métier ;
* données minimales ;
* géométrie ;
* palettisation homogène ;
* plans de couche ;
* produits cylindriques ;
* plaques ;
* débords ;
* palettes multiples ;
* poids ;
* hauteur ;
* stabilité ;
* gerbage ;
* métriques.

### Chargements hétérogènes

[`docs/ETUDE_HETEROGENE.md`](docs/ETUDE_HETEROGENE.md)

Référence spécifique aux palettes multi-articles.

### Import

[`docs/FORMAT_IMPORT_ARTICLES.md`](docs/FORMAT_IMPORT_ARTICLES.md)

Description complète du format d'import CSV.

---

# 27 / RACCOURCIS

```text
F5          Calculer
Ctrl + S    Enregistrer
Ctrl + P    Imprimer
Ctrl + E    Exporter
Ctrl + I    Importer
F1          Aide import
Ctrl + 1    Clients
Ctrl + 2    Articles
Ctrl + 3    Palettes
Ctrl + 4    Conditionnements
Ctrl + 5    Colisage
Échap       Fermer
```

---

# 28 / PALTUNES EN UNE LIGNE

```text
dimensions + contraintes + support
                ↓
             PalTunes
                ↓
 plan calculé + contrôlé + visualisable
```

---

<div align="center">

### PalTunes

`PALLET / PACK / OPTIMIZE`

Concevoir le chargement avant de charger.

[Releases](https://github.com/rodrigueantunes/PalTunes/releases) ·
[Étude de palettisation](docs/ETUDE_PALETTISATION.md) ·
[Chargements hétérogènes](docs/ETUDE_HETEROGENE.md) ·
[Format d'import](docs/FORMAT_IMPORT_ARTICLES.md)

</div>
