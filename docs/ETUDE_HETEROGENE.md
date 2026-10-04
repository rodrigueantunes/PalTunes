# PalTunes – Étude approfondie de la palettisation hétérogène (v0.0.4)

> Complément de [ETUDE_PALETTISATION.md](ETUDE_PALETTISATION.md) §7, rédigé **avant** le développement de la 0.0.4.
> Exigence fixée : **aucune donnée article obligatoire supplémentaire**. Tout ce qui manque est **déduit** des données
> déjà connues (type, dimensions, poids) et affiné par les champs facultatifs quand ils sont renseignés.

---

## 1. Le problème réel

Une palette hétérogène (« palette mixte », « palette multi-références ») est le résultat d'une préparation de commande :
plusieurs articles, en quantités quelconques, sur un même support. Sur le terrain, un préparateur applique d'instinct une
poignée de règles ; quand elles ne sont pas respectées, les dégâts sont toujours les mêmes : cartons écrasés, palette qui
penche, charge qui glisse au filmage, produits fragiles cassés, palette trop lourde d'un côté au chariot.

Règles de métier constatées (pratique de préparation, guides de chargement des distributeurs, littérature §12) :

| N° | Règle | Pourquoi |
|---|---|---|
| R1 | **Lourd et résistant en bas, léger et fragile en haut** | Écrasement, stabilité (centre de gravité bas) |
| R2 | **Rien sur un fragile** | Casse |
| R3 | **Ce qui est dessous doit supporter tout ce qui est dessus** (pas seulement l'article posé juste au-dessus) | Écrasement progressif des cartons du bas |
| R4 | **Poser à plat sur une surface de niveau** ; construire par couches de même hauteur | Appui, stabilité, filmage |
| R5 | **Les grandes bases en bas** (plaques, sacs, gros cartons) | Assise de toute la palette |
| R6 | **Ce qui roule (tubes, bobines couchées) ne porte rien d'autre que lui-même**, calé | Basculement, roulement |
| R7 | **Les sacs sont une base, pas un plateau** : on n'y pose que ce qui couvre largement leur surface | Dessus bombé, appui irrégulier |
| R8 | **Une plaque doit être portée sur toute sa surface** | Flexion, rupture, glissement |
| R9 | **Centre de gravité centré** sur la palette, charge répartie entre les palettes physiques | Basculement au chariot, charge par palette |
| R10 | **Regrouper par article** tant que c'est possible | Préparation, contrôle, déchargement |
| R11 | **Respecter poids et hauteur maxi** de l'unité de charge et la charge admissible de chaque palette | Sécurité, transport |

PalTunes 0.0.3 appliquait R2, R3 (si la charge maxi était saisie), R10, R11 et un tri « fragiles en dernier ». La 0.0.4
applique **toutes** les règles, y compris quand l'utilisateur n'a saisi que les données minimales.

---

## 2. Données disponibles

| Donnée | Statut | Usage |
|---|---|---|
| Type, dimensions, poids | Obligatoires (inchangé) | Géométrie, densité, classe de comportement, capacité déduite |
| Orientation / axe | Facultatif (valeur par défaut) | Orientations autorisées, cylindres couchés |
| Charge maxi sur le dessus | Facultatif | **Remplace** la capacité déduite |
| Couches maxi | Facultatif | Limite de superposition du même article |
| Fragile | Facultatif | Capacité nulle, zone haute |

Aucune nouvelle donnée n'est demandée. Le **profil de gerbage** de chaque article (§3) est calculé et **affiché** sur la
fiche article, avec son origine (« saisie » ou « déduite »), pour que l'utilisateur puisse le vérifier et le corriger en
renseignant les champs facultatifs s'il le souhaite.

---

## 3. Profil de gerbage déduit

### 3.1 Classe de comportement (par type)

| Classe | Types | Comportement |
|---|---|---|
| **Carton** | Caisse / carton, Autre | Résiste en compression verticale, perd 40 à 60 % de sa résistance quand la charge n'est pas alignée sur ses parois (Kellicutt 1963, étude §6.2) |
| **Rigide** | Bac, Fût, Bobine debout, Plaque | Structure porteuse ; garde sa résistance en gerbage non aligné |
| **Souple** | Sac | Très résistant en compression, mais **dessus bombé** : mauvais plateau (R7) |
| **Roulant** | Tube couché, Bobine couchée | Ne porte que des articles identiques (lits), doit être calé (R6) |

### 3.2 Capacité portante déduite (kg sur le dessus d'un exemplaire)

Principe de **l'auto-gerbage** : un emballage industriel est dimensionné pour supporter une palette homogène de lui-même
sur la hauteur de charge standard. Avec `H_ref = 1 656 mm` (1 800 mm palette EUR comprise) et `n = ⌊H_ref / h⌋` exemplaires
de hauteur `h` dans la pile :

```
C_déduite = k × w × (n − 1)        w : poids unitaire, k : coefficient de mélange
```

| Classe | k | Justification |
|---|---|---|
| Carton | **0,5** | Gerbage hétérogène = non aligné : 40 à 60 % de perte (§6.2), on retient la moitié |
| Rigide | **1,0** | Pas de perte liée au désalignement |
| Souple | **1,0** | Compression très élevée (sacs de 25 kg gerbés sur 10 rangs) |
| Roulant | **0** pour les autres articles | R6 ; les articles identiques restent possibles (lits) |

Le coefficient `k = 0,5` des cartons ne vaut que pour les charges **non alignées**. Une charge qui descend jusqu'au carton
par des colonnes parfaitement alignées (même empreinte, typiquement des couches complètes du même article) retrouve
`k = 1` : c'est le gerbage en colonne, sans perte (§6.2 de l'étude principale). Le moteur suit, pour chaque produit, le
chemin de chaque charge reçue ; dès qu'une charge arrive par un appui décalé, la capacité « mélange » s'applique.

