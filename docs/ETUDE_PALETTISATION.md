# PalTunes – Étude métier et scientifique de la palettisation

> Version de l'étude : 1.4 (PalTunes 0.0.5) – document de référence **préalable au développement**.
> Toute règle de calcul du logiciel renvoie à un paragraphe de ce document (§).

---

## 0. Objet et périmètre

PalTunes compose des **conditionnements palettisés** (unités de charge) à partir d'une base articles :

| Besoin | Réponse PalTunes 0.0.1 |
|---|---|
| Palettes **homogènes** (mono-article) | Moteur de plans de couche optimaux (§4), solutions classées, recommandation argumentée (§11) |
| Palettes **hétérogènes** (multi-articles) | Trois stratégies de construction (§7), score multicritère, recommandation |
| Tous types de produits | Caisse / carton, bobine, tube, plaque, sac, fût, bac, autre (§2) |
| Base articles | Champs minimaux selon le type + champs facultatifs (client, famille…), arborescence, import CSV (§9, §13) |
| Supports | Catalogue des palettes courantes : EUR/EPAL, ISO, CP chimie, **plastiques (gris)**, demi, quart (§8) |
| Palettes physiques multiples | Une charge 1600 × 1200 posée sur 2 palettes 800 × 1200 (§5.3) |
| Export | Spécification de palettisation des conditionnements **mono-article** (§12) |

---

## 1. Vocabulaire métier

| Terme | Définition retenue dans PalTunes |
|---|---|
| **Article / produit** | Objet élémentaire posé sur la palette (carton, bobine, tube…). C'est l'« UC » (unité de conditionnement) ou le colis. |
| **Palette physique** | Le support (bois, plastique, carton, métal). Caractérisée par L × l × **hauteur de bois** (hauteur du support seul), tare, charge admissible. |
| **Base** | Assemblage de *n* palettes physiques identiques jointives (ex. 2 × 800 × 1200 côte à côte = base 1600 × 1200). |
| **Charge** | L'ensemble des produits posés sur la base (palette exclue). |
| **Conditionnement (unité de charge)** | Base + charge + accessoires (intercalaires, coiffe). C'est l'objet exporté. |
| **Couche** | Ensemble des produits posés à la même cote Z. |
| **Plan de couche (motif)** | Disposition 2D des produits d'une couche. |
| **Schéma de gerbage interne** | Façon d'empiler les couches : **en colonne** (couches identiques superposées) ou **croisé / imbriqué** (couches alternées A/B). |
| **Gerbage (externe)** | Nombre de conditionnements pouvant être gerbés **sur** un premier (stockage au sol, camion) : 0 = non gerbable (premier niveau seul), 1 = un conditionnement dessus, etc. |
| **Débord (surplomb)** | Dépassement de la charge au-delà du bord de la base. Le **retrait** est l'inverse (charge en retrait du bord). |
| **Intercalaire** | Feuille (carton, plastique) posée entre deux couches ; ajoute une épaisseur. |
| **Coiffe / planche de dessus** | Protection posée sur la dernière couche ; ajoute une hauteur. |
| **Encombrement** | Enveloppe extérieure totale du conditionnement (base, charge, débords, accessoires). |

---

## 2. Typologie des produits et données minimales

Le calcul n'a besoin que de la **géométrie**, du **poids** et des **contraintes d'orientation et de charge**.
Les autres données sont facultatives (aide au classement, recherche, export).

| Type | Forme de calcul | Données **obligatoires** | Orientations possibles | Particularités métier |
|---|---|---|---|---|
| **Caisse / carton** | Pavé | Longueur, largeur, hauteur, poids | Haut imposé (défaut) ou libre (6 orientations) | Résistance à la compression (§6.2) : préférer la colonne si lourde |
| **Bobine** | Cylindre | Diamètre extérieur, laize (longueur d'axe), poids | Axe vertical (« eye to sky », défaut), axe horizontal (« eye to wall ») | Axe horizontal : calage obligatoire ; mandrin facultatif |
| **Tube** | Cylindre | Diamètre extérieur, longueur, poids | Axe horizontal (sens L ou l de la palette) **et/ou** vertical (debout) : les deux sont calculés, le meilleur est proposé, l'axe est **forçable** par conditionnement | Lits en quinconce plus denses (§4.5) ; cornières anti-chute ; debout élancé → cornières + cerclage |
| **Plaque** | Pavé plat | Longueur, largeur, épaisseur, poids | À plat uniquement | Souvent plus grande que la palette → palettes multiples (§5.3) |
| **Sac** | Pavé déformable | Longueur, largeur, hauteur, poids | À plat uniquement | Toujours croisé (cohésion) ; pas de colonne |
| **Fût** | Cylindre | Diamètre, hauteur, poids | Debout uniquement | Typiquement 4 fûts Ø585 sur 1200 × 1200 |
| **Bac / caisse plastique** | Pavé rigide | Longueur, largeur, hauteur, poids | Haut imposé | Empilable en colonne (structure porteuse) |
| **Autre** | Pavé | Longueur, largeur, hauteur, poids | Configurable | Cas générique |

Contraintes facultatives par article (toutes utilisées par le moteur si renseignées) :

- **Charge maxi sur le dessus** (kg) : poids maximal supportable par un exemplaire (load bearing, §6.3).
- **Couches maxi** : nombre maximal de couches superposées de ce produit.
- **Fragile** : rien ne doit être posé dessus (équivaut à charge maxi = 0).
- **Gerbable** : déduit des deux précédents.

Champs **facultatifs** de classement : désignation, client, famille, sous-famille, référence client, EAN, couleur
d'affichage, notes. L'arborescence de la base est construite sur ces champs (§9.4).

---

## 3. Classification scientifique du problème

Dans la typologie de référence des problèmes de découpe et de conditionnement (Wäscher, Haußner & Schumann, 2007) :

| Cas PalTunes | Problème académique | Dimension | Objectif |
|---|---|---|---|
| Palette homogène | **Manufacturer's Pallet Loading Problem (MPLP)** = *Identical Item Packing Problem* | 2D par couche, puis empilement | Maximiser le nombre de produits identiques |
| Produits cylindriques homogènes | *Circle packing in a rectangle* | 2D | Maximiser le nombre de cercles |
| Palette hétérogène | **Distributor's Pallet Loading Problem (DPLP)**, *Single / Multiple Bin-Size Bin Packing Problem* 3D | 3D | Minimiser le nombre de palettes sous contraintes |

Points clés de la littérature :

1. **MPLP** : le statut de complexité n'est pas tranché (la taille de l'entrée est logarithmique), mais des méthodes
   exactes existent pour les tailles réelles (Dowsland, 1987) et des heuristiques atteignent l'optimum sur la quasi-totalité
   des instances pratiques (Scheithauer & Terno, 1996 ; Morabito & Morales, 1998 ; Lins, Lins & Morabito, 2003 ; Birgin,
   Lobato & Morabito, 2010).
