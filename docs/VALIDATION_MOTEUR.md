# PalTunes – Validation du moteur de palettisation

> À jour de PalTunes 0.1.7. Les chiffres ci-dessous sont produits par les tests automatiques
> [`ValidationTests.cs`](../tests/PalTunes.Tests/ValidationTests.cs) (palettes) et
> [`CaseValidationTests.cs`](../tests/PalTunes.Tests/CaseValidationTests.cs) (colisage), rejoués à chaque compilation :
> une régression du moteur fait échouer la suite.

## 1. Méthode

Comparer le moteur à des références **indépendantes de lui** :

| Référence | Nature | Ce qu'elle garantit |
|---|---|---|
| Optimum publié | instances du *manufacturer's pallet loading problem* résolues dans la littérature (Birgin, Lobato, Morabito) | nombre exact de boîtes du meilleur plan connu |
| Borne de surface | aire de la palette / aire du produit | aucun plan ne peut faire mieux |
| Borne de Barnes | chaque côté réduit à la plus grande combinaison de longueurs de produit qu'il contient, puis surface | borne plus serrée ; atteinte = **optimum prouvé** |
| Maille de cercles | meilleure maille carrée ou hexagonale (rangées dans les deux sens), calculée à part | référence des produits ronds |
| Densité des cercles | π / √12 (maille hexagonale) appliquée à la surface | aucun plan de cercles ne peut faire mieux |
| Usage industriel | 2 fûts de 200 L sur EUR, 4 sur 1200 × 1200, 1 cuve IBC par niveau | pratique courante des fournisseurs |
| Bornes hétérogènes | volume total / volume utile, poids total / charge de la palette | nombre minimal de palettes, pour tout moteur |

Chaque solution retenue passe en plus le **contrôle indépendant** (chevauchements, appuis, hauteur, poids,
débords) : aucune violation n'est tolérée.

## 2. Palettes homogènes : un cas par type de produit

Palette EUR 1200 × 800 × 144, hauteur totale 1800 mm, sauf mention.

| Type | Cas | Référence | Borne | PalTunes / couche | Verdict |
|---|---|---|---|---|---|
| Caisse | Carton 600 × 400 | 4 | 4 | **4** | optimum |
| Caisse | Carton 400 × 300 | 8 | 8 | **8** | optimum |
| Caisse | Carton 300 × 200 | 16 | 16 | **16** | optimum |
| Caisse | Carton 365 × 245 | 9 (Barnes) | 9 | **9** | optimum (la surface brute laisserait croire à 10) |
| Caisse | Carton 330 × 220 sur 1200 × 1000 | 15 (Barnes) | 15 | **15** | optimum |
| Caisse | Instance N4 : palette 42 × 39, boîte 9 × 4 | 45 (publié) | 45 | **45** | optimum (44 avant 0.1.5) |
| Caisse | Instance N1 : palette 43 × 26, boîte 7 × 3 | 53 (publié) | 53 | **52** | à 1 boîte (98 %) — voir §4 |
| Sac | Sac 600 × 400 | 4 | 4 | **4** | optimum |
| Bac | Bac 600 × 400 | 4 | 4 | **4** | optimum |
| Plaque | Plaque 1200 × 800 ép. 20 | 1 | 1 | **1** (82 couches) | optimum |
| Fût | Fût 200 L Ø585 sur EUR | 2 (usage) | 2 | **2** | conforme |
| Fût | Fût 200 L Ø585 sur 1200 × 1200 | 4 (usage) | 4 | **4** | conforme |
| Bobine | Ø400 laize 300 debout | 6 (maille) | 6 | **6** | optimum |
| Tube | Ø110 debout | 80 (maille) | 91 | **80** | égal à la meilleure maille |
| Bidon | Jerrican 20 L 290 × 190 × 370 | 16 (Barnes) | 16 | **16** | optimum |
| Seau | Seau 10 L Ø270 | 11 (maille) | 15 | **11** | égal à la meilleure maille |
| Bouteille | Bouteille 1,5 L Ø90 | 125 (maille) | 136 | **125** | égal à la meilleure maille |
| Cuve | Cuve IBC 1200 × 1000 × 1160 | 1 / niveau (usage) | 1 | **1** (2 niveaux) | conforme |
| Autre | Pavé générique 500 × 350 | 4 (Barnes) | 4 | **4** | optimum |

Temps de calcul : 0 à 60 ms par cas.

## 3. Palettes hétérogènes : compositions multi-types

Toutes les quantités demandées sont placées ; chaque solution passe le contrôle indépendant.

| Composition | Produits | Borne (palettes) | PalTunes | Remplissage moyen | Temps |
|---|---|---|---|---|---|
| Cartons de trois formats (20 + 40 + 60) | 120 | 3 | **3** | 95,8 % | 35 ms |
| Cartons + bacs + sacs | 48 | 2 | **2** | 100 % | 12 ms |
| Bidons + seaux + cartons | 102 | 2 | **2** | 81,9 % | 44 ms |
| Bouteilles + bidons + seaux | 348 | 2 | **2** | 68,8 % | 0,25 s |
| Fûts + bidons + sacs (hauteur 2000) | 56 | 2 | **2** | 75,7 % | 13 ms |
| Bobines + tubes couchés + plaques | 62 | 1 | 2 | 77,9 % | 15 ms |
| Tous les types sauf cuve (9 articles) | 200 | 2 | 3 | 61,3 % | 0,23 s |
| Cuves IBC + bidons (1200 × 1000, hauteur 2600) | 34 | 2 | **2** | 86,7 % | 17 ms |

