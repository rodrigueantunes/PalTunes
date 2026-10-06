using System.Globalization;
using PalTunes.Core.Models;
using PalTunes.Core.Validation;

namespace PalTunes.Core.Engine;

/// <summary>
/// Palettisation hétérogène (multi-articles), étude §7 et §11.3 : trois stratégies constructives (couches homogènes
/// puis couche mixte, piles par article, densité maximale par points extrêmes), contraintes vérifiées à chaque pose
/// (inclusion, chevauchement, support, charge propagée, poids, couches maxi), ouverture d'unités supplémentaires.
/// </summary>
public static class HeterogeneousEngine
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static EngineResult Solve(IReadOnlyList<(Article Article, int Quantity)> lines, BaseInfo baseInfo, PackagingConstraints c)
    {
        var result = new EngineResult();
        var valid = new List<(Article, int)>();
        foreach (var (article, qty) in lines)
        {
            if (qty <= 0)
            {
                continue;
            }

            var errors = ArticleSchema.Validate(article);
            if (errors.Count > 0)
            {
                result.Messages.Add($"Article {article.Code} ignoré (incomplet) : " + string.Join(" ", errors));
                continue;
            }

            valid.Add((article.ForPalletizing(), qty)); // carton plié : hauteur pliée
        }

        if (valid.Count == 0)
        {
            result.Messages.Add("Aucune ligne calculable : ajoutez des articles complets avec une quantité.");
            return result;
        }

        var ctx = new Ctx(baseInfo, c);
        if (ctx.MaxZ <= 0)
        {
            result.Messages.Add("Hauteur utile nulle : la hauteur maximale est inférieure à la palette + coiffe.");
            return result;
        }

        // Articles qui ne tiennent pas seuls sur la base (dimensions, hauteur, poids) : exclus, la meilleure solution est
        // calculée avec les autres et l'exclusion est signalée.
        var excluded = new List<string>();
        var feasible = new List<(Article Article, int Quantity)>();
        foreach (var (article, qty) in valid)
        {
            if (Unfit(article, ctx) is { } reason)
            {
                excluded.Add($"{article.Code} × {qty.ToString("#,0", Fr)} : {reason}");
            }
            else
            {
                feasible.Add((article, qty));
            }
        }

        if (feasible.Count == 0)
        {
            result.Messages.Add("Aucun article ne tient sur cette base : " + string.Join(" ; ", excluded) + ".");
            return result;
        }

        // Palettes complètes mono-article (quantité au moins égale à une palette pleine) : posées telles quelles et
        // communes aux trois stratégies ; seul le reliquat de chaque article est mélangé. Une palette « pleine » d'un
        // article peut pourtant laisser de la place à un autre (bande libre le long d'un côté) : la variante tout mélangé
        // est aussi calculée tant que la pose un à un le permet, et pour chaque stratégie la variante qui demande le moins
        // d'unités est retenue.
        var requested = feasible.Sum(f => f.Quantity);
        var withFull = Variant(feasible, ctx, c, requested, extractFull: true, out var fullCount, result.Messages);
        var chosen = withFull;
        if (fullCount > 0 && requested <= MaxItemsPlacedOneByOne)
        {
            var mixed = Variant(feasible, ctx, c, requested, extractFull: false, out _, []);
            chosen = withFull.Select(w =>
            {
                var m = mixed.FirstOrDefault(x => x.Strategy == w.Strategy);
                return m != null && m.IsCompliant && m.UnplacedItems <= w.UnplacedItems && m.Units.Count < w.Units.Count ? m : w;
            }).ToList();
        }

        foreach (var s in chosen)
        {
            foreach (var e in excluded)
            {
                s.Warnings.Add("Article exclu (ne tient pas sur la base) : " + e);
            }

            s.ExcludedArticles = [.. excluded];
            result.Solutions.Add(s);
        }

        Rank(result.Solutions);
        var hollow = valid.GroupBy(v => v.Item1.Id).ToDictionary(g => g.Key, g => g.First().Item1.HollowDiameter);
        HomogeneousEngine.MarkHollow(result.Solutions, id => hollow.GetValueOrDefault(id));
        return result;
    }

    /// <summary>
    /// Une variante de calcul : palettes complètes mono-article extraites d'abord (<paramref name="extractFull"/>) ou
    /// tout mélangé ; les trois stratégies, en parallèle, chacune terminée (contrôle, indicateurs).
    /// </summary>
    private static List<Solution> Variant(List<(Article Article, int Quantity)> feasible, Ctx ctx, PackagingConstraints c, int requested,
        bool extractFull, out int fullCount, List<string> messages)
    {
        var full = new List<LoadUnit>();
        var demand = new List<Demand>();
        foreach (var (article, qty) in feasible)
        {
            var (units, rest) = extractFull ? FullPallets(article, qty, ctx) : ([], qty);
            full.AddRange(units);
            if (rest > 0)
            {
                demand.Add(new Demand(Proto(article, c, 0), rest));
            }
        }

        for (var i = 0; i < full.Count; i++)
        {
            full[i].Index = i + 1;
        }

        fullCount = full.Count;
        var mixedCount = demand.Sum(d => d.Count);
        var runs = new List<Func<Solution>> { () => Layered(demand, ctx) };
        if (mixedCount <= MaxItemsPlacedOneByOne)
        {
            // Ordre de pose de l'étude hétérogène §4, appliqué aux stratégies par points extrêmes.
            var items = Expand(demand);
            runs.Add(() => Packed(SortForPiles(items), ctx, Priority.Walls, MixedStrategy.PilesParArticle));
            runs.Add(() => BestOf(ctx, Priority.BottomLeft, MixedStrategy.DensiteMaximale,
                ByStudy(items), SortBy(items, i => -i.Volume), SortBy(items, i => -i.BaseArea), SortBy(items, i => -i.Article.Weight)));
        }
        else
        {
            messages.Add($"{mixedCount.ToString("#,0", Fr)} produits à mélanger : seule la stratégie « Couches homogènes » est calculée " +
                         $"(les stratégies « Piles par article » et « Densité maximale » posent les produits un à un, jusqu'à {MaxItemsPlacedOneByOne.ToString("#,0", Fr)}).");
        }

        var solutions = new Solution[runs.Count];
        Parallel.For(0, runs.Count, i => solutions[i] = runs[i]());
        foreach (var s in solutions)
        {
            // Palettes complètes en tête, puis les unités mélangées.
            foreach (var u in s.Units)
            {
                u.Index += full.Count;
            }

            s.Units.InsertRange(0, full);
            Finish(s, ctx, requested);
            if (full.Count > 0)
            {
                s.Description = $"{full.Count} palette(s) complète(s) mono-article, puis : " + s.Description;
            }
        }

        return [.. solutions];
    }

    /// <summary>
    /// Au-delà, les stratégies par points extrêmes (pose un à un) ne sont pas lancées : « Couches homogènes » traite les
    /// quantités par couches entières (100 000 produits et plus).
    /// </summary>
    public const int MaxItemsPlacedOneByOne = 20000;

    /// <summary>Raison pour laquelle un article ne peut pas être posé seul sur la base vide ; null s'il tient.</summary>
    private static string? Unfit(Article a, Ctx ctx)
    {
        if (a.Weight > ctx.MaxWeight + 1e-9)
        {
            return $"poids unitaire {a.Weight.ToString("0.#####", Fr)} kg > charge maxi {ctx.MaxWeight.ToString("0.#", Fr)} kg";
        }

        var x = ctx.MaxX - ctx.MinX;
        var y = ctx.MaxY - ctx.MinY;
        if (Orientations(a, ctx.C).Any(o => o.DX <= x + 1e-6 && o.DY <= y + 1e-6 && o.DZ <= ctx.MaxZ + 1e-6))
        {
            return null;
        }

        return $"{a.DimensionsText} mm ne tient pas sur la surface utile {x.ToString("0", Fr)} × {y.ToString("0", Fr)} mm " +
               $"avec la hauteur utile {ctx.MaxZ.ToString("0", Fr)} mm (dans aucune orientation autorisée)";
    }

    /// <summary>
    /// Palettes pleines d'un seul article (meilleure solution homogène sur la même base) tant que la quantité le permet.
    /// Les unités partagent la même liste de produits (palettes identiques).
    /// </summary>
    private static (List<LoadUnit> Units, int Remaining) FullPallets(Article a, int qty, Ctx ctx)
    {
        // Filtre peu coûteux : une palette pleine contient au plus la borne ci-dessous.
        var orients = Orientations(a, ctx.C);
        var area = (ctx.MaxX - ctx.MinX) * (ctx.MaxY - ctx.MinY);
        var geometric = orients.Max(o => Math.Floor(area / Math.Max(1, o.DX * o.DY)) * Math.Floor(ctx.MaxZ / Math.Max(1, o.DZ)));
        var bound = Math.Min(geometric, Math.Floor(ctx.MaxWeight / Math.Max(1e-9, a.Weight)));
        if (qty < Math.Max(1, bound * 0.5))
        {
            return ([], qty);
        }

        var best = HomogeneousEngine.Solve(a, ctx.Base, ctx.C).Recommended;
        if (best is not { IsCompliant: true, FirstUnit: { Items.Count: > 0 } template } || qty < template.Items.Count)
        {
            return ([], qty);
        }

        var per = template.Items.Count;
        var units = new List<LoadUnit>();
        for (var k = 0; k < qty / per; k++)
        {
            units.Add(new LoadUnit
            {
                Items = template.Items,
                Layers = template.Layers,
                Metrics = template.Metrics,
                CornerFrame = template.CornerFrame,
                RemovedForCorners = template.RemovedForCorners,
                IsFullPallet = true
            });
        }

        return (units, qty % per);
    }

    /// <summary>Quantité restante d'un article et son prototype (produits identiques).</summary>
    private sealed record Demand(Item Proto, int Count);

    private static Item Proto(Article article, PackagingConstraints c, int index) =>
        new(article, Orientations(article, c), index, StackingProfile.For(article, c.ForcedAxis),
            StackingProfile.For(article, CoilAxis.Vertical), StackingProfile.For(article, CoilAxis.Horizontal));

    /// <summary>Produits un à un (stratégies par points extrêmes, reliquats) : copies du prototype.</summary>
    private static List<Item> Expand(IEnumerable<Demand> demand)
    {
        var items = new List<Item>();
        foreach (var d in demand)
        {
            for (var i = 0; i < d.Count; i++)
            {
                items.Add(d.Proto.Copy(items.Count));
            }
        }

        return items;
    }

    // ------------------------------------------------------------------ Données

    private sealed class Ctx(BaseInfo b, PackagingConstraints c)
    {
        public BaseInfo Base { get; } = b;
        public PackagingConstraints C { get; } = c;
        public double MinX { get; } = c.CornerInset - c.OverhangLength;
        public double MinY { get; } = c.CornerInset - c.OverhangWidth;
        public double MaxX { get; } = b.Length + c.OverhangLength - c.CornerInset;
        public double MaxY { get; } = b.Width + c.OverhangWidth - c.CornerInset;
        public double MaxZ { get; } = c.MaxTotalHeight - b.PalletHeight - c.CapHeight;
        public double MaxWeight { get; } = c.MaxLoadWeight > 0 ? c.MaxLoadWeight : b.DynamicCapacity > 0 ? b.DynamicCapacity : double.MaxValue;
        public double MinSupport { get; } = c.MinSupportPercent / 100;
    }

    private sealed record Orient(double DX, double DY, double DZ, ShapeKind Shape);

    private sealed class Item(Article article, List<Orient> orientations, int index, StackingProfile profile, StackingProfile upright, StackingProfile lying)
    {
        public Item Copy(int newIndex) => new(Article, Orientations, newIndex, Profile, upright, lying);

        public Article Article { get; } = article;
        public List<Orient> Orientations { get; } = orientations;
        public int Index { get; } = index;

        /// <summary>Profil de gerbage (étude hétérogène §3) pour le tri.</summary>
        public StackingProfile Profile { get; } = profile;

        public double Volume => Article.Volume;
        public double BaseArea => Orientations.Max(o => o.DX * o.DY);
        public double MaxHeight => Orientations.Max(o => o.DZ);

        /// <summary>Comportement réel selon l'orientation posée : un tube debout est rigide, couché il roule.</summary>
        public StackingProfile ProfileFor(ShapeKind shape) => shape switch
        {
            ShapeKind.CylinderX or ShapeKind.CylinderY => lying,
            ShapeKind.CylinderZ when Article.Kind is ArticleKind.Tube or ArticleKind.Bobine => upright,
            _ => Profile
        };

        /// <summary>Capacité portante absolue : ce qui porte le plus passe dessous (étude hétérogène §4).</summary>
        public double StrengthKey => Math.Min(Profile.Capacity, 1e6);
    }

    private static List<Orient> Orientations(Article a, PackagingConstraints c)
    {
        var list = new List<Orient>();
        switch (a.Kind)
        {
            case ArticleKind.Bobine:
            case ArticleKind.Tube:
                var axis = c.ForcedAxis ?? a.CoilAxis;
                var d = a.Diameter;
                var len = a.AxisLength;
                if (axis != CoilAxis.Horizontal)
                {
                    list.Add(new Orient(d, d, len, ShapeKind.CylinderZ));
                }

                if (axis != CoilAxis.Vertical)
                {
                    list.Add(new Orient(len, d, d, ShapeKind.CylinderX));
                    list.Add(new Orient(d, len, d, ShapeKind.CylinderY));
                }

                break;
            case ArticleKind.Fut:
                list.Add(new Orient(a.Diameter, a.Diameter, a.Height, ShapeKind.CylinderZ));
                break;
            default:
                list.Add(new Orient(a.Length, a.Width, a.Height, ShapeKind.Box));
                list.Add(new Orient(a.Width, a.Length, a.Height, ShapeKind.Box));
                if (a.Orientation == OrientationRule.Libre && ArticleSchema.UsesOrientation(a.Kind))
                {
                    list.Add(new Orient(a.Length, a.Height, a.Width, ShapeKind.Box));
                    list.Add(new Orient(a.Height, a.Length, a.Width, ShapeKind.Box));
                    list.Add(new Orient(a.Width, a.Height, a.Length, ShapeKind.Box));
                    list.Add(new Orient(a.Height, a.Width, a.Length, ShapeKind.Box));
                }

                break;
        }

        return list.DistinctBy(o => (Math.Round(o.DX, 3), Math.Round(o.DY, 3), Math.Round(o.DZ, 3), o.Shape)).ToList();
    }

    /// <summary>
    /// Ordre de pose de l'étude hétérogène §4 : zone (bas → haut), résistance relative, poids, surface au sol,
    /// hauteur (couches de niveau), article (regroupement).
    /// </summary>
    private static List<Item> ByStudy(IEnumerable<Item> items) =>
        items.OrderBy(i => (int)i.Profile.Zone)
            .ThenByDescending(i => i.StrengthKey)
            .ThenByDescending(i => i.Article.Weight)
            .ThenByDescending(i => Math.Round(i.BaseArea))
            .ThenBy(i => Math.Round(i.MaxHeight))
            .ThenBy(i => i.Article.Code)
            .ThenBy(i => i.Index).ToList();

    /// <summary>Variante de tri à l'intérieur de chaque zone : jamais un article « haut » sous un article « bas ».</summary>
    private static List<Item> SortBy(List<Item> items, Func<Item, double> key) =>
        items.OrderBy(i => (int)i.Profile.Zone).ThenBy(key).ThenByDescending(i => i.StrengthKey)
            .ThenBy(i => i.Article.Code).ThenBy(i => i.Index).ToList();

    private static List<Item> SortForPiles(List<Item> items) =>
        items.OrderBy(i => (int)i.Profile.Zone)
            .ThenByDescending(i => i.StrengthKey)
            .ThenByDescending(i => i.Article.Weight)
            .ThenBy(i => i.Article.Code)
            .ThenBy(i => i.Index).ToList();

    // ------------------------------------------------------------------ Stratégie A : couches homogènes

    private static Solution Layered(List<Demand> demand, Ctx ctx)
    {
        // Par quantités : une couche complète consomme autant de produits que de positions, sans les poser un à un.
        var solution = NewSolution(ctx, MixedStrategy.CouchesHomogenes);
        var left = demand.ToDictionary(d => d.Proto.Article.Id, d => d.Count);
        var protos = demand.ToDictionary(d => d.Proto.Article.Id, d => d.Proto);
        var plans = new Dictionary<Guid, (List<Rect2> rects, double h, List<ShapeKind> shapes)?>();
        var unplaced = 0;
        while (left.Values.Any(n => n > 0))
        {
            var packer = new UnitPacker(ctx, Priority.BottomLeft, protos.Values.Where(p => left[p.Article.Id] > 0));
            var layerRefused = new HashSet<Guid>();
            var articles = protos.Values.Where(p => left[p.Article.Id] > 0)
                .OrderBy(i => (int)i.Profile.Zone)
                .ThenByDescending(i => i.StrengthKey)
                .ThenByDescending(i => i.Article.Weight / Math.Max(1, FootprintArea(i.Article)))
                .ToList();
            double z0 = 0;
            foreach (var proto in articles)
            {
                var article = proto.Article;
                if (!plans.TryGetValue(article.Id, out var plan))
                {
                    plans[article.Id] = plan = BestLayer(article, ctx);
                }

                if (plan == null)
                {
                    continue;
                }

                var (rects, h, shapes) = plan.Value;
                var sx = ctx.C.CenterLoad ? (ctx.MaxX - ctx.MinX - rects.Max(r => r.Right)) / 2 : 0;
                var sy = ctx.C.CenterLoad ? (ctx.MaxY - ctx.MinY - rects.Max(r => r.Top)) / 2 : 0;
                while (left[article.Id] >= rects.Count && z0 + h <= ctx.MaxZ + 1e-6)
                {
                    var layer = rects.Select((r, k) => (proto, new Placement
                    {
                        ArticleId = article.Id,
                        X = ctx.MinX + sx + r.X,
                        Y = ctx.MinY + sy + r.Y,
                        Z = z0,
                        DX = r.W,
                        DY = r.H,
                        DZ = h,
                        Shape = shapes[k],
                        Weight = article.Weight
                    })).ToList();
                    if (!packer.PlaceLayer(layer))
                    {
                        layerRefused.Add(article.Id);
                        break;
                    }

                    left[article.Id] -= rects.Count;
                    z0 += h;
                }
            }

            // Reliquat (moins d'une couche complète par article) : couche(s) mixte(s) sur le dessus, en points extrêmes. Les
            // articles qui ont encore des couches complètes à poser attendent l'unité suivante.
            // Couche complète refusée (capacité, poids) ou sans plan de couche : pose produit par produit, au plus une couche
            // (ou le plafond des poses une à une) par unité.
            var rest = Expand(protos.Values.Where(p => left[p.Article.Id] > 0).Select(p =>
            {
                var n = left[p.Article.Id];
                var take = plans[p.Article.Id] is not { } pl ? Math.Min(n, MaxItemsPlacedOneByOne)
                    : n < pl.rects.Count ? n
                    : layerRefused.Contains(p.Article.Id) ? Math.Min(n, pl.rects.Count)
                    : 0;
                return new Demand(p, take);
            }).Where(d => d.Count > 0));
            foreach (var item in ByStudy(rest))
            {
                if (packer.TryPlace(item))
                {
                    left[item.Article.Id]--;
                }
            }

            if (packer.Count == 0)
            {
                unplaced = left.Values.Sum();
                break;
            }

            solution.Units.Add(packer.ToUnit(solution.Units.Count + 1));
        }

        solution.UnplacedItems = unplaced;
        return solution;
    }

    private static double FootprintArea(Article a) => a.IsCylinder ? a.Diameter * a.Diameter : a.Length * a.Width;

    /// <summary>
    /// Meilleure couche complète d'un article sur la surface utile (plans du §4). Tubes et bobines : debout (maille
    /// circulaire) ou couchés (lits carrés), selon l'axe autorisé, la disposition qui pose le plus de produits par mm de
    /// hauteur l'emporte.
    /// </summary>
    private static (List<Rect2> rects, double h, List<ShapeKind> shapes)? BestLayer(Article a, Ctx ctx)
    {
        var x = ctx.MaxX - ctx.MinX;
        var y = ctx.MaxY - ctx.MinY;
        if (a.IsCylinder)
        {
            var axis = a.Kind == ArticleKind.Fut ? CoilAxis.Vertical : ctx.C.ForcedAxis ?? a.CoilAxis;
            (List<Rect2>, double, List<ShapeKind>)? best = null;
            double bestDensity = 0;
            if (axis != CoilAxis.Horizontal && a.AxisLength <= ctx.MaxZ + 1e-6)
            {
                var circles = CircleLayerSolver.Solve(x, y, a.Diameter, ctx.C.Gap).Best;
                if (circles.Count > 0)
                {
                    best = (circles.Items, a.AxisLength, Enumerable.Repeat(ShapeKind.CylinderZ, circles.Count).ToList());
                    bestDensity = circles.Count / a.AxisLength;
                }
            }

            if (axis != CoilAxis.Vertical && a.Diameter <= ctx.MaxZ + 1e-6)
            {
                var lying = RectLayerSolver.Solve(x, y, a.AxisLength, a.Diameter, ctx.C.Gap).Best;
                if (lying.Count > 0 && lying.Count / a.Diameter > bestDensity * 1.0001)
                {
                    var shapes = lying.Items.Select(r => Math.Abs(r.W - a.AxisLength) < 1e-6 && Math.Abs(r.H - a.AxisLength) > 1e-6
                        ? ShapeKind.CylinderX
                        : Math.Abs(r.H - a.AxisLength) < 1e-6 ? ShapeKind.CylinderY : ShapeKind.CylinderX).ToList();
                    best = (lying.Items, a.Diameter, shapes);
                }
            }

            return best;
        }

        var options = new List<(double a, double b, double h)> { (a.Length, a.Width, a.Height) };
        if (a.Orientation == OrientationRule.Libre && ArticleSchema.UsesOrientation(a.Kind))
        {
            options.Add((a.Length, a.Height, a.Width));
            options.Add((a.Width, a.Height, a.Length));
        }

        (List<Rect2>, double, List<ShapeKind>)? bestBox = null;
        double bestCoverage = 0;
        foreach (var (oa, ob, oh) in options)
        {
            if (oh > ctx.MaxZ)
            {
                continue;
            }

            var layer = RectLayerSolver.Solve(x, y, oa, ob, ctx.C.Gap).Best;
            var coverage = layer.Count * oa * ob;
            if (layer.Count > 0 && coverage > bestCoverage + 1e-6)
            {
                bestCoverage = coverage;
                bestBox = (layer.Items, oh, Enumerable.Repeat(ShapeKind.Box, layer.Count).ToList());
            }
        }

        return bestBox;
    }

    // ------------------------------------------------------------------ Stratégies B / C : points extrêmes

    private static Solution BestOf(Ctx ctx, Priority priority, MixedStrategy strategy, params List<Item>[] orders)
    {
        Solution? best = null;
        foreach (var order in orders)
        {
            var s = Packed(order, ctx, priority, strategy);
            if (best == null || Compare(s, best) < 0)
            {
                best = s;
            }

            if (order.Count > 400)
            {
                break;
            }
        }

        return Intensify(best!, ctx, strategy, orders[0]);
    }

    /// <summary>Recherche élargie : nombre d'ordres de pose essayés au plus, et budget de temps (commandes courantes).</summary>
    private const int IntensifyOrders = 120;

    private const int IntensifyMilliseconds = 1500;

    /// <summary>
    /// Recherche élargie (commandes de 400 produits au plus) : d'autres ordres de pose — articles dans un autre ordre,
    /// volumes légèrement perturbés — avec les deux règles de pose, tant que la borne de volume n'est pas atteinte et dans
    /// la limite du budget de temps. Tirages à graine fixe : même commande, même résultat.
    /// </summary>
    private static Solution Intensify(Solution best, Ctx ctx, MixedStrategy strategy, List<Item> all)
    {
        if (all.Count == 0 || all.Count > 400 || best.UnplacedItems > 0)
        {
            return best;
        }

        var usable = (ctx.MaxX - ctx.MinX) * (ctx.MaxY - ctx.MinY) * ctx.MaxZ;
        var bound = usable <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(all.Sum(i => i.Volume) / usable - 1e-9));
        if (best.Units.Count <= bound)
        {
            return best;
        }

        var rnd = new Random(all.Count * 7919 + all.Select(i => i.Article.Code.Length).Sum());
        var groups = all.GroupBy(i => i.Article.Id).Select(g => g.ToList()).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var k = 0; k < IntensifyOrders && best.Units.Count > bound && sw.ElapsedMilliseconds < IntensifyMilliseconds; k++)
        {
            var order = k % 2 == 0
                ? groups.OrderBy(_ => rnd.Next()).SelectMany(g => g).ToList()
                : all.OrderByDescending(i => i.Volume * (0.7 + 0.6 * rnd.NextDouble())).ToList();
            foreach (var priority in new[] { Priority.BottomLeft, Priority.Walls })
            {
                var s = Packed(order, ctx, priority, strategy);
                if (Compare(s, best) < 0)
                {
                    best = s;
                }
            }
        }

        return best;
    }

    private static int Compare(Solution a, Solution b)
    {
        if (a.UnplacedItems != b.UnplacedItems)
        {
            return a.UnplacedItems.CompareTo(b.UnplacedItems);
        }

        if (a.Units.Count != b.Units.Count)
        {
            return a.Units.Count.CompareTo(b.Units.Count);
        }

        var ha = a.Units.Sum(u => u.Items.Count == 0 ? 0 : u.Items.Max(p => p.MaxZ));
        var hb = b.Units.Sum(u => u.Items.Count == 0 ? 0 : u.Items.Max(p => p.MaxZ));
        return ha.CompareTo(hb);
    }

    private static Solution Packed(List<Item> order, Ctx ctx, Priority priority, MixedStrategy strategy)
    {
        var solution = NewSolution(ctx, strategy);
        var remaining = order.ToList();
        while (remaining.Count > 0)
        {
            var packer = new UnitPacker(ctx, priority, remaining.DistinctBy(i => i.Article.Id));
            var next = new List<Item>(remaining.Count);
            foreach (var item in remaining)
            {
                if (!packer.TryPlace(item))
                {
                    next.Add(item);
                }
            }

            if (packer.Count == 0)
            {
                break;
            }

            remaining = next;
            solution.Units.Add(packer.ToUnit(solution.Units.Count + 1));
        }

        solution.UnplacedItems = remaining.Count;
        return solution;
    }

    private enum Priority
    {
        /// <summary>Bas → fond → gauche : construction par niveaux, densité.</summary>
        BottomLeft,

        /// <summary>Fond → gauche → bas : piles verticales, murs successifs (George &amp; Robinson).</summary>
        Walls
    }

    /// <summary>Remplissage d'une unité de charge par points extrêmes (Crainic, Perboli &amp; Tadei, 2008).</summary>
    private sealed class UnitPacker
    {
        private const double Cell = 100;
        private readonly Ctx _ctx;
        private readonly Priority _priority;
        private readonly List<Box> _boxes = [];
        /// <summary>Grille 3D des produits posés (mailles de 100 mm, hauteur comprise) : chaque contrôle ne voit que ses voisins.</summary>
        private readonly Dictionary<(int, int, int), List<Box>> _grid = [];

        /// <summary>Points extrêmes tenus triés dans l'ordre de la priorité (pas de tri à chaque pose).</summary>
        private readonly SortedSet<(double X, double Y, double Z)> _eps;

        /// <summary>Cotes des dessus déjà posés (peu nombreuses) : couche plane (R4).</summary>
        private readonly HashSet<double> _tops = [];

        /// <summary>Article refusé et nombre de poses à ce moment : un produit identique sera refusé tant que rien ne change.</summary>
        private readonly Dictionary<Guid, int> _failedAt = [];

        private double _weight;
        private double _sumX;
        private double _sumY;

        /// <summary>Plus petite empreinte et plus petite hauteur des produits à poser : un point extrême plus étroit est inutile.</summary>
        private readonly double _minFoot;
        private readonly double _minHeight;

        public UnitPacker(Ctx ctx, Priority priority, IEnumerable<Item>? kinds = null)
        {
            _ctx = ctx;
            _priority = priority;
            var orients = kinds?.SelectMany(k => k.Orientations).ToList() ?? [];
            _minFoot = orients.Count == 0 ? 0 : orients.Min(o => Math.Min(o.DX, o.DY));
            _minHeight = orients.Count == 0 ? 0 : orients.Min(o => o.DZ);
            _eps = new SortedSet<(double X, double Y, double Z)>(Comparer<(double X, double Y, double Z)>.Create(priority == Priority.Walls
                ? (a, b) => (Math.Round(a.X), Math.Round(a.Y), a.Z, a.X, a.Y).CompareTo((Math.Round(b.X), Math.Round(b.Y), b.Z, b.X, b.Y))
                : (a, b) => (Math.Round(a.Z), Math.Round(a.X), a.Y, a.Z, a.X).CompareTo((Math.Round(b.Z), Math.Round(b.X), b.Y, b.Z, b.X))));
            _eps.Add((ctx.MinX, ctx.MinY, 0));
        }

        public int Count => _boxes.Count;

        private sealed class Box(Placement p, Item item)
        {
            public Placement P { get; } = p;
            public Item Item { get; } = item;
            public StackingProfile Profile { get; } = item.ProfileFor(p.Shape);

            /// <summary>Capacité pour les autres articles ; un cylindre couché ne porte que ses semblables (pas de limite entre eux).</summary>
            public double Cap => Profile.SameArticleOnTopOnly ? double.MaxValue : Profile.Capacity;

            public double CapAligned => Profile.SameArticleOnTopOnly ? double.MaxValue : Profile.AlignedCapacity;

            /// <summary>Au moins une charge non alignée est arrivée sur ce produit : la capacité « mélange » s'applique.</summary>
            public bool MixedLoaded { get; set; }

            public double CapFor(bool mixed) => mixed || MixedLoaded ? Cap : CapAligned;

            /// <summary>Charge reçue (calculée exactement en fin d'unité, pour les indicateurs).</summary>
            public double Load { get; set; }

            /// <summary>Colonne du produit : pile de produits alignés posés exactement les uns sur les autres.</summary>
            public Column Col { get; set; } = null!;

            public int SameLevel { get; set; } = 1;
            public List<(Box Box, double Fraction)> Supporters { get; } = [];
        }

        /// <summary>
        /// Colonne de produits alignés (même empreinte, posés exactement les uns sur les autres). La charge n'y entre que
        /// par le haut et atteint tous ses produits : la plus petite marge (capacité − charge) suffit pour contrôler la
        /// colonne entière en une opération, au lieu de la parcourir produit par produit.
        /// </summary>
        private sealed class Column(Box bottom)
        {
            public Box Bottom { get; } = bottom;
            public Box Top { get; set; } = bottom;

            /// <summary>Produits déjà chargés « en mélange » : plus petite marge sur la capacité « mélange ».</summary>
            public double PreMin = double.MaxValue;

            /// <summary>Autres produits : plus petite marge sur la capacité « colonne » et sur la capacité « mélange ».</summary>
            public double SufMinA = double.MaxValue;

            public double SufMinM = double.MaxValue;

            // Charge attendue pendant un contrôle.
            public int Stamp;
            public double PendingLoad;
            public bool PendingMixed;

            public double MinSlack(bool mixed) => Math.Min(PreMin, mixed ? SufMinM : SufMinA);

            public void Apply(double load, bool mixed)
            {
                PreMin -= load;
                SufMinA -= load;
                SufMinM -= load;
                if (mixed)
                {
                    PreMin = Math.Min(PreMin, SufMinM);
                    SufMinA = SufMinM = double.MaxValue;
                }
            }

            public void Push(Box box)
            {
                Top = box;
                SufMinA = Math.Min(SufMinA, box.CapAligned);
                SufMinM = Math.Min(SufMinM, box.Cap);
            }
        }

        public bool TryPlace(Item item)
        {
            if (_weight + item.Article.Weight > _ctx.MaxWeight + 1e-9)
            {
                return false;
            }

            // Un produit identique vient d'être refusé et rien n'a été posé depuis : refus immédiat.
            if (_failedAt.TryGetValue(item.Article.Id, out var at) && at == _boxes.Count)
            {
                return false;
            }

            if (Place(item))
            {
                return true;
            }

            _failedAt[item.Article.Id] = _boxes.Count;
            return false;
        }

        /// <summary>
        /// Choix de la position sur critères géométriques (place, chevauchement, appui), puis contrôle de la charge
        /// propagée sur les seules positions retenues, dans l'ordre de préférence ; un seul parcours des points extrêmes.
        /// </summary>
        private bool Place(Item item) => _priority == Priority.Walls ? PlaceWalls(item) : PlaceBottomLeft(item);

        private bool TryCommit(Item item, Placement p, Geo geo)
        {
            if (Loads([(p, geo.Supporters)]) is not { } loads)
            {
                return false;
            }

            ApplyLoads(loads);
            Commit(p, item, geo);
            UpdatePoints(p);
            return true;
        }

        /// <summary>
        /// Cylindres debout dont le dessus est libre (un cylindre debout centré dessus est pleinement appuyé), tenus triés :
        /// cote la plus basse, puis plus près du coin fond-gauche.
        /// </summary>
        private readonly SortedSet<Box> _freeTops = new(Comparer<Box>.Create((a, b) =>
            (Math.Round(a.P.MaxZ), Math.Round(a.P.X + a.P.Y), a.P.X, a.P.Y, a.P.Sequence)
                .CompareTo((Math.Round(b.P.MaxZ), Math.Round(b.P.X + b.P.Y), b.P.X, b.P.Y, b.P.Sequence))));

        /// <summary>Dessus libres proposés à chaque pose (les plus bas, les plus au fond) : borne le coût des grandes couches.</summary>
        private const int TopCandidates = 48;

        /// <summary>
        /// Positions à essayer, dans l'ordre de la priorité : points extrêmes (toutes orientations) et, pour un cylindre
        /// debout, le centre de chaque dessus libre de cylindre debout (couches en quinconce : les coins des enveloppes ne
        /// tombent pas sur les produits du dessous).
        /// </summary>
        private List<(double X, double Y, double Z, int Orientation)> Points(Item item)
        {
            var tops = new List<(double X, double Y, double Z, int Orientation)>();
            for (var oi = 0; oi < item.Orientations.Count; oi++)
            {
                var o = item.Orientations[oi];
                if (o.Shape != ShapeKind.CylinderZ)
                {
                    continue;
                }

                foreach (var b in _freeTops.Take(TopCandidates))
                {
                    tops.Add((Math.Round(b.P.X + b.P.DX / 2 - o.DX / 2, 3), Math.Round(b.P.Y + b.P.DY / 2 - o.DY / 2, 3), Math.Round(b.P.MaxZ, 3), oi));
                }
            }

            if (tops.Count == 0)
            {
                return _eps.Select(e => (e.X, e.Y, e.Z, -1)).ToList();
            }

            // Fusion de deux suites triées (points extrêmes, dessus libres) dans l'ordre de la priorité.
            int Cmp((double X, double Y, double Z, int) a, (double X, double Y, double Z, int) b) => _eps.Comparer.Compare((a.X, a.Y, a.Z), (b.X, b.Y, b.Z));
            tops.Sort(Cmp);
            var points = new List<(double X, double Y, double Z, int Orientation)>(_eps.Count + tops.Count);
            var t = 0;
            foreach (var e in _eps)
            {
                var ep = (e.X, e.Y, e.Z, -1);
                while (t < tops.Count && Cmp(tops[t], ep) < 0)
                {
                    points.Add(tops[t++]);
                }

                points.Add(ep);
            }

            while (t < tops.Count)
            {
                points.Add(tops[t++]);
            }

            return points;
        }

        private IEnumerable<int> OrientationsAt(Item item, int only) =>
            only >= 0 ? [only] : Enumerable.Range(0, item.Orientations.Count);

        /// <summary>Fond → gauche → bas : point extrême par point extrême, orientations de la plus basse à la plus large.</summary>
        private bool PlaceWalls(Item item)
        {
            foreach (var (x, y, z, only) in Points(item))
            {
                var options = new List<(Placement P, Geo Geo)>();
                foreach (var oi in OrientationsAt(item, only))
                {
                    var p = NewPlacement(item, item.Orientations[oi], x, y, z);
                    if (Candidate(p, item, oi) is { } geo)
                    {
                        options.Add((p, geo));
                    }
                }

                foreach (var (p, geo) in options.OrderBy(c => Math.Round(c.P.MaxZ, 6)).ThenByDescending(c => c.P.DX * c.P.DY))
                {
                    if (TryCommit(item, p, geo))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Score de placement (étude hétérogène §5) : cote la plus basse, surface de niveau, équilibre, fond-gauche. Les
        /// positions sont évaluées par tranche de hauteur ; dans la tranche la plus basse, la charge est contrôlée dans
        /// l'ordre du score, puis tranche suivante si aucune ne passe.
        /// </summary>
        private bool PlaceBottomLeft(Item item)
        {
            var band = double.NaN;
            var inBand = new List<((double z, int level, double balance, double corner, double top) Key, Placement P, Geo Geo)>();

            bool Flush()
            {
                foreach (var c in inBand.OrderBy(c => c.Key))
                {
                    if (TryCommit(item, c.P, c.Geo))
                    {
                        return true;
                    }
                }

                inBand.Clear();
                return false;
            }

            foreach (var (x, y, z, only) in Points(item))
            {
                var b = Math.Round(z / 10);
                if (inBand.Count > 0 && b > band && Flush())
                {
                    return true;
                }

                band = b;
                foreach (var oi in OrientationsAt(item, only))
                {
                    var p = NewPlacement(item, item.Orientations[oi], x, y, z);
                    if (Candidate(p, item, oi) is not { } geo)
                    {
                        continue;
                    }

                    var key = (b, LevelPenalty(p), Math.Round(Balance(p), 2), Math.Round((p.X - _ctx.MinX) + (p.Y - _ctx.MinY)), p.MaxZ);
                    inBand.Add((key, p, geo));
                }
            }

            return inBand.Count > 0 && Flush();
        }

        private static Placement NewPlacement(Item item, Orient o, double x, double y, double z) => new()
        {
            ArticleId = item.Article.Id, X = x, Y = y, Z = z, DX = o.DX, DY = o.DY, DZ = o.DZ, Shape = o.Shape, Weight = item.Article.Weight
        };

        /// <summary>0 si le dessus du produit s'aligne sur un dessus existant (couche plane), sinon 1 (R4).</summary>
        private int LevelPenalty(Placement p) =>
            _boxes.Count == 0 || _tops.Any(t => Math.Abs(t - p.MaxZ) < 1) ? 0 : 1;

        /// <summary>Décalage du centre de gravité (relatif à la demi-dimension) si le produit est posé ici (R9).</summary>
        private double Balance(Placement p)
        {
            var w = Math.Max(1e-9, p.Weight);
            var total = _weight + w;
            var cx = (_sumX + w * (p.X + p.DX / 2)) / total;
            var cy = (_sumY + w * (p.Y + p.DY / 2)) / total;
            var midX = (_ctx.MinX + _ctx.MaxX) / 2;
            var midY = (_ctx.MinY + _ctx.MaxY) / 2;
            return Math.Max(Math.Abs(cx - midX) / ((_ctx.MaxX - _ctx.MinX) / 2), Math.Abs(cy - midY) / ((_ctx.MaxY - _ctx.MinY) / 2));
        }

        /// <summary>
        /// Pose d'une couche complète : tous les produits passent les contrôles ou aucun n'est posé. La charge de toute
        /// la couche est propagée en une seule passe.
        /// </summary>
        public bool PlaceLayer(List<(Item item, Placement p)> layer)
        {
            if (_weight + layer.Sum(l => l.p.Weight) > _ctx.MaxWeight + 1e-9)
            {
                return false;
            }

            var geos = new List<Geo>(layer.Count);
            foreach (var (item, p) in layer)
            {
                if (CheckGeometry(p, item) is not { } geo)
                {
                    return false;
                }

                geos.Add(geo);
            }

            if (Loads(layer.Select((l, i) => (l.p, geos[i].Supporters))) is not { } loads)
            {
                return false;
            }

            ApplyLoads(loads);
            for (var i = 0; i < layer.Count; i++)
            {
                Commit(layer[i].p, layer[i].item, geos[i]);
            }

            // Points extrêmes de la couche entière : son dessus et ses abords, au lieu d'un calcul par produit.
            var x0 = layer.Min(l => l.p.X);
            var y0 = layer.Min(l => l.p.Y);
            var x1 = layer.Max(l => l.p.MaxX);
            var y1 = layer.Max(l => l.p.MaxY);
            var z0 = layer.Min(l => l.p.Z);
            var z1 = layer.Max(l => l.p.MaxZ);
            _eps.RemoveWhere(e => Inside(e.X, e.Y, e.Z));
            AddPoint(x0, y0, z1);
            AddPoint(x1, y0, z0);
            AddPoint(x0, y1, z0);
            return true;
        }

        /// <summary>Contrôles géométriques d'une position : appuis retenus (fraction de charge) et rang dans la pile.</summary>
        private sealed record Geo(List<(Box Box, double Fraction)> Supporters, int SameLevel);

        /// <summary>Colonnes touchées par la charge (charge et « mélange » attendus portés par chaque colonne).</summary>
        private sealed record LoadDelta(List<Column> Columns);

        /// <summary>Positions refusées faute de place (chevauchement, bords) : définitif, la place ne fait que diminuer.</summary>
        private readonly HashSet<(double, double, double, Guid, int)> _blocked = [];

        /// <summary>Positions refusées faute d'appui, par cote : oubliées dès qu'un nouveau dessus arrive à cette cote.</summary>
        private readonly Dictionary<double, HashSet<(double, double, Guid, int)>> _unsupported = [];

        /// <summary>Contrôle géométrique d'une position de point extrême, avec mémoire des refus.</summary>
        private Geo? Candidate(Placement p, Item item, int orientation)
        {
            var key = (p.X, p.Y, p.Z, item.Article.Id, orientation);
            if (_blocked.Contains(key))
            {
                return null;
            }

            var level = Math.Round(p.Z, 3);
            if (_unsupported.TryGetValue(level, out var set) && set.Contains((p.X, p.Y, item.Article.Id, orientation)))
            {
                return null;
            }

            var geo = CheckGeometry(p, item, out var permanent);
            if (geo == null)
            {
                if (permanent)
                {
                    _blocked.Add(key);
                }
                else
                {
                    if (set == null)
                    {
                        _unsupported[level] = set = [];
                    }

                    set.Add((p.X, p.Y, item.Article.Id, orientation));
                }
            }

            return geo;
        }

        private Geo? CheckGeometry(Placement p, Item item) => CheckGeometry(p, item, out _);

        /// <param name="permanent">Refus définitif (chevauchement, bords, appui interdit) ou seulement faute d'appui pour l'instant.</param>
        private Geo? CheckGeometry(Placement p, Item item, out bool permanent)
        {
            permanent = true;
            const double e = Geometry.Eps;
            if (p.X < _ctx.MinX - e || p.Y < _ctx.MinY - e || p.MaxX > _ctx.MaxX + e || p.MaxY > _ctx.MaxY + e || p.MaxZ > _ctx.MaxZ + e)
            {
                return null;
            }

            var near = Nearby(p);
            if (near.Any(b => Geometry.Intersects(p, b.P)))
            {
                return null;
            }

            var supporters = new List<(Box, double)>();
            var sameLevel = 1;
            if (p.Z > e)
            {
                double covered = 0;
                double effective = 0;
                var required = Math.Max(_ctx.MinSupport, item.ProfileFor(p.Shape).MinSupportOffFloor);
                var contacts = new List<(Box, double)>();
                foreach (var b in near)
                {
                    if (Math.Abs(b.P.MaxZ - p.Z) > e)
                    {
                        continue;
                    }

                    // Deux cylindres debout : surface d'appui réelle (disques), rapportée au disque du produit posé.
                    var disks = p.Shape == ShapeKind.CylinderZ && b.P.Shape == ShapeKind.CylinderZ;
                    var o = Geometry.ContactArea(p, b.P);
                    if (o > 1)
                    {
                        // Cylindre couché : seuls ses semblables dessus (R6).
                        if (b.Profile.SameArticleOnTopOnly && b.Item.Article.Id != item.Article.Id)
                        {
                            return null;
                        }

                        contacts.Add((b, o));
                        covered += o;
                        effective += disks ? Geometry.SupportShare(p, b.P) : b.Profile.RoundTop ? o * Math.PI / 4 : o;
                        required = Math.Max(required, b.Profile.SupportRequiredOnTop);
                    }
                }

                if (effective / (p.DX * p.DY) < required - 1e-9)
                {
                    permanent = false;
                    return null;
                }

                supporters.AddRange(contacts.Select(c => (c.Item1, c.Item2 / covered)));
                foreach (var (b, _) in supporters)
                {
                    if (b.Item.Article.Id == item.Article.Id)
                    {
                        sameLevel = Math.Max(sameLevel, b.SameLevel + 1);
                    }
                }

                if (item.Article.MaxLayers is { } ml && sameLevel > ml)
                {
                    permanent = false;
                    return null;
                }
            }

            return new Geo(supporters, sameLevel);
        }

        private static bool Aligned(Placement a, Placement b) =>
            Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.DX - b.DX) < 1 && Math.Abs(a.DY - b.DY) < 1;

        /// <summary>
        /// Charge propagée vers le bas (§6.3) en une passe : les produits sont traités du plus haut au plus bas et chacun
        /// transmet en une fois tout ce qu'il a reçu (le parcours chemin par chemin était exponentiel avec la hauteur).
        /// La capacité « colonne » s'applique tant que la charge descend par des empreintes alignées, sinon la capacité
        /// « mélange ». Null si un produit inférieur dépasserait sa capacité.
        /// </summary>
        private int _stamp;

        /// <summary>
        /// Charge propagée vers le bas (§6.3) en une passe, colonne par colonne, de la plus haute à la plus basse : chaque
        /// colonne est contrôlée en une opération puis transmet sa charge aux appuis de son produit du bas. La capacité
        /// « colonne » s'applique tant que la charge descend par des empreintes alignées, sinon la capacité « mélange ».
        /// Null si un produit inférieur dépasserait sa capacité.
        /// </summary>
        private LoadDelta? Loads(IEnumerable<(Placement Top, List<(Box Box, double Fraction)> Supporters)> tops)
        {
            var stamp = ++_stamp;
            var touched = new List<Column>();
            var queue = new PriorityQueue<Column, double>();

            void Add(Column c, double load, bool mixedPath)
            {
                if (c.Stamp != stamp)
                {
                    c.Stamp = stamp;
                    c.PendingLoad = load;
                    c.PendingMixed = mixedPath;
                    touched.Add(c);
                    queue.Enqueue(c, -c.Bottom.P.Z);
                }
                else
                {
                    c.PendingLoad += load;
                    c.PendingMixed |= mixedPath;
                }
            }

            foreach (var (top, supporters) in tops)
            {
                foreach (var (b, f) in supporters)
                {
                    Add(b.Col, top.Weight * f, !Aligned(top, b.P));
                }
            }

            while (queue.TryDequeue(out var col, out _))
            {
                if (col.PendingLoad > col.MinSlack(col.PendingMixed) + 1e-9)
                {
                    return null;
                }

                var bottom = col.Bottom;
                foreach (var (s, f) in bottom.Supporters)
                {
                    Add(s.Col, col.PendingLoad * f, col.PendingMixed || !Aligned(bottom.P, s.P));
                }
            }

            return new LoadDelta(touched);
        }

        /// <summary>À appliquer aussitôt après <see cref="Loads"/>, avant de poser le produit (charges attendues portées par les colonnes).</summary>
        private static void ApplyLoads(LoadDelta loads)
        {
            foreach (var c in loads.Columns)
            {
                c.Apply(c.PendingLoad, c.PendingMixed);
            }
        }

        private void Commit(Placement p, Item item, Geo geo)
        {
            var box = new Box(p, item) { SameLevel = geo.SameLevel };
            box.Supporters.AddRange(geo.Supporters);
            if (geo.Supporters is [var (s, f)] && f > 0.999 && Aligned(p, s.P) && ReferenceEquals(s.Col.Top, s))
            {
                box.Col = s.Col;
                s.Col.Push(box);
            }
            else
            {
                box.Col = new Column(box);
                box.Col.Push(box);
            }
            p.Sequence = _boxes.Count + 1;
            _boxes.Add(box);
            _weight += p.Weight;
            _sumX += p.Weight * (p.X + p.DX / 2);
            _sumY += p.Weight * (p.Y + p.DY / 2);
            foreach (var key in Cells(p))
            {
                if (!_grid.TryGetValue(key, out var list))
                {
                    _grid[key] = list = [];
                }

                list.Add(box);
            }

            _tops.Add(Math.Round(p.MaxZ, 3));
            _unsupported.Remove(Math.Round(p.MaxZ, 3));
            foreach (var (below, _) in geo.Supporters)
            {
                _freeTops.Remove(below);
            }

            if (p.Shape == ShapeKind.CylinderZ)
            {
                _freeTops.Add(box);
            }
        }

        private IEnumerable<(int, int, int)> Cells(Placement p) => Cells(p, p.Z, p.MaxZ - 1e-6);

        private IEnumerable<(int, int, int)> Cells(Placement p, double z0, double z1)
        {
            var x0 = (int)Math.Floor((p.X - _ctx.MinX) / Cell);
            var x1 = (int)Math.Floor((p.MaxX - _ctx.MinX - 1e-6) / Cell);
            var y0 = (int)Math.Floor((p.Y - _ctx.MinY) / Cell);
            var y1 = (int)Math.Floor((p.MaxY - _ctx.MinY - 1e-6) / Cell);
            var k0 = (int)Math.Floor(z0 / Cell);
            var k1 = (int)Math.Floor(z1 / Cell);
            for (var i = x0; i <= x1; i++)
            {
                for (var j = y0; j <= y1; j++)
                {
                    for (var k = k0; k <= k1; k++)
                    {
                        yield return (i, j, k);
                    }
                }
            }
        }

        /// <summary>Produits voisins : ceux qui recoupent le volume du produit, et ceux dont le dessus est sous sa base (appuis).</summary>
        private HashSet<Box> Nearby(Placement p)
        {
            var set = new HashSet<Box>();
            foreach (var key in Cells(p, p.Z - 1, p.MaxZ - 1e-6))
            {
                if (_grid.TryGetValue(key, out var list))
                {
                    set.UnionWith(list);
                }
            }

            return set;
        }

        private void UpdatePoints(Placement p)
        {
            _eps.Remove((p.X, p.Y, p.Z));
            var candidates = new List<(double, double, double)>
            {
                (p.MaxX, p.Y, p.Z),
                (p.X, p.MaxY, p.Z),
                (p.X, p.Y, p.MaxZ),
                (p.MaxX, ProjectY(p.MaxX, p.Y, p.Z), p.Z),
                (ProjectX(p.X, p.MaxY, p.Z), p.MaxY, p.Z)
            };
            foreach (var (x, y, z) in candidates.ToList())
            {
                var dz = ProjectZ(x, y, z);
                if (dz < z - Geometry.Eps)
                {
                    candidates.Add((x, y, dz));
                }
            }

            // Les anciens points recouverts par ce produit disparaissent ; les nouveaux sont gardés s'ils sont libres.
            _eps.RemoveWhere(e => InBox(e.X, e.Y, e.Z, p));
            foreach (var (x, y, z) in candidates)
            {
                AddPoint(x, y, z);
            }
        }

        private void AddPoint(double x, double y, double z)
        {
            // Trop près d'un bord pour le plus petit produit : point inutile (marges de centrage des couches, etc.).
            if (_ctx.MaxX - x < _minFoot - 1e-6 || _ctx.MaxY - y < _minFoot - 1e-6 || _ctx.MaxZ - z < _minHeight - 1e-6)
            {
                return;
            }

            if (x < _ctx.MaxX - 1 && y < _ctx.MaxY - 1 && z < _ctx.MaxZ - 1 && !Inside(x, y, z))
            {
                _eps.Add((Math.Round(x, 3), Math.Round(y, 3), Math.Round(z, 3)));
            }
        }

        private static bool InBox(double x, double y, double z, Placement b) =>
            x >= b.X - 1e-6 && x < b.MaxX - 1e-6 && y >= b.Y - 1e-6 && y < b.MaxY - 1e-6 && z >= b.Z - 1e-6 && z < b.MaxZ - 1e-6;

        private (int, int, int) CellOf(double x, double y, double z) =>
            ((int)Math.Floor((x - _ctx.MinX) / Cell), (int)Math.Floor((y - _ctx.MinY) / Cell), (int)Math.Floor(z / Cell));

        private List<Box> At(int i, int j, int k) => _grid.TryGetValue((i, j, k), out var list) ? list : [];

        /// <summary>Point à l'intérieur d'un produit posé (produits de la maille du point seulement).</summary>
        private bool Inside(double x, double y, double z)
        {
            var (i, j, k) = CellOf(x, y, z);
            return At(i, j, k).Any(b => InBox(x, y, z, b.P));
        }

        private int Columns => (int)Math.Ceiling((_ctx.MaxX - _ctx.MinX) / Cell) + 1;
        private int Rows => (int)Math.Ceiling((_ctx.MaxY - _ctx.MinY) / Cell) + 1;

        private double ProjectY(double x, double y, double z)
        {
            var best = _ctx.MinY;
            var (i, _, k) = CellOf(x, y, z);
            for (var j = -1; j <= Rows; j++)
            {
                foreach (var b in At(i, j, k))
                {
                    if (x >= b.P.X && x < b.P.MaxX && z >= b.P.Z && z < b.P.MaxZ && b.P.MaxY <= y + 1e-6)
                    {
                        best = Math.Max(best, b.P.MaxY);
                    }
                }
            }

            return best;
        }

        private double ProjectX(double x, double y, double z)
        {
            var best = _ctx.MinX;
            var (_, j, k) = CellOf(x, y, z);
            for (var i = -1; i <= Columns; i++)
            {
                foreach (var b in At(i, j, k))
                {
                    if (y >= b.P.Y && y < b.P.MaxY && z >= b.P.Z && z < b.P.MaxZ && b.P.MaxX <= x + 1e-6)
                    {
                        best = Math.Max(best, b.P.MaxX);
                    }
                }
            }

            return best;
        }

        private double ProjectZ(double x, double y, double z)
        {
            double best = 0;
            var (i, j, top) = CellOf(x, y, z);
            for (var k = top; k >= 0; k--)
            {
                foreach (var b in At(i, j, k))
                {
                    if (x >= b.P.X && x < b.P.MaxX && y >= b.P.Y && y < b.P.MaxY && b.P.MaxZ <= z + 1e-6)
                    {
                        best = Math.Max(best, b.P.MaxZ);
                    }
                }

                if (best > 0)
                {
                    break; // les mailles plus basses ne contiennent que des dessus plus bas
                }
            }

            return best;
        }

        public LoadUnit ToUnit(int index)
        {
            // Recentrage de la charge sur la base (même décalage pour tous les produits).
            if (_ctx.C.CenterLoad && _boxes.Count > 0)
            {
                var dx = (_ctx.MinX + _ctx.MaxX) / 2 - (_boxes.Min(b => b.P.X) + _boxes.Max(b => b.P.MaxX)) / 2;
                var dy = (_ctx.MinY + _ctx.MaxY) / 2 - (_boxes.Min(b => b.P.Y) + _boxes.Max(b => b.P.MaxY)) / 2;
                foreach (var b in _boxes)
                {
                    b.P.X += dx;
                    b.P.Y += dy;
                }
            }

            // Charges exactes reçues par chaque produit (une passe, du haut vers le bas) pour les indicateurs.
            foreach (var b in _boxes)
            {
                b.Load = 0;
                b.MixedLoaded = false;
            }

            foreach (var b in _boxes.OrderByDescending(b => b.P.Z))
            {
                var total = b.Load + b.P.Weight;
                foreach (var (s, f) in b.Supporters)
                {
                    s.Load += total * f;
                    if (b.MixedLoaded || !Aligned(b.P, s.P))
                    {
                        s.MixedLoaded = true;
                    }
                }
            }

            var unit = new LoadUnit { Index = index };
            var contacts = 0;
            var good = 0;
            double capUse = 0;
            foreach (var b in _boxes)
            {
                foreach (var (s, _) in b.Supporters)
                {
                    contacts++;
                    // Bon sens : plus léger dessus, ou appui rigide / souple qui porte la charge reçue.
                    if (b.P.Weight <= s.P.Weight + 1e-9 || s.Profile.Class is StackClass.Rigide or StackClass.Souple)
                    {
                        good++;
                    }
                }

                var cap = b.CapFor(false);
                if (b.Load > 0 && cap is > 0 and < double.MaxValue)
                {
                    capUse = Math.Max(capUse, b.Load / cap);
                }
            }

            unit.Metrics.OrderRespect = contacts == 0 ? 100 : good * 100.0 / contacts;
            unit.Metrics.CapacityUseMax = capUse * 100;
            var levels = _boxes.Select(b => Math.Round(b.P.Z)).Distinct().OrderBy(z => z)
                .Select((z, k) => (z, k)).ToDictionary(t => t.z, t => t.k + 1);
            foreach (var b in _boxes)
            {
                b.P.Layer = levels[Math.Round(b.P.Z)];
                unit.Items.Add(b.P);
            }

            foreach (var group in unit.Items.GroupBy(p => p.Layer).OrderBy(g => g.Key))
            {
                var articles = group.Select(p => p.ArticleId).Distinct().ToList();
                unit.Layers.Add(new LayerInfo
                {
                    Index = group.Key,
                    Z = group.Min(p => p.Z),
                    Height = group.Max(p => p.DZ),
                    Pattern = articles.Count == 1 ? "Homogène" : "Mixte",
                    ArticleId = articles.Count == 1 ? articles[0] : null,
                    Count = group.Count()
                });
            }

            return unit;
        }
    }

    // ------------------------------------------------------------------ Solution, classement

    private static Solution NewSolution(Ctx ctx, MixedStrategy strategy) => new()
    {
        Kind = PackagingKind.Heterogene,
        Strategy = strategy,
        Pattern = StackPattern.Mixte,
        Base = ctx.Base,
        Title = StrategyLabel(strategy),
        Description = strategy switch
        {
            MixedStrategy.CouchesHomogenes => "Couches complètes mono-article (du plus lourd au plus léger), reliquat en couche mixte sur le dessus.",
            MixedStrategy.PilesParArticle => "Piles verticales regroupées par article, construites du fond vers l'avant.",
            _ => "Placement par points extrêmes, bas → fond → gauche, meilleur de plusieurs ordres de tri."
        }
    };

    public static string StrategyLabel(MixedStrategy s) => s switch
    {
        MixedStrategy.CouchesHomogenes => "Couches homogènes",
        MixedStrategy.PilesParArticle => "Piles par article",
        _ => "Densité maximale"
    };

    private static void Finish(Solution s, Ctx ctx, int requested)
    {
        s.RequestedItems = requested;
        foreach (var unit in s.Units.Where(u => !u.IsFullPallet))
        {
            var order = unit.Metrics.OrderRespect;
            var capUse = unit.Metrics.CapacityUseMax;
            unit.Metrics = MetricsCalculator.Compute(unit, ctx.Base, ctx.C);
            unit.Metrics.OrderRespect = order;
            unit.Metrics.CapacityUseMax = capUse;
        }

        s.ItemsPerUnit = s.Units.Count == 0 ? 0 : s.Units.Max(u => u.Items.Count);
        s.LayerCount = s.Units.Count == 0 ? 0 : s.Units.Max(u => u.Layers.Count);
        s.CapHeight = ctx.C.CapHeight;
        s.StackLevels = 1;
        s.StackLimitReason = "hétérogène : gerbage non calculé";
        if (s.Units.Count > 0)
        {
            var fill = s.Units.Average(u => u.Metrics.FillRate) / 100;
            s.StabilityScore = s.Units.Average(u => u.Metrics.SupportAvg / 100 * (1 - Math.Min(1, u.Metrics.CogOffset / 100)));
            var homogeneity = s.Units.Average(u => u.Metrics.Homogeneity) / 100;
            var order = s.Units.Average(u => u.Metrics.OrderRespect) / 100;
            s.Score = 0.35 * fill + 0.30 * s.StabilityScore + 0.20 * order + 0.15 * homogeneity;
        }

        s.Title = $"{StrategyLabel(s.Strategy!.Value)} · {s.Units.Count} unité(s)";
        if (s.UnplacedItems > 0)
        {
            s.Violations.Add($"{s.UnplacedItems} produit(s) impossibles à placer sur cette base (dimensions, hauteur ou poids).");
        }

        foreach (var u in s.Units)
        {
            var label = s.Units.Count > 1 ? $"Unité {u.Index} : " : "";
            if (u.Metrics.CogOffset > 10)
            {
                s.Warnings.Add($"{label}centre de gravité décentré de {u.Metrics.CogOffset.ToString("0", Fr)} %.");
            }

            if (u.Metrics.Slenderness > 2.5)
            {
                s.Warnings.Add($"{label}élancement élevé ({u.Metrics.Slenderness.ToString("0.0", Fr)}) : filmage renforcé ou cerclage conseillé.");
            }

            var perPallet = u.Metrics.LoadWeight / ctx.Base.PhysicalCount;
            if (ctx.Base.PalletDynamicLoad > 0 && perPallet > ctx.Base.PalletDynamicLoad + 1e-6)
            {
                s.Violations.Add($"{label}poids par palette physique {perPallet:0} kg > charge dynamique {ctx.Base.PalletDynamicLoad:0} kg.");
            }
        }

        // Palettes complètes : solution homogène déjà contrôlée ; seules les unités mélangées sont revérifiées.
        var mixedUnits = s.Units.Where(u => !u.IsFullPallet).ToList();
        if (mixedUnits.Count > 0)
        {
            var view = new Solution { Kind = s.Kind, Base = s.Base, Units = mixedUnits };
            s.Violations.AddRange(SolutionValidator.Validate(view, ctx.C, checkSupport: true));
        }
    }

    private static void Rank(List<Solution> list)
    {
        list.Sort((a, b) =>
        {
            var c = (a.IsCompliant ? 0 : 1).CompareTo(b.IsCompliant ? 0 : 1);
            if (c != 0)
            {
                return c;
            }

            c = a.UnplacedItems.CompareTo(b.UnplacedItems);
            if (c != 0)
            {
                return c;
            }

            c = a.Units.Count.CompareTo(b.Units.Count);
            return c != 0 ? c : b.Score.CompareTo(a.Score);
        });

        var best = list.FirstOrDefault(s => s.Units.Count > 0);
        if (best == null)
        {
            return;
        }

        best.Recommended = true;
        var u = best.Units;
        best.Recommendation =
            $"Recommandée : {best.TotalItems.ToString("#,0", Fr)} produit(s) sur {u.Count} unité(s) de charge ; " +
            $"remplissage {u.Average(x => x.Metrics.FillRate).ToString("0", Fr)} %, " +
            $"support moyen {u.Average(x => x.Metrics.SupportAvg).ToString("0", Fr)} %, " +
            $"ordre lourd / léger respecté à {u.Average(x => x.Metrics.OrderRespect).ToString("0", Fr)} %, " +
            $"capacité portante utilisée au plus à {u.Max(x => x.Metrics.CapacityUseMax).ToString("0", Fr)} %, " +
            $"regroupement par article {u.Average(x => x.Metrics.Homogeneity).ToString("0", Fr)} % " +
            $"(score {best.Score * 100:0}/100 : 35 % remplissage, 30 % stabilité, 20 % ordre, 15 % homogénéité).";
    }
}