2. **DPLP** : NP-difficile au sens fort (il généralise le bin packing). On utilise des heuristiques constructives
   (couches, murs, points extrêmes) évaluées sur des critères multiples (Bischoff & Ratcliff, 1995 ; Bortfeldt & Wäscher, 2013).
3. Les **contraintes pratiques** (stabilité, résistance, orientation, poids, regroupement par article) sont aussi importantes
   que la densité : une solution dense mais instable est inutilisable (Bortfeldt & Wäscher, 2013 ; Ramos et al., 2016).

---

## 4. Palettisation homogène – méthode retenue

### 4.1 Décomposition couche × hauteur

Pour un produit pavé de dimensions (L, l, h) et une orientation verticale donnée :

1. On choisit la **dimension verticale** (h, ou l, ou L si l'orientation est libre).
2. On résout le problème 2D : placer le maximum de rectangles (a × b), rotation de 90° autorisée dans le plan,
   dans la surface utile de la base.
3. On empile les couches jusqu'à la première limite atteinte (§10.2) : hauteur, poids, couches maxi, charge sur le dessus.

Toutes les orientations autorisées sont évaluées ; chacune produit une ou plusieurs solutions.

### 4.2 Surface utile

```
Lu = Lbase + 2 × débord_longueur        lu = lbase + 2 × débord_largeur
```

Un débord négatif est un **retrait** (charge plus petite que la palette, protège les cartons des chocs de fourche).

### 4.3 Bornes supérieures (qualité prouvée)

- **Borne d'aire** : ⌊Lu·lu / (a·b)⌋.
- **Borne d'aire sur dimensions efficaces** (Dowsland, 1984/1987 ; Barnes, 1979) : on remplace Lu (resp. lu) par la plus grande
  combinaison entière i·a + j·b ≤ Lu (resp. ≤ lu). Toute disposition est contenue dans ces dimensions.

PalTunes affiche la borne et **l'écart** : un plan qui atteint la borne est **prouvé optimal**.

### 4.4 Générateur de plans de couche

Trois familles sont calculées et comparées :

| Famille | Principe | Référence |
|---|---|---|
| **Grille simple** | Tous les produits dans la même orientation | Base de comparaison |
| **Guillotine optimale** | Programmation dynamique exacte sur les **points de discrétisation** (combinaisons i·a + j·b) : chaque rectangle est soit rempli en grille, soit coupé en deux sous-rectangles traités récursivement | Herz (1972) ; Christofides & Whitlock (1977) ; Beasley (1985) |
| **Non-guillotine d'ordre 1 (« moulinet » à 5 blocs)** | Quatre blocs tournant autour d'un bloc central, chaque bloc rempli par la guillotine optimale. Les deux chiralités sont explorées | Smith & De Cani (1980) ; Bischoff & Dowsland (1982) ; structure G4 : Scheithauer & Terno (1996) |

Justification : les plans optimaux des instances réelles sont quasiment toujours guillotine ou non-guillotine d'ordre 1
(Scheithauer & Terno, 1996 ; Morabito & Morales, 1998). Combinés à la borne (§4.3), ils donnent à l'utilisateur la
**preuve d'optimalité** dans la grande majorité des cas, en quelques millisecondes.

Exemple : base EUR 1200 × 800, carton 400 × 300 → grille 1 : 3 × 2 = 6 ; grille tournée : 4 × 2 = **8** ; borne d'aire 8 → optimal.

### 4.5 Produits cylindriques

**Cylindres debout** (bobines axe vertical, fûts) – cercles de diamètre D dans un rectangle :

- **Maille carrée** : ⌊Lu/D⌋ × ⌊lu/D⌋ (densité théorique π/4 ≈ 78,5 %).
- **Maille hexagonale (quinconce)** : rangées décalées de D/2, pas entre rangées D·√3/2 ≈ 0,866 D (densité théorique
  π/(2√3) ≈ 90,7 %, Thue / Fejes Tóth). Nombre de rangées : 1 + ⌊(lu − D)/(0,866 D)⌋ ; une rangée décalée contient
  ⌊(Lu − D/2)/D⌋ cercles.
- Les deux mailles sont calculées dans les deux sens ; la meilleure est retenue.

Exemple vérifié : bobines Ø250 sur 1200 × 800 → carrée 4 × 3 = 12 ; hexagonale (rangées le long de 800) 3+2+3+2+3 = **13**.
À l'inverse, Ø400 → carrée 6, hexagonale 5 : la maille carrée gagne. **Aucune règle fixe : il faut calculer les deux.**

Règle métier bobines (pratique papier / film / métal) :
- **Axe vertical** : solution standard ; empilement en colonne (mandrins alignés), jamais croisé.
- **Axe horizontal** : uniquement si le produit l'exige ; une seule couche par défaut, calage (berceaux, cales) obligatoire ;
  PalTunes émet un avertissement.

**Cylindres couchés** (tubes) : on raisonne en **lits**. L'axe est parallèle à L ou à l de la base ; la section (cercles)
s'empile en maille carrée (lits superposés) ou **en quinconce** (lit décalé de D/2, pas vertical 0,866 D, lit décalé de
n ou n − 1 tubes).

