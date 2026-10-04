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

            valid.Add((article, qty));
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

        var items = new List<Item>();
        foreach (var (article, qty) in valid)
        {
            var orientations = Orientations(article, c);
            var profile = StackingProfile.For(article, c.ForcedAxis);
            var upright = StackingProfile.For(article, CoilAxis.Vertical);
            var lying = StackingProfile.For(article, CoilAxis.Horizontal);
            for (var i = 0; i < qty; i++)
            {
                items.Add(new Item(article, orientations, items.Count, profile, upright, lying));
            }
        }

        // Ordre de pose de l'étude hétérogène §4, appliqué aux trois stratégies.
        var runs = new List<Func<Solution>>
        {
            () => Layered(items, ctx),
            () => Packed(SortForPiles(items), ctx, Priority.Walls, MixedStrategy.PilesParArticle),
            () => BestOf(ctx, Priority.BottomLeft, MixedStrategy.DensiteMaximale,
                ByStudy(items), SortBy(items, i => -i.Volume), SortBy(items, i => -i.BaseArea), SortBy(items, i => -i.Article.Weight))
        };
        var solutions = new Solution[runs.Count];
        Parallel.For(0, runs.Count, i => solutions[i] = runs[i]());
        foreach (var s in solutions)
        {
            Finish(s, ctx, items.Count);
            result.Solutions.Add(s);
        }

        Rank(result.Solutions);
        return result;
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

    private static Solution Layered(List<Item> all, Ctx ctx)
    {
        var solution = NewSolution(ctx, MixedStrategy.CouchesHomogenes);
        var remaining = all.ToList();
        while (remaining.Count > 0)
        {
            var packer = new UnitPacker(ctx);
            var articles = remaining.GroupBy(i => i.Article.Id)
                .Select(g => g.First())
                .OrderBy(i => (int)i.Profile.Zone)
                .ThenByDescending(i => i.StrengthKey)
                .ThenByDescending(i => i.Article.Weight / Math.Max(1, FootprintArea(i.Article)))
                .Select(i => i.Article)
                .ToList();
            double z0 = 0;
            foreach (var article in articles)
            {
                var plan = BestLayer(article, ctx);
                if (plan == null)
                {
                    continue;
                }

                var (rects, h, shape) = plan.Value;
                while (true)
                {
                    var pool = remaining.Where(i => i.Article.Id == article.Id).ToList();
                    if (pool.Count < rects.Count || z0 + h > ctx.MaxZ + 1e-6)
                    {
                        break;
                    }

                    var sx = ctx.C.CenterLoad ? (ctx.MaxX - ctx.MinX - rects.Max(r => r.Right)) / 2 : 0;
                    var sy = ctx.C.CenterLoad ? (ctx.MaxY - ctx.MinY - rects.Max(r => r.Top)) / 2 : 0;
                    var layer = rects.Select((r, k) => (pool[k], new Placement
                    {
                        ArticleId = article.Id,
                        X = ctx.MinX + sx + r.X,
                        Y = ctx.MinY + sy + r.Y,
                        Z = z0,
                        DX = r.W,
                        DY = r.H,
                        DZ = h,
                        Shape = shape,
                        Weight = article.Weight
                    })).ToList();
                    if (!packer.PlaceLayer(layer))
                    {
                        break;
                    }

                    foreach (var (item, _) in layer)
                    {
                        remaining.Remove(item);
                    }

                    z0 += h;
                }
            }

            // Reliquat : couche(s) mixte(s) sur le dessus, en points extrêmes.
            foreach (var item in ByStudy(remaining))
            {
                if (packer.TryPlace(item, Priority.BottomLeft))
                {
                    remaining.Remove(item);
                }
            }

            if (packer.Count == 0)
            {
                break;
            }

            solution.Units.Add(packer.ToUnit(solution.Units.Count + 1));
        }

        solution.UnplacedItems = remaining.Count;
        return solution;
    }

    private static double FootprintArea(Article a) => a.IsCylinder ? a.Diameter * a.Diameter : a.Length * a.Width;

    /// <summary>Meilleure couche complète d'un article sur la surface utile (plans du §4), produits debout uniquement.</summary>
    private static (List<Rect2> rects, double h, ShapeKind shape)? BestLayer(Article a, Ctx ctx)
    {
        var x = ctx.MaxX - ctx.MinX;
        var y = ctx.MaxY - ctx.MinY;
        if (a.IsCylinder)
        {
            var axis = ctx.C.ForcedAxis ?? a.CoilAxis;
            if (a.Kind != ArticleKind.Fut && axis == CoilAxis.Horizontal)
            {
                return null;
            }

            var circles = CircleLayerSolver.Solve(x, y, a.Diameter, ctx.C.Gap).Best;
            return circles.Count == 0 ? null : (circles.Items, a.AxisLength, ShapeKind.CylinderZ);
        }

        var options = new List<(double a, double b, double h)> { (a.Length, a.Width, a.Height) };
        if (a.Orientation == OrientationRule.Libre && ArticleSchema.UsesOrientation(a.Kind))
        {
            options.Add((a.Length, a.Height, a.Width));
            options.Add((a.Width, a.Height, a.Length));
        }

        (List<Rect2>, double, ShapeKind)? best = null;
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
                best = (layer.Items, oh, ShapeKind.Box);
            }
        }

        return best;
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

        return best!;
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
            var packer = new UnitPacker(ctx);
            foreach (var item in remaining.ToList())
            {
                if (packer.TryPlace(item, priority))
                {
                    remaining.Remove(item);
                }
            }

            if (packer.Count == 0)
            {
                break;
            }

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
        private readonly List<Box> _boxes = [];
        private readonly Dictionary<(int, int), List<Box>> _grid = [];
        private readonly HashSet<(double, double, double)> _eps = [];
        private double _weight;
        private double _sumX;
        private double _sumY;

        public UnitPacker(Ctx ctx)
        {
            _ctx = ctx;
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

            public double Load { get; set; }
            public int SameLevel { get; set; } = 1;
            public List<(Box Box, double Fraction)> Supporters { get; } = [];
        }

        public bool TryPlace(Item item, Priority priority)
        {
            if (_weight + item.Article.Weight > _ctx.MaxWeight + 1e-9)
            {
                return false;
            }

            if (priority == Priority.Walls)
            {
                foreach (var (x, y, z) in _eps.OrderBy(e => Math.Round(e.Item1)).ThenBy(e => Math.Round(e.Item2)).ThenBy(e => e.Item3).ToList())
                {
                    Placement? best = null;
                    Check? bestCheck = null;
                    foreach (var o in item.Orientations)
                    {
                        var p = NewPlacement(item, o, x, y, z);
                        var check = Evaluate(p, item);
                        if (check != null && (best == null || p.MaxZ < best.MaxZ - 1e-6 || (Math.Abs(p.MaxZ - best.MaxZ) < 1e-6 && p.DX * p.DY > best.DX * best.DY)))
                        {
                            best = p;
                            bestCheck = check;
                        }
                    }

                    if (best != null)
                    {
                        Commit(best, item, bestCheck!);
                        return true;
                    }
                }

                return false;
            }

            // Score de placement (étude hétérogène §5) : cote la plus basse, surface de niveau, équilibre, fond-gauche.
            Placement? chosen = null;
            Check? chosenCheck = null;
            (double z, int level, double balance, double corner, double top) bestKey = default;
            foreach (var (x, y, z) in _eps.OrderBy(e => Math.Round(e.Item3)).ThenBy(e => Math.Round(e.Item1)).ThenBy(e => e.Item2).ToList())
            {
                var band = Math.Round(z / 10);
                if (chosen != null && band > bestKey.z)
                {
                    break;
                }

                foreach (var o in item.Orientations)
                {
                    var p = NewPlacement(item, o, x, y, z);
                    var check = Evaluate(p, item);
                    if (check == null)
                    {
                        continue;
                    }

                    var key = (band, LevelPenalty(p), Math.Round(Balance(p), 2), Math.Round((p.X - _ctx.MinX) + (p.Y - _ctx.MinY)), p.MaxZ);
                    if (chosen == null || key.CompareTo(bestKey) < 0)
                    {
                        chosen = p;
                        chosenCheck = check;
                        bestKey = key;
                    }
                }
            }

            if (chosen == null)
            {
                return false;
            }

            Commit(chosen, item, chosenCheck!);
            return true;
        }

        private static Placement NewPlacement(Item item, Orient o, double x, double y, double z) => new()
        {
            ArticleId = item.Article.Id, X = x, Y = y, Z = z, DX = o.DX, DY = o.DY, DZ = o.DZ, Shape = o.Shape, Weight = item.Article.Weight
        };

        /// <summary>0 si le dessus du produit s'aligne sur un dessus existant (couche plane), sinon 1 (R4).</summary>
        private int LevelPenalty(Placement p) =>
            _boxes.Count == 0 || _boxes.Any(b => Math.Abs(b.P.MaxZ - p.MaxZ) < 1) ? 0 : 1;

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

        /// <summary>Pose d'une couche complète : tous les produits passent les contrôles ou aucun n'est posé.</summary>
        public bool PlaceLayer(List<(Item item, Placement p)> layer)
        {
            if (_weight + layer.Sum(l => l.p.Weight) > _ctx.MaxWeight + 1e-9)
            {
                return false;
            }

            var checks = new List<Check>();
            foreach (var (item, p) in layer)
            {
                var check = Evaluate(p, item);
                if (check == null)
                {
                    return false;
                }

                checks.Add(check);
            }

            // Charges cumulées de toute la couche sur les produits inférieurs.
            var total = new Dictionary<Box, double>();
            var mixedAll = new HashSet<Box>();
            foreach (var check in checks)
            {
                foreach (var (box, load) in check.Delta)
                {
                    total[box] = total.GetValueOrDefault(box) + load;
                }

                mixedAll.UnionWith(check.Mixed);
            }

            if (total.Any(kv => kv.Key.Load + kv.Value > kv.Key.CapFor(mixedAll.Contains(kv.Key)) + 1e-9))
            {
                return false;
            }

            for (var i = 0; i < layer.Count; i++)
            {
                Commit(layer[i].p, layer[i].item, checks[i]);
            }

            return true;
        }

        private sealed record Check(List<(Box Box, double Fraction)> Supporters, Dictionary<Box, double> Delta, int SameLevel, HashSet<Box> Mixed);

        private Check? Evaluate(Placement p, Item item)
        {
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

                    var o = Geometry.FootprintOverlap(p, b.P);
                    if (o > 1)
                    {
                        // Cylindre couché : seuls ses semblables dessus (R6).
                        if (b.Profile.SameArticleOnTopOnly && b.Item.Article.Id != item.Article.Id)
                        {
                            return null;
                        }

                        contacts.Add((b, o));
                        covered += o;
                        effective += b.Profile.RoundTop ? o * Math.PI / 4 : o;
                        required = Math.Max(required, b.Profile.SupportRequiredOnTop);
                    }
                }

                if (effective / (p.DX * p.DY) < required - 1e-9)
                {
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
                    return null;
                }
            }

            // Charge propagée vers le bas (§6.3) : refus si un produit inférieur dépasse sa capacité. La capacité « colonne »
            // s'applique tant que la charge descend par des empreintes parfaitement alignées, sinon la capacité « mélange ».
            var delta = new Dictionary<Box, double>();
            var mixed = new HashSet<Box>();
            foreach (var (b, f) in supporters)
            {
                Propagate(b, p.Weight * f, !Aligned(p, b.P), delta, mixed);
            }

            if (delta.Any(kv => kv.Key.Load + kv.Value > kv.Key.CapFor(mixed.Contains(kv.Key)) + 1e-9))
            {
                return null;
            }

            return new Check(supporters, delta, sameLevel, mixed);
        }

        private static bool Aligned(Placement a, Placement b) =>
            Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.DX - b.DX) < 1 && Math.Abs(a.DY - b.DY) < 1;

        private static void Propagate(Box box, double load, bool mixedPath, Dictionary<Box, double> delta, HashSet<Box> mixed)
        {
            delta[box] = delta.GetValueOrDefault(box) + load;
            if (mixedPath)
            {
                mixed.Add(box);
            }

            foreach (var (s, f) in box.Supporters)
            {
                Propagate(s, load * f, mixedPath || !Aligned(box.P, s.P), delta, mixed);
            }
        }

        private void Commit(Placement p, Item item, Check check)
        {
            var box = new Box(p, item) { SameLevel = check.SameLevel };
            box.Supporters.AddRange(check.Supporters);
            foreach (var (b, load) in check.Delta)
            {
                b.Load += load;
            }

            foreach (var b in check.Mixed)
            {
                b.MixedLoaded = true;
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

            UpdatePoints(p);
        }

        private IEnumerable<(int, int)> Cells(Placement p)
        {
            var x0 = (int)Math.Floor((p.X - _ctx.MinX) / Cell);
            var x1 = (int)Math.Floor((p.MaxX - _ctx.MinX - 1e-6) / Cell);
            var y0 = (int)Math.Floor((p.Y - _ctx.MinY) / Cell);
            var y1 = (int)Math.Floor((p.MaxY - _ctx.MinY - 1e-6) / Cell);
            for (var i = x0; i <= x1; i++)
            {
                for (var j = y0; j <= y1; j++)
                {
                    yield return (i, j);
                }
            }
        }

        private HashSet<Box> Nearby(Placement p)
        {
            var set = new HashSet<Box>();
            foreach (var key in Cells(p))
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

            foreach (var c in candidates)
            {
                if (c.Item1 < _ctx.MaxX - 1 && c.Item2 < _ctx.MaxY - 1 && c.Item3 < _ctx.MaxZ - 1)
                {
                    _eps.Add((Math.Round(c.Item1, 3), Math.Round(c.Item2, 3), Math.Round(c.Item3, 3)));
                }
            }

            _eps.RemoveWhere(e => _boxes.Any(b => e.Item1 >= b.P.X - 1e-6 && e.Item1 < b.P.MaxX - 1e-6 &&
                                                   e.Item2 >= b.P.Y - 1e-6 && e.Item2 < b.P.MaxY - 1e-6 &&
                                                   e.Item3 >= b.P.Z - 1e-6 && e.Item3 < b.P.MaxZ - 1e-6));
        }

        private double ProjectY(double x, double y, double z)
        {
            var best = _ctx.MinY;
            foreach (var b in _boxes)
            {
                if (x >= b.P.X && x < b.P.MaxX && z >= b.P.Z && z < b.P.MaxZ && b.P.MaxY <= y + 1e-6)
                {
                    best = Math.Max(best, b.P.MaxY);
                }
            }

            return best;
        }

        private double ProjectX(double x, double y, double z)
        {
            var best = _ctx.MinX;
            foreach (var b in _boxes)
            {
                if (y >= b.P.Y && y < b.P.MaxY && z >= b.P.Z && z < b.P.MaxZ && b.P.MaxX <= x + 1e-6)
                {
                    best = Math.Max(best, b.P.MaxX);
                }
            }

            return best;
        }

        private double ProjectZ(double x, double y, double z)
        {
            double best = 0;
            foreach (var b in _boxes)
            {
                if (x >= b.P.X && x < b.P.MaxX && y >= b.P.Y && y < b.P.MaxY && b.P.MaxZ <= z + 1e-6)
                {
                    best = Math.Max(best, b.P.MaxZ);
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
            var levels = _boxes.Select(b => Math.Round(b.P.Z)).Distinct().OrderBy(z => z).ToList();
            foreach (var b in _boxes)
            {
                b.P.Layer = levels.IndexOf(Math.Round(b.P.Z)) + 1;
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
        foreach (var unit in s.Units)
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

        s.Violations.AddRange(SolutionValidator.Validate(s, ctx.C, checkSupport: true));
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
            $"Recommandée : {best.TotalItems} produit(s) sur {u.Count} unité(s) de charge ; " +
            $"remplissage {u.Average(x => x.Metrics.FillRate).ToString("0", Fr)} %, " +
            $"support moyen {u.Average(x => x.Metrics.SupportAvg).ToString("0", Fr)} %, " +
            $"ordre lourd / léger respecté à {u.Average(x => x.Metrics.OrderRespect).ToString("0", Fr)} %, " +
            $"capacité portante utilisée au plus à {u.Max(x => x.Metrics.CapacityUseMax).ToString("0", Fr)} %, " +
            $"regroupement par article {u.Average(x => x.Metrics.Homogeneity).ToString("0", Fr)} % " +
            $"(score {best.Score * 100:0}/100 : 35 % remplissage, 30 % stabilité, 20 % ordre, 15 % homogénéité).";
    }
}