La borne ignore la géométrie : elle suppose qu'on peut remplir 100 % du volume. Six compositions l'atteignent (optimum
prouvé) ; les deux autres sont à une palette de la borne. Elles mêlent plaques pleine palette, rouleaux couchés et
cylindres de diamètres différents, qui ne peuvent pas partager les mêmes couches. Le test exige au plus borne + 1.

## 4. Amélioration apportée par la validation (0.1.5)

La campagne a mis en évidence deux écarts sur les instances publiées (N1, N4). Le plan de couche des produits
rectangulaires utilise désormais des **moulinets récursifs** : chaque bloc d'un moulinet à 5 blocs peut être
lui-même guillotine ou moulinet (méthode récursive de Morabito & Morales), quand la taille du problème le permet.

Balayage de 7 965 tailles de cartons (40 à 600 mm) sur 1200 × 800, 1200 × 1000 et 1140 × 1140 :

- **43 plans gagnent une boîte par couche**, la plupart atteignant leur borne (optimum prouvé) ; aucun plan n'est dégradé ;
- **tous les plans sont géométriquement vérifiés** : aucun chevauchement, tout dans la palette, jamais au-delà de la borne ;
- temps moyen 8 ms par plan, 0,4 s au pire ; la guillotine (plus simple à poser) est gardée à égalité.

Exemples : 1200 × 800 avec 271 × 201 : 14 → 15 ; 208 × 165 : 26 → 27 ; instance N4 : 44 → 45.

Limite connue : l'instance N1 (52 au lieu de 53) relève de l'« approche en L » (Lins, Lins & Morabito), plus coûteuse,
non implémentée.

## 6. Colisage homogène : un cas par type de produit

Dimensions **intérieures** de la caisse. Référence : borne de Barnes (ou meilleure maille de cercles) par couche ×
nombre de couches, ou usage courant.

| Type | Cas | Référence | PalTunes / caisse | Verdict |
|---|---|---|---|---|
| Caisse | Boîte 190 × 130 × 140 dans 590 × 390 × 290 | 9 × 2 = 18 (Barnes) | **18** | optimum |
| Bouteille | Bouteille 1,5 L Ø90 × 320 dans 370 × 280 × 330 | 12 (carton de 12) | **12** | conforme |
| Bidon | Jerrican 20 L dans 600 × 400 × 400 | 4 (Barnes) | **4** | optimum |
| Seau | Seau 10 L Ø270 dans 560 × 560 × 270 | 4 (maille) | **4** | optimum |
| Tube | Ø110 × 1200 couché dans 1210 × 340 × 340 | 9 (lits 3 × 3) | **9** | conforme |
| Bobine | Ø400 laize 300 dans 820 × 820 × 310 | 4 (maille) | **4** | optimum |
| Plaque | 1200 × 800 × 20 dans 1210 × 810 × 300 | 15 | **15** | optimum |
| Sac | 600 × 400 × 120 dans 610 × 410 × 500 | 4 | **4** | optimum |
| Bac | 600 × 400 × 300 dans 1210 × 810 × 620 | 4 × 2 = 8 (Barnes) | **8** | optimum |
| Fût | Fût 200 L Ø585 dans 1200 × 1200 × 900 | 4 (usage) | **4** | conforme |
| Cuve | IBC 1200 × 1000 × 1160 dans 1210 × 1010 × 1170 | 1 | **1** | conforme |
| Autre | Pavé 500 × 350 × 120 dans 1010 × 710 × 250 | 4 × 2 = 8 (Barnes) | **8** | optimum |

## 7. Colisage hétérogène : compositions multi-types

Meilleure caisse du catalogue par défaut (15 caisses), palette de destination EUR 1200 × 800, 25 kg brut maximum par
caisse manutentionnée à la main. Borne : nombre minimal de caisses du format retenu, par le volume (enveloppes) et par
le poids (charge maxi de la caisse et 25 kg brut).

| Composition | Produits | Caisse retenue | Borne | PalTunes | Remplissage | Palettes |
|---|---|---|---|---|---|---|
| Boîtes de trois formats | 34 | CRT-8060 | 1 | **1** | 66 % | 1 |
| Bouteilles 1,5 L + flacons | 36 | CRT-6040X | 2 | **2** | 27 % | 1 |
| Bidons 5 L + bouteilles | 10 | CRT-6040X | 2 | **2** | 28 % | 1 |
| Seaux + pots | 12 | CRT-4030H | 2 | 3 | 50 % | 1 |
| Tubes couchés + petites boîtes | 22 | CRT-6040 | 1 | **1** | 59 % | 1 |
| Bobines + sacs + boîtes | 14 | BAC-6040H | 1 | **1** | 61 % | 1 |
| Tous les types courants (8 articles) | 32 | CRT-6040X | 2 | **2** | 37 % | 1 |