Exemple vérifié : tubes Ø110 × 1200 sur EUR, hauteur utile 1656 mm → carré 7 × 15 = 105 ; quinconce 17 lits
(1 + ⌊(1656 − 110)/95,26⌋) alternant 7 et 6 tubes = **111**.

### 4.6 Plaques

Une plaque est un pavé très plat, posé à plat. Une couche contient en général une seule plaque (ou quelques-unes si elles
sont petites) ; le nombre de couches = hauteur utile / épaisseur, limité par le poids (souvent limitant pour l'acier).
Si la plaque dépasse la palette, on utilise des **palettes physiques multiples** (§5.3) ou un débord contrôlé.

### 4.7 Schéma de gerbage interne : colonne ou croisé

Une fois le plan de couche A trouvé, PalTunes calcule ses variantes **B** (miroir en X, miroir en Y, rotation 180°, et
rotation 90° si la base est carrée) et retient celle qui maximise l'**imbrication** : part des produits d'une couche reposant
sur **au moins deux** produits de la couche inférieure (pontage des joints).

- Si aucune variante ne change la disposition (plan symétrique, ex. grille simple), seul le schéma **colonne** existe.
- Sinon deux solutions sont proposées : **colonne** (A/A/A…) et **croisé** (A/B/A/B…).

Le choix entre les deux est un **compromis scientifiquement documenté** (§6.2) : le croisé améliore la cohésion de la charge
mais réduit fortement la résistance à la compression des cartons.

---

## 5. Base, débords et palettes physiques multiples

### 5.1 Débords

- Débord standard recommandé : **0 mm** (charge à fleur) pour les cartons : un débord fait perdre 20 à 40 % de résistance
  en compression aux cartons du bas (§6.2).
- Les plaques et tubes peuvent déborder (paramètre explicite par conditionnement, par longueur et par largeur).
- Un retrait de 10 à 20 mm protège la charge des fourches et des chocs latéraux.

### 5.2 Hauteur et poids

- **Hauteur maximale totale** (palette comprise) : paramètre du conditionnement (défaut 1800 mm). Valeurs usuelles :
  1200 (gerbage ×2 en camion de 2,70 m), 1800, 2000–2200 (charges légères), 2600–2700 (camion complet non gerbé).
- **Poids maximal** de la charge : paramètre (défaut : charge dynamique admissible des palettes physiques de la base).

### 5.3 Palettes physiques multiples

Lorsque la charge est plus grande qu'une palette (plaques, grands cartons, tubes longs), on pose la charge sur **n palettes
identiques jointives** : `n = nL × nl`.

```
Lbase = nL × Lpalette (orientée)      lbase = nl × lpalette (orientée)
```

Exemple : plaque 1600 × 1200 → 2 palettes 800 × 1200 orientées « 800 en longueur », nL = 2, nl = 1 → base 1600 × 1200,
**nombre de palettes physiques par conditionnement = 2**.

Règles : chaque palette doit porter la charge (vérification du poids par palette = poids / n ≤ charge admissible) ;
l'assistant de proposition (§11.2) teste, pour chaque palette active, la palette seule puis l'**assemblage minimal** couvrant l'empreinte du produit (n = ⌈dimension produit / dimension palette⌉ dans chaque sens, palette tournée ou non, jusqu'à 3 × 3) : un assemblage plus grand ne ferait que juxtaposer plusieurs charges.

---

## 6. Stabilité et résistance – fondements

### 6.1 Stabilité géométrique