Règles de priorité : **Fragile → 0** ; **Charge maxi saisie → valeur saisie** (donnée du fabricant, non pondérée) ;
sinon `C_déduite`, avec un plancher de `0,5 × w` pour un produit non fragile (un carton porte toujours au moins un produit
léger posé dessus).

Exemples (vérifiés par les tests) :

| Article | h | w | n | Classe | Capacité déduite |
|---|---|---|---|---|---|
| Carton 400 × 300 × 250 | 250 | 12 kg | 6 | Carton | 0,5 × 12 × 5 = **30 kg** |
| Carton 600 × 400 × 300, 9 kg | 300 | 9 kg | 5 | Carton | 0,5 × 9 × 4 = **18 kg** |
| Bac 600 × 400 × 300, 8 kg | 300 | 8 kg | 5 | Rigide | 8 × 4 = **32 kg** |
| Sac 25 kg (h 120) | 120 | 25 kg | 13 | Souple | 25 × 12 = **300 kg** |
| Fût 200 L (h 880, 220 kg) | 880 | 220 kg | 1 | Rigide | plancher **110 kg** |

### 3.3 Exigence d'appui (taux de support minimal)

| Situation | Seuil |
|---|---|
| Cas général | Paramètre du conditionnement (80 % par défaut) |
| **Plaque** posée sur autre chose que la palette (R8) | **95 %** |
| Article posé **sur un sac** (R7) | **90 %** |
| **Roulant** posé sur autre chose que la palette | **90 %** |
| Appui sur un **fût ou une bobine debout** | Surface de contact comptée à π/4 de l'enveloppe (dessus circulaire) |

### 3.4 Zone conseillée