Tous les produits sont placés, chaque caisse respecte sa charge et les 25 kg brut, le contrôle indépendant ne relève
aucune violation. Six compositions atteignent la borne (optimum prouvé) ; « Seaux + pots » est à une caisse de la
borne (seaux Ø270 et pots ne se combinent pas en deux cartons). Les compositions de bouteilles ou de bidons sont
limitées par le **poids** (25 kg brut), d'où un remplissage volumique modéré.

### Défauts corrigés par cette campagne (0.1.6)

- **Caisse limitée par sa charge maxi** : quand une seule couche dépassait la charge (26 bouteilles de 1,6 kg pour un
  carton de 30 kg), le colisage concluait que le produit « ne tenait pas ». La dernière couche est désormais remplie
  jusqu'au poids : le carton reçoit 18 bouteilles. Cela concernait aussi le colisage d'un seul article.
- **Classement des caisses mixtes** : une grande caisse bois l'emportait parce qu'elle demandait moins de caisses ;
  le critère est maintenant le plus petit volume total de caisses (meilleur remplissage), après la manutention à la
  main et le nombre de palettes.

## 8. Améliorations 0.1.7

### Produits ronds : mailles mixtes

Le plan de couche des cylindres debout ne se limite plus à la maille carrée ou à la quinconce : il cherche la
meilleure suite de rangées, chacune alignée ou décalée d'un demi-diamètre, deux rangées consécutives distantes d'un
diamètre (même décalage) ou de D·√3/2 (cercles imbriqués). Quelques rangées alignées glissées dans une quinconce
gagnent souvent une rangée.

Sur 581 cas (diamètres de 30 mm au plus petit côté, six bases de 390 × 290 à 1200 × 1000) : 44 plans gagnent 1 à
2 produits par couche, aucun n'est dégradé ; chaque plan est contrôlé disque par disque (dans la base, centres à au
moins un diamètre).

| Base | Diamètre | Avant | 0.1.7 |
|---|---|---|---|
| 1200 × 800 | Ø107 | 84 | **86** |
| 1200 × 800 | Ø156 | 36 | **38** |
| 1200 × 800 | Ø250 | 13 | **14** |
| 1200 × 1000 | Ø170 | 39 | **41** |
| 1140 × 1140 | Ø79 | 216 | **218** |

### Hétérogène : unités pleines et ordres de pose

- **Unités pleines mono-article** : un article qui remplit seul une unité (palette ou caisse) ne la monopolise plus
  quand il y reste de la place ; la variante « tout mélangé » est calculée aussi (jusqu'à 20 000 produits) et, pour
  chaque stratégie, la variante qui demande le moins d'unités est retenue.
- **Recherche élargie** (commandes de 400 produits au plus) : jusqu'à 120 ordres de pose supplémentaires — articles
  dans un autre ordre, volumes légèrement perturbés — avec les deux règles de pose, arrêt dès la borne de volume ou
  après 1,5 s ; tirages à graine fixe, même commande, même résultat.

Sur 80 compositions aléatoires de 2 à 4 formats de colis (40 sur palette EUR, 40 en carton 600 × 400 × 300) :

| | Avant 0.1.7 | 0.1.7 |
|---|---|---|
| Compositions où une unité de moins existe (solution réalisable, appui total, contrôle sans violation) | 19 | **4** |
| Compositions à l'optimum prouvé | 45 | **62** |

Exemples : 12 colis 294 × 238 × 151 + 10 colis 171 × 132 × 144 en carton : 7 → 6 caisses (optimum) ; 6 + 6 colis
283 × 253 × 167 et 280 × 228 × 121 : 5 → 3 caisses ; 11 + 13 colis de 376 mm de haut sur palette : 3 → 2 palettes.

### Plans de couche des cartons

Sur 302 plans de couche (palettes et caisses standard, instances publiées, cas aléatoires), 278 sont prouvés optimaux
(borne de Barnes ou preuve qu'un produit de plus est impossible) ; aucun meilleur plan n'a été trouvé pour les autres,
qui sont des instances difficiles (dont N1, §4).

## 9. Références

- E. G. Birgin, R. D. Lobato, R. Morabito, *An effective recursive partitioning approach for the packing of identical
  rectangles in a rectangle*, Journal of the Operational Research Society, 2010.
- R. Morabito, S. Morales, *A simple and effective recursive procedure for the manufacturer's pallet loading problem*,
  Journal of the Operational Research Society, 1998.
- L. Lins, S. Lins, R. Morabito, *An L-approach for packing (l, w)-rectangles into rectangular and L-shaped pieces*,
  Journal of the Operational Research Society, 2003.
- J. E. Beasley, *An exact two-dimensional non-guillotine cutting tree search procedure*, 1985 ; F. W. Barnes,
  *Packing the maximum number of m × n tiles in a large p × q rectangle*, 1979.