| Critère | Définition | Seuil PalTunes |
|---|---|---|
| **Taux de support** | Part de la surface de base d'un produit reposant sur la palette ou sur des produits dont le dessus est exactement à sa cote (Junqueira, Morabito & Yamashita, 2012) | ≥ 80 % (paramétrable) ; 100 % en homogène |
| **Imbrication (pontage)** | Part des produits reposant sur ≥ 2 produits inférieurs | Indicateur ; favorise la cohésion |
| **Centre de gravité** | Décalage horizontal du CdG / demi-dimension de la base | Alerte au-delà de 10 % |
| **Hauteur du CdG** | Z du CdG de l'unité complète | Indicateur (basculement) |
| **Élancement** | Hauteur totale / plus petite dimension de la base | Alerte au-delà de 2,5 (filmage renforcé, cerclage) |

La stabilité en transport relève de l'arrimage (EN 12195-1) et de la rigidité de l'unité de charge (EUMOS 40509) : PalTunes
ne remplace pas ces essais, il fournit les indicateurs qui les conditionnent.

### 6.2 Résistance à la compression des cartons

Formule de McKee (McKee, Gander & Wachuta, 1963) : `BCT ≈ 5,87 × ECT × √(e × Z)` (ECT : résistance à l'écrasement sur chant,
e : épaisseur du carton, Z : périmètre de la caisse). La résistance utile est le BCT multiplié par des facteurs de perte
dont les ordres de grandeur sont classiques (Kellicutt, 1963 ; Fibre Box Handbook ; revue de Frank, 2014) :

| Situation | Perte de résistance typique |
|---|---|
| Colonnes parfaitement alignées | 0 % (référence) |
| Colonnes désalignées | 10 – 15 % |
| **Gerbage croisé (imbriqué)** | **40 – 60 %** |
| Débord sur le bord de palette | 20 – 40 % |
| Espaces entre planches de la palette | 10 – 25 % |
| Humidité élevée (≈ 90 % HR) | jusqu'à ≈ 60 % |
| Stockage de longue durée | ≈ 40 % |

Conséquence directe dans PalTunes : **facteur de schéma** = 1,0 en colonne, 0,5 en croisé, × 0,7 en cas de débord.
Ce facteur pondère la *charge maxi sur le dessus* de l'article dans la vérification de résistance (§10.4).

### 6.3 Charge sur le dessus (load bearing)

Le produit le plus sollicité est celui de la couche du bas. Charge reçue (homogène, colonne) :

```
Charge(bas) = (nb_couches − 1) × poids_produit + (gerbages − 1) × poids_unité / nb_produits_par_couche
```

En hétérogène, le poids de chaque produit placé est réparti sur ses supports au prorata des surfaces de contact, puis
propagé récursivement vers le bas ; un placement est refusé s'il fait dépasser la charge admissible d'un produit inférieur.

### 6.4 Règles d'agencement universelles (hétérogène)

1. **Lourd en bas, léger en haut** (stabilité, CdG bas).
2. **Résistant en bas, fragile en haut** ; rien sur un produit fragile.
3. **Grande base en bas** (support).
4. **Regrouper par article** (préparation de commandes, contrôle, déchargement).
5. **Couches homogènes complètes d'abord** lorsque la quantité le permet : plus stables et plus rapides à préparer.

---

## 7. Palettisation hétérogène – méthode retenue

### 7.1 Stratégies

| Stratégie | Principe | Quand elle gagne | Références |
|---|---|---|---|
| **A. Couches homogènes + couche mixte** | Pour chaque article (du plus lourd / résistant au plus léger), autant de couches complètes que possible avec le plan optimal §4 ; le reliquat est placé en couche(s) mixte(s) sur le dessus | Quantités ≥ une couche par article ; préparation et stabilité | Bischoff & Ratcliff (1995) ; pratique logistique |
| **B. Piles par article (murs)** | Placement en points extrêmes avec priorité « fond → avant » : chaque article forme un bloc vertical contigu | Préparation par article, déchargement sélectif | George & Robinson (1980) ; Crainic, Perboli & Tadei (2008) |
| **C. Densité maximale (points extrêmes)** | Placement en points extrêmes « bas → fond → gauche », plusieurs ordres de tri (volume, hauteur, surface, poids) ; le meilleur est retenu | Petites quantités très variées | Crainic, Perboli & Tadei (2008) ; Martello, Pisinger & Vigo (2000) |

### 7.2 Contraintes vérifiées à chaque placement

Inclusion dans la surface utile et la hauteur ; non-chevauchement ; orientations autorisées ; **taux de support ≥ seuil** ;
**charge sur le dessus propagée** (§6.3) ; produit fragile non chargé ; poids maximal ; couches maximales. Si un produit
ne tient plus, une nouvelle unité de charge est ouverte (même base).

### 7.3 Étude approfondie (v0.0.4)

La palettisation hétérogène fait l'objet d'une étude dédiée : **[ETUDE_HETEROGENE.md](ETUDE_HETEROGENE.md)** (règles de
métier, profil de gerbage déduit sans donnée obligatoire supplémentaire, ordre de pose, score de placement, contraintes,
indicateurs, cas de contrôle). Elle prévaut sur §7.1–7.2 et §11.3 pour l'hétérogène.

### 7.4 Contrôle indépendant