| Zone | Articles |
|---|---|
| **Bas** | Plaques, sacs ; fûts, bobines et cylindres couchés **lourds** (w ≥ 15 kg) ; tout article dense et lourd (ρ ≥ 0,6 kg/dm³ et w ≥ 15 kg) |
| **Haut** | Fragiles ; articles dont la capacité est inférieure à leur propre poids (ils ne portent même pas un exemplaire d'eux-mêmes) ; très légers (ρ < 0,08 kg/dm³) ; cylindres couchés légers (< 15 kg : ils ne portent que leurs semblables, inutile de leur réserver le bas) |
| **Milieu** | Tous les autres (dont les petites bobines et petits fûts debout, < 15 kg) |

---

## 4. Ordre de pose (dessus / dessous)

L'ordre dans lequel les articles sont présentés au placement décide, en pratique, de ce qui finit en bas. Clé de tri :

1. **Zone** : Bas → Milieu → Haut (R1, R2, R5).
2. **Capacité portante** `C` décroissante (valeur absolue, en kg) : ce qui porte le plus passe dessous (R3). La résistance
   relative `C / w` n'est pas retenue : elle placerait un carton léger mais « solide pour son poids » sous un carton lourd
   qui l'écraserait (constaté en test).
3. **Poids unitaire** décroissant (R1).
4. **Surface au sol** décroissante (R5).
5. **Hauteur** : les articles de même hauteur se suivent, ce qui produit des couches de niveau (R4).
6. **Article** : regroupement (R10).

Ce tri est appliqué aux trois stratégies (§7) ; la stratégie « densité maximale » essaie en plus des variantes (volume,
surface, poids) **à l'intérieur de chaque zone**, sans jamais remonter un article de la zone Haut sous la zone Bas.

---

## 5. Choix de la position (score de placement)

Pour chaque article, toutes les positions candidates (points extrêmes, Crainic et al. 2008) et orientations autorisées
sont évaluées. Les contraintes dures (§6) éliminent les positions interdites ; parmi les positions possibles :

| Priorité | Critère | Règle |
|---|---|---|
| 1 | **Cote Z la plus basse** (arrondie à 10 mm) | Construction par niveaux, centre de gravité bas (R1, R4) |
| 2 | **Surface de niveau** | Préférer une position où le dessus de l'article s'aligne avec un dessus voisin existant (couche plane) (R4) |
| 3 | **Équilibre** | Préférer la position qui rapproche le centre de gravité de la charge du centre de la base, pondéré par le poids de l'article (R9) |
| 4 | Fond → gauche | Compacité |

Après remplissage, la charge est **recentrée** sur la base (même décalage pour tous les produits) si l'option « centrer
la charge » est active (défaut).

---

## 6. Contraintes dures vérifiées à chaque pose

1. Inclusion dans la surface utile (débords, cornières contenues) et la hauteur maxi.
2. Non-chevauchement (enveloppes ; cylindres de même axe par distance des axes).
3. **Taux d'appui** selon §3.3 (plaques, sacs, roulants, dessus circulaires).
4. **Capacité portante propagée** (R3) : le poids de l'article est réparti sur ses appuis au prorata des surfaces de contact,
   puis propagé récursivement jusqu'à la palette ; refus si un article inférieur dépasse sa capacité (saisie ou déduite).
5. **Roulants** : seul un exemplaire du même article peut être posé dessus (R6).
6. **Fragile** : rien dessus (capacité 0).
7. **Couches maxi** du même article (si saisi).
8. **Poids maxi** de l'unité et charge dynamique de chaque palette physique (R11).

---

## 7. Stratégies (inchangées dans leur principe, enrichies)

| Stratégie | 0.0.4 |
|---|---|
| A. Couches homogènes + reliquat | Les couches complètes sont empilées **dans l'ordre §4** ; une couche n'est posée que si toutes les capacités inférieures la supportent ; le reliquat est placé avec le score §5 |
| B. Piles par article | Tri §4, piles construites du fond vers l'avant |
| C. Densité maximale | Tri §4 et variantes intra-zone, score §5 |

Plusieurs unités : **remplissage séquentiel** (unités pleines, puis une unité reliquat). C'est la pratique de préparation
(moins de manipulations, palettes pleines expédiées directement) ; le rééquilibrage entre unités est une évolution.

---

## 8. Classement et recommandation

```
1. Solutions conformes, puis moins de produits non placés, puis moins d'unités (inchangé)
2. Score = 0,35 × remplissage + 0,30 × stabilité + 0,20 × ordre + 0,15 × homogénéité
   stabilité = support moyen × (1 − décalage du CdG)
   ordre     = part des appuis « dans le bon sens » (§9)
```

---

## 9. Indicateurs nouveaux

| Indicateur | Définition |
|---|---|
| **Ordre lourd / léger respecté** | Part des contacts d'appui où l'article du dessus est moins lourd que celui du dessous, ou repose sur un article rigide de capacité suffisante |
| **Capacité utilisée maxi** | Plus forte charge reçue / capacité, sur tous les articles (100 % = limite) |
| **Poids par palette physique** | Charge rapportée à chaque palette de la base |

Ils figurent dans le récapitulatif et la fiche imprimée.

---

## 10. Ce que voit l'utilisateur

- **Fiche article** : « Profil de gerbage » en lecture seule : classe, zone conseillée, capacité (saisie ou déduite, avec le calcul).
- **Conditionnement hétérogène** : chaque ligne affiche zone, capacité et sous-total de poids ; total de la commande et
  rappel de la charge admissible.
- **Ordre de pose** : liste numérotée (article, couche, position) à suivre par le préparateur.

---

## 11. Cas de contrôle (tests automatiques)

| Cas | Attendu |
|---|---|
| Sacs 25 kg + cartons légers | Tous les sacs sous les cartons |
| Cartons + articles fragiles | Aucun article sur un fragile ; fragiles en zone haute |
| Fût 220 kg + cartons | Le fût n'est jamais posé sur un carton (capacité dépassée) |
| Plaque + cartons | Une plaque n'est posée hors palette que sur ≥ 95 % d'appui |
| Tubes couchés + cartons | Rien d'autre que des tubes sur les tubes |
| Cartons de poids très différents | Ordre lourd / léger respecté ≥ 95 % |
| Profils | Capacités déduites conformes au tableau §3.2 |

---

## 12. Limites v0.0.4

- Pas de compatibilité de produits (alimentaire / chimique, matières dangereuses) ni d'ordre de livraison multi-arrêts.
- Pas de rééquilibrage automatique entre plusieurs unités (§7).
- La capacité déduite est une estimation prudente : pour un article critique, renseigner la charge maxi sur le dessus.

---

## 13. Références

- Bischoff, E.E. (2006). Three-dimensional packing of items with limited load bearing strength. *EJOR*, 168(3), 952–966.
- Bischoff, E.E., Ratcliff, M.S.W. (1995). Issues in the development of approaches to container loading. *Omega*, 23(4), 377–390.
- Bortfeldt, A., Wäscher, G. (2013). Constraints in container loading – A state-of-the-art review. *EJOR*, 229(1), 1–20.
- Crainic, T.G., Perboli, G., Tadei, R. (2008). Extreme point-based heuristics for three-dimensional bin packing. *INFORMS JOC*, 20(3), 368–384.
- Davies, A.P., Bischoff, E.E. (1999). Weight distribution considerations in container loading. *EJOR*, 114(3), 509–527.
- Elhedhli, S., Gzara, F., Yildiz, B. (2019). Three-dimensional bin packing and mixed-case palletization. *INFORMS Journal on Optimization*, 1(4), 323–352.
- Gzara, F., Elhedhli, S., Yildiz, B.C. (2020). The pallet loading problem: Three-dimensional bin packing with practical constraints. *EJOR*, 287(3), 1062–1074.
- Junqueira, L., Morabito, R., Yamashita, D.S. (2012). Three-dimensional container loading models with cargo stability and load bearing constraints. *Computers & OR*, 39, 74–85.
- Kellicutt, K.Q. (1963). Effect of contents and load bearing surface on compressive strength and stacking life of corrugated containers. *TAPPI*, 46(1).
- Ramos, A.G., Oliveira, J.F., Gonçalves, J.F., Lopes, M.P. (2016). A container loading algorithm with static mechanical equilibrium stability constraints. *Transportation Research Part B*, 91, 565–581.
- Schuster, M., Bormann, R., Steidl, D., Reynolds-Haertle, S., Stilman, M. (2010). Stable stacking for the distributor's palletizing problem. *IEEE/RSJ IROS*.