Chaque solution est recontrôlée par un validateur qui recalcule chevauchements, inclusion, supports et poids sans réutiliser
l'état du moteur (même principe qu'OptiTunes).

---

## 8. Supports – catalogue par défaut

Normes : ISO 6780 (dimensions principales des palettes intercontinentales : 1200 × 800, 1200 × 1000, 1219 × 1016,
1140 × 1140, 1100 × 1100, 1067 × 1067) ; EN 13698-1 (palette EUR 1200 × 800) ; EN 13698-2 (1200 × 1000) ;
ISO 8611 (essais de charge) ; ISO 3394 (module 600 × 400 des emballages) ; palettes CP (industrie chimique).

| Code | Désignation | L × l × H (mm) | Matériau | Tare (kg) | Charge dyn. (kg) |
|---|---|---|---|---|---|
| EUR1 | Europe EPAL / EUR 1 | 1200 × 800 × 144 | Bois | 25 | 1500 |
| EUR2 | EUR 2 (1200 × 1000) | 1200 × 1000 × 162 | Bois | 33 | 1250 |
| EUR3 | EUR 3 (1000 × 1200) | 1000 × 1200 × 144 | Bois | 29 | 1500 |
| EUR6 | Demi-palette EUR 6 | 800 × 600 × 144 | Bois | 10 | 500 |
| QUART | Quart de palette (display) | 600 × 400 × 144 | Bois | 6 | 250 |
| ISO1210 | Palette industrielle / ISO | 1200 × 1000 × 144 | Bois | 30 | 1500 |
| US4840 | US GMA 48" × 40" | 1219 × 1016 × 140 | Bois | 22 | 1200 |
| AS1165 | Australienne | 1165 × 1165 × 150 | Bois | 35 | 1500 |
| AS1100 | Asie | 1100 × 1100 × 150 | Bois | 30 | 1500 |
| ISO1067 | 42" × 42" | 1067 × 1067 × 150 | Bois | 28 | 1200 |
| CP1 … CP9 | Palettes chimie | 1000 × 1200 × 138 … 1140 × 1140 × 156 | Bois | 15 – 34 | 1000 – 1500 |
| PLA-EUR9 | **Plastique gris** 1200 × 800, 9 pieds (emboîtable) | 1200 × 800 × 150 | Plastique | 9 | 500 |
| PLA-EUR9B | **Plastique gris** 1200 × 800, 9 pieds renforcée | 1200 × 800 × 160 | Plastique | 14 | 1000 |
| PLA-EUR3 | **Plastique gris** 1200 × 800, 3 patins (type H1) | 1200 × 800 × 160 | Plastique | 18 | 1250 |
| PLA-1210 | **Plastique gris** 1200 × 1000, 3 patins | 1200 × 1000 × 160 | Plastique | 22 | 1250 |
| PLA-DEMI | **Plastique gris** demi-palette | 800 × 600 × 150 | Plastique | 5 | 250 |
| PLA-QUART | **Plastique gris** quart display | 600 × 400 × 150 | Plastique | 3 | 150 |
| PERDUE | Palette perdue (export) | 1200 × 800 × 120 | Bois | 15 | 1000 |
| CARTON | Palette carton | 1200 × 800 × 130 | Carton | 6 | 600 |

Chaque palette est représentée en 3D et en 2D selon sa **construction** : 9 blocs + 3 semelles (EUR), 9 blocs sans semelles
(perdue), 3 patins (plastique H1, CP), 9 pieds (plastique emboîtable), bloc plein (carton). Le catalogue est modifiable,
chaque palette peut être exclue des propositions automatiques.

---

## 9. Modèle de données

### 9.1 Article

```
Code* (unique) · Type* · Dimensions* (selon §2) · Poids* · Orientation · Charge maxi dessus · Couches maxi · Fragile
Désignation · Client · Famille · Sous-famille · Réf. client · EAN · Couleur · Notes          (facultatifs)
```

### 9.2 Palette (support)

```
Code* · Désignation · Famille · Matériau · Construction · L* · l* · Hauteur de bois* · Tare · Charge dynamique · Charge statique · Couleur · Active
```

### 9.3 Conditionnement

```
Code* · Désignation · Type (homogène / hétérogène)
Base : palette · orientation · nL × nl
Contraintes : hauteur totale maxi · poids maxi · débords L / l · jeu entre produits · intercalaire (épaisseur, toutes les n couches)
              coiffe (hauteur) · taux de support mini · niveaux de gerbage maxi · hauteur gerbée maxi · centrer la charge
Accessoires : intercalaires (épaisseur, toutes les n couches, aussi sur la palette, poids) · coiffe (hauteur, poids)
              cornières (épaisseur, aile, hauteur, poids) · film (épaisseur) · cerclages (nombre)
Axe forcé des tubes / bobines : automatique (le meilleur), vertical ou horizontal
Contenu : article (homogène) ou lignes article × quantité (hétérogène)
Solution retenue : plan calculé, figé à l'enregistrement (exporté tel quel)
```

### 9.4 Client

```
Code* (cité par le champ Client des articles ; un changement de code est répercuté) · Nom* (deux clients peuvent porter le même nom) · Adresse · CP · Ville · Pays · Contact · Tél. · E-mail
Exigences : palette imposée · hauteur totale maxi · niveaux de gerbage (appliquées à la création d'un conditionnement)
```

Les clients cités par des articles (saisie, import CSV, anciennes bases) sont créés automatiquement. Le poids unitaire
d'un article accepte 5 décimales (0,00001 kg).

### 9.5 Arborescence

L'arbre des articles est regroupé au choix : **Client › Famille › Sous-famille**, **Famille › Type**, **Type › Client** ou
liste plate. Les valeurs vides sont regroupées sous « (Non renseigné) ». Recherche plein texte sur tous les champs.

---

## 10. Règles de calcul (formules)

### 10.1 Hauteurs

```
H_utile_charge = H_totale_maxi − H_bois − H_coiffe
H_charge = Σ hauteurs des couches + nb_intercalaires × e_intercalaire
```

### 10.2 Nombre de couches (homogène)

```
couches = min( ⌊H_utile / (h + e_intercalaire / n_inter)⌋ ,            hauteur (calcul exact couche par couche)
               ⌊Poids_maxi / (poids × n_par_couche)⌋ ,                  poids (couche incomplète autorisée en dernière)
               couches_maxi_article ,
               1 + ⌊Charge_dessus_maxi × facteur_schéma / poids⌋ )      résistance §6.2–6.3
```

### 10.3 Exemple complet vérifié

Carton 400 × 300 × 250, 12 kg, haut imposé, sur EUR 1200 × 800 × 144, hauteur totale maxi 1800 :
plan 8/couche (optimal, borne 8) ; H_utile = 1656 → 6 couches → **48 cartons**, charge 1500 mm, 576 kg,
hauteur totale 1644 mm, encombrement 1200 × 800 × 1644.

### 10.4 Gerbage (externe)

Convention : **gerbages = nombre de conditionnements posés sur le premier** (0 = non gerbable). En interne, la pile compte
`niveaux = gerbages + 1`.

```
niveaux  = min( gerbages_maxi_saisis + 1 ,
                ⌊H_gerbée_maxi / H_encombrement⌋ ,
                plus grand k tel que Charge(bas) avec (k − 1) unités au-dessus ≤ Charge_dessus_maxi × facteur_schéma ,
                plus grand k tel que (k − 1) unités au-dessus ≤ charge statique des palettes )
gerbages = niveaux − 1
```

### 10.5 Encombrement

```
L_enc = max(L_base, L_charge + 2 × (ép_cornière + ép_film))    l_enc = idem en largeur
H_enc = H_bois + H_charge + H_coiffe
Poids total = poids produits + tare des palettes + intercalaires + coiffe + 4 cornières
```

Lecture du récapitulatif : quand une cote d'encombrement diffère **à la fois** de la palette et de la charge, le calcul
est donné entre parenthèses, par exemple longueur `1210 mm (charge 1200 + cornières 2 × 5 = +10 mm)` ; la hauteur est
toujours détaillée `(bois 144 + charge 1634 [+ coiffe])`. Une cote égale à la palette ou à la charge n'a pas de note.
Les schémas 2D portent les cotes d'encombrement en orange, sans formule.

### 10.6 Accessoires

- **Intercalaires** : possibles pour tous les types de produits, entre couches (toutes les n couches) et sur la palette ;
  ils ajoutent leur épaisseur à la hauteur de charge. En lits de tubes en quinconce, seul l'intercalaire sur palette est
  retenu (un intercalaire entre lits empêcherait l'emboîtement).
- **Cornières (coins)** : 4 cornières verticales aux angles de la charge ; elles maintiennent latéralement les tubes
  couchés (anti-roulement), les tubes debout et les bobines couchées. Par défaut elles sont **à l'extérieur** de la
  charge et s'ajoutent à l'encombrement ; option « **contenues dans la palette** » (décochée par défaut) : la surface
  utile est réduite de leur épaisseur de chaque côté, l'encombrement ne dépasse pas la palette (au prix de quelques
  millimètres de charge en moins).
- **Cornières et tubes couchés en débord** (v0.0.5, uniquement ce cas) : une cornière ne peut pas tenir en porte-à-faux
  au bout des tubes. Elles **restent au niveau de la palette**, aux 4 coins, à fleur des bords (cadre en longueur
  `[t ; L_base − t]` dans l'axe des tubes, emprise de la charge en travers). Tout tube qui traverse l'emprise d'une
  cornière (aile `a` en travers, épaisseur `t` dans l'axe, sur la hauteur de la cornière) est **retiré** ; puis, couche
  par couche, ceux qui n'ont plus assez d'appuis (1 en maille carrée, 2 en quinconce) le sont aussi. La perte est
  comptée (avertissement « N tube(s) retiré(s) », récapitulatif, nombre de produits par palette) et les schémas 2D/3D
  montrent les cornières au niveau de la palette. Exemple : tubes Ø110 × 1500 sur EUR 1200 × 800, débord 150,
  cornières 5 × 60 : 7 rangées → 5 rangées, 105 → 75 tubes, encombrement 1500 × 800. Case « **les cornières suivent
  les tubes** » (décochée par défaut) : cochée, elles entourent la charge (105 tubes, encombrement 1510 × 800).
- **Coiffe** : ajoutée à la hauteur d'encombrement.
- **Film étirable** : enveloppe la charge et le haut de la palette ; son épaisseur s'ajoute de chaque côté à la longueur
  et à la largeur d'encombrement. Représenté en 3D (translucide) et en 2D (contour pointillé).
- **Cerclages** : répartis sur la largeur, dans le sens de la longueur, passés sous le plateau ; épaisseur négligeable
  sur l'encombrement ; représentés en 3D et en 2D.

---

## 11. Critères de recommandation

### 11.1 Homogène (base fixée)

1. Solutions **conformes** d'abord (aucune contrainte violée).
2. **Nombre de produits par conditionnement** décroissant.
3. Choix colonne / croisé selon le produit (§4.7, §6.2) :
   - Sac → croisé (pratique universelle) ;
   - Bac, fût, bobine → colonne (structure porteuse, mandrins alignés) ;
   - Caisse : **colonne** si la résistance est limitante (charge sur le dessus utilisée à plus de 60 % en croisé) ou fragile ;
     sinon **croisé** si l'imbrication ≥ 50 % et l'élancement ≥ 1,5 ; sinon colonne.
4. À égalité : meilleur taux de remplissage volumique, CdG centré, moins de changements d'orientation.

### 11.2 Assistant « Proposer » (toutes palettes)

Compare toutes les palettes actives (seules, ou en assemblage minimal si le produit dépasse la palette, §5.3). Critère principal : **densité de transport**
= produits × gerbages / surface au sol d'encombrement (produits / m²), puis débord nul, puis remplissage volumique.

### 11.3 Hétérogène

```
1. Nombre d'unités de charge (minimiser)  – critère dominant
2. Score = 0,40 × remplissage volumique + 0,30 × stabilité + 0,30 × homogénéité
   stabilité   = moyenne(taux de support) × (1 − décalage CdG)
   homogénéité = part des produits en couches mono-article ou en blocs contigus par article
```

La solution de meilleur score est marquée **Recommandée** avec sa justification.

---

## 12. Export des conditionnements mono-article

Format CSV (séparateur « ; », UTF-8 avec BOM, décimales « , », une ligne par conditionnement). Colonnes :

| Bloc | Colonnes |
|---|---|
| Identification | Code conditionnement, désignation, code article, désignation article, client, palette |
| **Spécification de palettisation** | Nombre de produits par palette produit ; **nombre de palettes physiques par conditionnement** ; **nombre de gerbages** (0 = non gerbable) ; couches ; produits par couche ; schéma |
| **Dimensions de la palette** | Longueur (mm) ; largeur (mm) ; **hauteur de bois** (mm) – dimensions de la base complète |
| **Dimensions de la charge** | Longueur ; largeur ; hauteur (mm) – produits seuls |
| **Encombrement de palettisation** | Longueur ; largeur ; hauteur (mm) – §10.5 |
| Complément | Poids charge, poids total, taux de remplissage |

Colonnes complémentaires : **plan par couche** (« C1-6 : plan A, 8 produit(s) ; C7 : plan B, 5 produit(s) ») et
**intercalaires (couches)** (« aucun » ou « sous C1, 4, 7 »).

Une fiche de palettisation imprimable (PDF via « Microsoft Print to PDF ») reprend ces données ; le **plan de
palettisation** est ajouté en pages finales, avec les informations minimales de mise sur palette :

1. vue 3D de l'unité de charge et tableau des couches (n°, plan, nombre de produits, cote z, hauteur, intercalaire
   dessous) ;
2. pour chaque plan de couche distinct (A, B… ; deux couches ont le même plan si tous leurs produits ont la même
   position, empreinte et orientation) : vue de dessus avec les produits numérotés dans l'ordre de lecture (rangées
   le long de la largeur, puis le long de la longueur) et tableau des positions (n°, article, X, Y du coin du produit
   depuis le coin de la palette, empreinte, sens).

---

## 13. Import CSV des articles

Séparateur détecté (« ; », « , », tabulation), en-têtes reconnus avec synonymes, décimales « , » ou « . ». Mise à jour par
code (création ou modification), rapport ligne par ligne. Format détaillé : `docs/FORMAT_IMPORT_ARTICLES.md`.

---

## 14. Couverture fonctionnelle StackBuilder

PalTunes reprend a minima les fonctions de StackBuilder (treeDiM), avec les compléments OptiTunes :

| Fonction StackBuilder | PalTunes 0.0.1 |
|---|---|
| Base d'articles (caisses, cylindres, palettes…) | Base clients et articles (8 types), arborescence, import / export CSV ; catalogue de 27 palettes |
| Analyse caisse / palette (produits identiques) | Conditionnement homogène : plans optimaux, colonne / croisé, quinconce, toutes orientations autorisées |
| Analyse boîte / caisse (colisage) | Espace **Colisage** : catalogue de caisses modifiable ; palette de destination (EUR 1 par défaut) : chaque caisse possible est palettisée et la meilleure est celle qui donne le **plus de produits par palette** (après la règle de manutention manuelle ≤ 25 kg brut, puis remplissage du volume intérieur, puis tare) ; « Forcer la caisse » impose une caisse du catalogue (palette alors ignorée) ; caisse spécifique ; caisse ouverte / fermée en 3D et 2D ; création de l'article caisse puis palettisation |
| Cylindres sur palette | Bobines, tubes (axe vertical / horizontal forçable), fûts ; maille carrée et hexagonale |
| Contraintes : hauteur, poids, nombre, débord | Hauteur totale, poids, quantité imposée, débords / retraits, jeu, couche incomplète |
| Intercalaires, coiffe, cornières, film, cerclage | Tous, avec poids et effet sur l'encombrement, représentés en 3D / 2D |
| Palettes hétérogènes (HPallet) | Trois stratégies, score multicritère, unités multiples |
| Vues 3D / 2D, rapport | 3D par couche avec survol, 2D cotées dessus / côté / face, fiche imprimable (PDF) |
| Export | CSV des spécifications mono-article (§12) |
| Analyse camion | Hors périmètre : outil OptiTunes |

## 15. Limites de la version 0.0.1 et évolutions

- Export limité aux conditionnements mono-article (demandé) ; l'hétérogène est calculé, visualisé et imprimable.
- Pas de calcul de BCT à partir des caractéristiques du carton (la charge maxi sur le dessus est saisie).
- Plans non-guillotine d'ordre supérieur (L-approach) non implémentés : la borne affichée signale les rares cas non prouvés.
- Pas d'éditeur manuel de couche (déplacement d'un produit à la souris) ni d'optimisation des dimensions de caisse.
- Évolutions : export hétérogène, éditeur de couche, optimisation de caisse, chargement camion (lien OptiTunes), étiquette logistique.

---

## 16. Bibliographie

- Barnes, F.W. (1979). Packing the maximum number of m × n tiles in a large p × q rectangle. *Discrete Mathematics*, 26, 93–100.
- Beasley, J.E. (1985). Algorithms for unconstrained two-dimensional guillotine cutting. *Journal of the Operational Research Society*, 36, 297–306.
- Birgin, E.G., Lobato, R.D., Morabito, R. (2010). An effective recursive partitioning approach for the packing of identical rectangles in a rectangle. *JORS*, 61, 306–320.
- Bischoff, E.E., Dowsland, W.B. (1982). An application of the micro to product design and distribution. *JORS*, 33, 271–280.
- Bischoff, E.E., Ratcliff, M.S.W. (1995). Issues in the development of approaches to container loading. *Omega*, 23(4), 377–390.
- Bortfeldt, A., Wäscher, G. (2013). Constraints in container loading – A state-of-the-art review. *European Journal of Operational Research*, 229(1), 1–20.
- Christofides, N., Whitlock, C. (1977). An algorithm for two-dimensional cutting problems. *Operations Research*, 25(1), 30–44.
- Crainic, T.G., Perboli, G., Tadei, R. (2008). Extreme point-based heuristics for three-dimensional bin packing. *INFORMS Journal on Computing*, 20(3), 368–384.
- Dowsland, K.A. (1987). An exact algorithm for the pallet loading problem. *EJOR*, 31(1), 78–84.
- Frank, B. (2014). Corrugated box compression – A literature survey. *Packaging Technology and Science*, 27(2), 105–128.
- George, J.A., Robinson, D.F. (1980). A heuristic for packing boxes into a container. *Computers & Operations Research*, 7, 147–156.
- Herz, J.C. (1972). Recursive computational procedure for two-dimensional stock cutting. *IBM Journal of Research and Development*, 16, 462–469.
- Junqueira, L., Morabito, R., Yamashita, D.S. (2012). Three-dimensional container loading models with cargo stability and load bearing constraints. *Computers & Operations Research*, 39, 74–85.
- Kellicutt, K.Q. (1963). Effect of contents and load bearing surface on compressive strength and stacking life of corrugated containers. *TAPPI*, 46(1).
- Lins, L., Lins, S., Morabito, R. (2003). An L-approach for packing (ℓ, w)-rectangles into rectangular and L-shaped pieces. *JORS*, 54, 777–789.
- Martello, S., Pisinger, D., Vigo, D. (2000). The three-dimensional bin packing problem. *Operations Research*, 48(2), 256–267.
- McKee, R.C., Gander, J.W., Wachuta, J.R. (1963). Compression strength formula for corrugated boxes. *Paperboard Packaging*, 48(8).
- Morabito, R., Morales, S. (1998). A simple and effective recursive procedure for the manufacturer's pallet loading problem. *JORS*, 49, 819–828.
- Ramos, A.G., Oliveira, J.F., Gonçalves, J.F., Lopes, M.P. (2016). A container loading algorithm with static mechanical equilibrium stability constraints. *Transportation Research Part B*, 91, 565–581.
- Scheithauer, G., Terno, J. (1996). The G4-heuristic for the pallet loading problem. *JORS*, 47, 511–522.
- Smith, A., De Cani, P. (1980). An algorithm to optimize the layout of boxes in pallets. *JORS*, 31, 573–578.
- Steudel, H.J. (1979). Generating pallet loading patterns: a special case of the two-dimensional cutting stock problem. *Management Science*, 25(10), 997–1004.
- Wäscher, G., Haußner, H., Schumann, H. (2007). An improved typology of cutting and packing problems. *EJOR*, 183, 1109–1130.
- Normes : ISO 6780, EN 13698-1/-2, ISO 8611, ISO 3394, EN 12195-1, EUMOS 40509.
