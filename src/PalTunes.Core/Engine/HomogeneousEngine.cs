using System.Globalization;
using PalTunes.Core.Models;
using PalTunes.Core.Validation;

namespace PalTunes.Core.Engine;

public sealed class EngineResult
{
    public List<Solution> Solutions { get; } = [];
    public List<string> Messages { get; } = [];
    public Solution? Recommended => Solutions.FirstOrDefault(s => s.Recommended) ?? Solutions.FirstOrDefault();
}

/// <summary>
/// Palettisation homogène (mono-article), étude §4, §10, §11.1 : pour chaque orientation autorisée, plans de couche
/// optimaux, empilement colonne et croisé, limites (hauteur, poids, couches, résistance), gerbage, recommandation.
/// </summary>
public static class HomogeneousEngine
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly double HexPitch = Math.Sqrt(3) / 2;

    public static EngineResult Solve(Article article, BaseInfo baseInfo, PackagingConstraints c, int? targetQuantity = null)
    {
        article = article.ForPalletizing(); // carton plié : hauteur pliée
        var result = new EngineResult();
        var errors = ArticleSchema.Validate(article);
        if (errors.Count > 0)
        {
            result.Messages.Add($"Article {article.Code} incomplet : " + string.Join(" ", errors));
            return result;
        }

        var ctx = new Context(article, baseInfo, c, targetQuantity);
        if (ctx.UsefulHeight <= 0)
        {
            result.Messages.Add("Hauteur utile nulle : la hauteur maximale est inférieure à la palette + coiffe.");
            return result;
        }

        if (ctx.UsableX <= 0 || ctx.UsableY <= 0)
        {
            result.Messages.Add("Surface utile nulle : vérifiez les débords (retraits trop grands).");
            return result;
        }

        switch (article.Kind)
        {
            case ArticleKind.Tube:
            case ArticleKind.Bobine:
                // Axe imposé par le conditionnement, sinon celui de l'article ; « indifférent » calcule les deux axes.
                if (ctx.Axis != CoilAxis.Horizontal)
                {
                    AddUprightCylinders(ctx, result);
                }

                if (ctx.Axis != CoilAxis.Vertical)
                {
                    AddBeds(ctx, result);
                }

                break;
            case ArticleKind.Fut:
                AddUprightCylinders(ctx, result);
                break;
            default:
                AddBoxes(ctx, result);
                break;
        }

        if (result.Solutions.Count == 0)
        {
            result.Messages.Add($"Aucune disposition possible de {article.DimensionsText} sur {baseInfo.Label} " +
                                $"(surface utile {ctx.UsableX:0} × {ctx.UsableY:0}, hauteur utile {ctx.UsefulHeight:0} mm). " +
                                "Essayez une base plus grande, des palettes multiples ou un débord.");
            return result;
        }

        Deduplicate(result.Solutions);
        Rank(result.Solutions, article);
        return result;
    }

    /// <summary>
    /// Assistant (étude §11.2) : meilleure solution pour chaque palette active et chaque assemblage pertinent
    /// (1 × 1, puis jusqu'à 3 × 2 si le produit ne tient pas sur une seule palette), classées par densité de transport.
    /// </summary>
    public static EngineResult Propose(Article article, IEnumerable<PalletType> pallets, PackagingConstraints c, int? targetQuantity = null)
    {
        article = article.ForPalletizing(); // carton plié : hauteur pliée
        var result = new EngineResult();
        var errors = ArticleSchema.Validate(article);
        if (errors.Count > 0)
        {
            result.Messages.Add($"Article {article.Code} incomplet : " + string.Join(" ", errors));
            return result;
        }

        var (fa, fb) = Footprint(article, c.ForcedAxis ?? article.CoilAxis);
        var bases = new List<BaseInfo>();
        foreach (var p in pallets.Where(p => p.Active && p.Length > 0 && p.Width > 0))
        {
            bases.Add(BaseInfo.From(p, false, 1, 1));
            var fitsSingle = Fits(fa, fb, p.Length + 2 * (c.OverhangLength - c.CornerInset), p.Width + 2 * (c.OverhangWidth - c.CornerInset));
            if (fitsSingle)
            {
                continue;
            }

            // Assemblage minimal couvrant l'empreinte du produit (étude §5.3), dans les deux sens, jusqu'à 3 × 3.
            foreach (var rotated in new[] { false, true })
            {
                var pl = rotated ? p.Width : p.Length;
                var pw = rotated ? p.Length : p.Width;
                foreach (var (a, b2) in new[] { (fa, fb), (fb, fa) })
                {
                    var nl = Math.Max(1, (int)Math.Ceiling((a - 2 * (c.OverhangLength - c.CornerInset)) / pl - 1e-9));
                    var nw = Math.Max(1, (int)Math.Ceiling((b2 - 2 * (c.OverhangWidth - c.CornerInset)) / pw - 1e-9));
                    if (nl * nw <= 1 || nl > 3 || nw > 3)
                    {
                        continue;
                    }

                    var b = BaseInfo.From(p, rotated, nl, nw);
                    if (bases.Any(x => x.PalletId == b.PalletId && Math.Abs(Math.Min(x.Length, x.Width) - Math.Min(b.Length, b.Width)) < 1 &&
                                       Math.Abs(Math.Max(x.Length, x.Width) - Math.Max(b.Length, b.Width)) < 1))
                    {
                        continue;
                    }

                    bases.Add(b);
                }
            }
        }

        var bests = new Solution?[bases.Count];
        Parallel.For(0, bases.Count, i =>
        {
            var r = Solve(article, bases[i], c, targetQuantity);
            bests[i] = r.Recommended;
        });

        var list = bests.Where(s => s != null && s.IsCompliant).Select(s => s!).ToList();
        foreach (var s in list)
        {
            s.Recommended = false;
            s.Title = $"{s.Base.Label} · {s.ItemsPerUnit} produits";
        }

        list = list
            .OrderByDescending(s => Math.Round(s.TransportDensity, 1))
            .ThenBy(s => Overhang(s) > Geometry.Eps ? 1 : 0)
            .ThenByDescending(s => s.FirstUnit!.Metrics.FillRate)
            .ThenBy(s => s.Base.PhysicalCount)
            .ToList();
        if (list.Count > 0)
        {
            var best = list[0];
            best.Recommended = true;
            best.Recommendation = $"Meilleure densité de transport : {best.TransportDensity.ToString("0.#", Fr)} produits/m² au sol " +
                                  $"({best.ItemsPerUnit} produits × (1 + {best.Stackings} gerbage(s)) sur {best.FirstUnit!.Metrics.EnclosureLength:0} × {best.FirstUnit.Metrics.EnclosureWidth:0} mm)" +
                                  (best.Base.PhysicalCount > 1 ? $", {best.Base.PhysicalCount} palettes physiques." : ".");
        }

        result.Solutions.AddRange(list);
        if (list.Count == 0)
        {
            result.Messages.Add("Aucune palette active ne permet de conditionner cet article avec les contraintes saisies.");
        }

        return result;
    }

    private static double Overhang(Solution s)
    {
        var m = s.FirstUnit!.Metrics;
        return Math.Max(Math.Max(-m.MinX, m.MaxX - s.Base.Length), Math.Max(-m.MinY, m.MaxY - s.Base.Width));
    }

    private static (double a, double b) Footprint(Article a, CoilAxis axis) => a.Kind switch
    {
        ArticleKind.Fut => (a.Diameter, a.Diameter),
        ArticleKind.Tube or ArticleKind.Bobine => axis == CoilAxis.Horizontal ? (a.Diameter, a.AxisLength) : (a.Diameter, a.Diameter),
        _ => a.Orientation == OrientationRule.Libre && ArticleSchema.UsesOrientation(a.Kind)
            ? SmallestFace(a)
            : (Math.Min(a.Length, a.Width), Math.Max(a.Length, a.Width))
    };

    private static (double, double) SmallestFace(Article a)
    {
        var d = new[] { a.Length, a.Width, a.Height }.OrderBy(v => v).ToArray();
        return (d[0], d[1]);
    }

    private static bool Fits(double a, double b, double x, double y) => (a <= x + 1e-6 && b <= y + 1e-6) || (b <= x + 1e-6 && a <= y + 1e-6);

    // ------------------------------------------------------------------ Contexte

    private sealed class Context
    {
        public Context(Article article, BaseInfo baseInfo, PackagingConstraints c, int? target)
        {
            Article = article;
            Base = baseInfo;
            C = c;
            Target = target is > 0 ? target : null;
            UsableX = baseInfo.Length + 2 * (c.OverhangLength - c.CornerInset);
            UsableY = baseInfo.Width + 2 * (c.OverhangWidth - c.CornerInset);
            OriginX = c.CornerInset - c.OverhangLength;
            OriginY = c.CornerInset - c.OverhangWidth;
            UsefulHeight = c.MaxTotalHeight - baseInfo.PalletHeight - c.CapHeight;
            MaxWeight = c.MaxLoadWeight > 0 ? c.MaxLoadWeight : baseInfo.DynamicCapacity > 0 ? baseInfo.DynamicCapacity : double.MaxValue;
            Axis = c.ForcedAxis ?? article.CoilAxis;
        }

        /// <summary>Axe retenu pour les tubes et bobines.</summary>
        public CoilAxis Axis { get; }

        /// <summary>Intercalaire sous la couche d'indice k (0 = sur la palette).</summary>
        public bool SheetBelow(int k) => C.SlipSheetThickness > 0 &&
                                         (k == 0 ? C.SlipSheetOnPallet : k % Math.Max(1, C.SlipSheetEvery) == 0);

        public Article Article { get; }
        public BaseInfo Base { get; }
        public PackagingConstraints C { get; }
        public int? Target { get; }
        public double UsableX { get; }
        public double UsableY { get; }
        public double UsefulHeight { get; }

        /// <summary>Coin de la surface utile dans le repère de la base (débords, retrait des cornières contenues).</summary>
        public double OriginX { get; }

        public double OriginY { get; }
        public double MaxWeight { get; }
        public bool HasOverhang => C.OverhangLength > Geometry.Eps || C.OverhangWidth > Geometry.Eps;

        /// <summary>Les pertes de résistance du §6.2 concernent les emballages carton.</summary>
        public bool IsCardboard => Article.Kind is ArticleKind.Caisse or ArticleKind.Autre or ArticleKind.Sac;
    }

    // ------------------------------------------------------------------ Pavés

    private static void AddBoxes(Context ctx, EngineResult result)
    {
        var a = ctx.Article;
        var orientations = new List<(double a, double b, double h, string text)> { (a.Length, a.Width, a.Height, a.Kind == ArticleKind.Plaque ? "À plat" : "Haut en haut") };
        if (a.Orientation == OrientationRule.Libre && ArticleSchema.UsesOrientation(a.Kind))
        {
            orientations.Add((a.Length, a.Height, a.Width, "Couché (largeur verticale)"));
            orientations.Add((a.Width, a.Height, a.Length, "Debout (longueur verticale)"));
        }

        var seen = new HashSet<string>();
        foreach (var o in orientations)
        {
            var key = $"{Math.Min(o.a, o.b):0.###}|{Math.Max(o.a, o.b):0.###}|{o.h:0.###}";
            if (!seen.Add(key) || o.h > ctx.UsefulHeight + 1e-6)
            {
                continue;
            }

            var layer = RectLayerSolver.Solve(ctx.UsableX, ctx.UsableY, o.a, o.b, ctx.C.Gap);
            if (layer.Best.Count == 0)
            {
                continue;
            }

            var patterns = new List<LayerPattern> { layer.Best };
            if (layer.Grid.Count > 0 && layer.Grid.Count < layer.Best.Count && layer.Grid.Count >= layer.Best.Count * 0.85)
            {
                patterns.Add(layer.Grid);
            }

            foreach (var pattern in patterns)
            {
                var desc = $"{o.text} · plan « {pattern.Kind} »";
                var column = BuildLayered(ctx, pattern, null, o.h, ShapeKind.Box, StackPattern.Colonne, desc, layer.UpperBound);
                if (column != null)
                {
                    result.Solutions.Add(column);
                }

                if (a.Kind is ArticleKind.Plaque or ArticleKind.Bac)
                {
                    continue;
                }

                var (variant, _) = BestInterlock(pattern, ctx.C.MinSupportPercent / 100);
                if (variant != null)
                {
                    var crossed = BuildLayered(ctx, pattern, variant, o.h, ShapeKind.Box, StackPattern.Croise, desc, layer.UpperBound);
                    if (crossed != null)
                    {
                        result.Solutions.Add(crossed);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Variante B d'un plan de couche (miroirs, rotation 180°, rotation 90° si carré) maximisant l'imbrication,
    /// avec un taux de support minimal garanti (étude §4.7).
    /// </summary>
    internal static (LayerPattern? variant, double interlock) BestInterlock(LayerPattern a, double minSupport)
    {
        if (a.Count < 2)
        {
            return (null, 0);
        }

        var ux = a.UsedX;
        var uy = a.UsedY;
        var candidates = new List<List<Rect2>>
        {
            a.Items.Select(r => r with { X = ux - r.X - r.W }).ToList(),
            a.Items.Select(r => r with { Y = uy - r.Y - r.H }).ToList(),
            a.Items.Select(r => new Rect2(ux - r.X - r.W, uy - r.Y - r.H, r.W, r.H)).ToList()
        };
        if (Math.Abs(ux - uy) < 0.5)
        {
            candidates.Add(a.Items.Select(r => new Rect2(r.Y, ux - r.X - r.W, r.H, r.W)).ToList());
        }

        List<Rect2>? best = null;
        double bestScore = 0;
        foreach (var cand in candidates)
        {
            if (SameLayout(a.Items, cand))
            {
                continue;
            }

            var interlocked = 0;
            var minRatio = 1.0;
            foreach (var r in cand)
            {
                var area = r.W * r.H;
                double covered = 0;
                var supporters = 0;
                foreach (var q in a.Items)
                {
                    var o = Geometry.OverlapLength(r.X, r.Right, q.X, q.Right) * Geometry.OverlapLength(r.Y, r.Top, q.Y, q.Top);
                    if (o > 0)
                    {
                        covered += o;
                        if (o > area * 0.05)
                        {
                            supporters++;
                        }
                    }
                }

                minRatio = Math.Min(minRatio, covered / area);
                if (supporters >= 2)
                {
                    interlocked++;
                }
            }

            var score = interlocked / (double)cand.Count;
            if (minRatio >= minSupport - 1e-9 && score > bestScore + 1e-9)
            {
                bestScore = score;
                best = cand;
            }
        }

        return best == null || bestScore <= 0
            ? (null, 0)
            : (new LayerPattern { Kind = a.Kind + " (B)", Items = best, Circles = a.Circles }, bestScore);
    }

    private static bool SameLayout(List<Rect2> a, List<Rect2> b)
    {
        static string Key(Rect2 r) => $"{Math.Round(r.X)}|{Math.Round(r.Y)}|{Math.Round(r.W)}|{Math.Round(r.H)}";
        var set = a.Select(Key).ToHashSet();
        return b.All(r => set.Contains(Key(r)));
    }

    // ------------------------------------------------------------------ Cylindres debout

    private static void AddUprightCylinders(Context ctx, EngineResult result)
    {
        var a = ctx.Article;
        var h = a.AxisLength;
        if (h > ctx.UsefulHeight + 1e-6)
        {
            return;
        }

        var layer = CircleLayerSolver.Solve(ctx.UsableX, ctx.UsableY, a.Diameter, ctx.C.Gap);
        if (layer.Best.Count == 0)
        {
            return;
        }

        var text = a.Kind == ArticleKind.Fut ? "Debout" : a.Kind == ArticleKind.Tube ? "Tube debout (axe vertical)" : "Axe vertical";
        var best = BuildLayered(ctx, layer.Best, null, h, ShapeKind.CylinderZ, StackPattern.Colonne,
            $"{text} · maille « {layer.Best.Kind} »", layer.UpperBound);
        if (best != null)
        {
            if (a.Kind == ArticleKind.Tube && h > 3 * a.Diameter)
            {
                best.Warnings.Add(ctx.C.Corners
                    ? "Tubes debout élancés : cornières en place, cerclage conseillé."
                    : "Tubes debout élancés : cornières (coins) et cerclage indispensables pour éviter la chute.");
            }

            result.Solutions.Add(best);
        }

        if (layer.Square.Count > 0 && layer.Square.Count < layer.Best.Count)
        {
            var square = BuildLayered(ctx, layer.Square, null, h, ShapeKind.CylinderZ, StackPattern.Colonne,
                $"{text} · maille « Carré »", layer.UpperBound);
            if (square != null)
            {
                result.Solutions.Add(square);
            }
        }
    }

    // ------------------------------------------------------------------ Empilement par couches

    private static Solution? BuildLayered(Context ctx, LayerPattern a, LayerPattern? b, double layerHeight, ShapeKind shape,
        StackPattern pattern, string description, int upperBound)
    {
        var article = ctx.Article;
        var n = a.Count;
        var c = ctx.C;
        var factor = pattern == StackPattern.Croise ? 0.5 : 1.0;
        if (ctx.IsCardboard && ctx.HasOverhang)
        {
            factor *= 0.7;
        }

        // Limite de hauteur : couche par couche, intercalaires compris.
        var byHeight = 0;
        double z = 0;
        while (true)
        {
            var sheet = ctx.SheetBelow(byHeight) ? c.SlipSheetThickness : 0;
            if (z + sheet + layerHeight > ctx.UsefulHeight + 1e-6)
            {
                break;
            }

            z += sheet + layerHeight;
            byHeight++;
        }

        if (byHeight == 0)
        {
            return null;
        }

        var limits = new List<(int layers, string reason)> { (byHeight, "hauteur maximale") };
        var byWeight = ctx.MaxWeight >= double.MaxValue ? int.MaxValue : (int)Math.Floor(ctx.MaxWeight / (n * article.Weight) + 1e-9);
        limits.Add((byWeight, "poids maximal"));
        if (article.MaxLayers is { } ml)
        {
            limits.Add((ml, "couches maxi de l'article"));
        }

        if (article.EffectiveMaxLoadOnTop is { } top)
        {
            limits.Add((1 + (int)Math.Floor(top * factor / article.Weight + 1e-9), "résistance (charge maxi sur le dessus)"));
        }

        var (layers, reason) = limits.OrderBy(l => l.layers).First();
        var total = layers * n;

        // Dernière couche incomplète : poids (si autorisé) ou quantité imposée.
        if (c.AllowPartialLayer && reason == "poids maximal" && layers < byHeight)
        {
            var byWeightItems = (int)Math.Floor(ctx.MaxWeight / article.Weight + 1e-9);
            var capped = limits.Where(l => l.reason != "poids maximal").Min(l => l.layers) * n;
            total = Math.Min(byWeightItems, capped);
            layers = (int)Math.Ceiling(total / (double)n);
        }

        if (ctx.Target is { } target && target < total)
        {
            total = target;
            layers = (int)Math.Ceiling(total / (double)n);
            reason = "quantité imposée";
        }

        if (total <= 0)
        {
            return null;
        }

        var sx = c.CenterLoad ? (ctx.UsableX - a.UsedX) / 2 : 0;
        var sy = c.CenterLoad ? (ctx.UsableY - a.UsedY) / 2 : 0;
        var ox = ctx.OriginX + sx;
        var oy = ctx.OriginY + sy;

        var unit = new LoadUnit();
        z = 0;
        var placed = 0;
        var sheets = 0;
        for (var k = 0; k < layers; k++)
        {
            var sheetBelow = ctx.SheetBelow(k);
            if (sheetBelow)
            {
                z += c.SlipSheetThickness;
                sheets++;
            }

            var current = b != null && k % 2 == 1 ? b : a;
            var rects = current.Items;
            var remaining = total - placed;
            if (remaining < rects.Count)
            {
                rects = PartialSubset(rects, remaining);
            }

            foreach (var r in rects.OrderBy(r => Math.Round(r.Y)).ThenBy(r => r.X))
            {
                unit.Items.Add(new Placement
                {
                    ArticleId = article.Id,
                    X = ox + r.X,
                    Y = oy + r.Y,
                    Z = z,
                    DX = r.W,
                    DY = r.H,
                    DZ = layerHeight,
                    Shape = shape,
                    Layer = k + 1,
                    Sequence = ++placed,
                    Weight = article.Weight
                });
            }

            unit.Layers.Add(new LayerInfo
            {
                Index = k + 1,
                Z = z,
                Height = layerHeight,
                Pattern = ReferenceEquals(current, b) ? "B" : "A",
                ArticleId = article.Id,
                Count = rects.Count,
                SlipSheetBelow = sheetBelow
            });
            z += layerHeight;
        }

        var s = NewSolution(ctx, unit, pattern, description, upperBound, n, layers, factor, reason, sheets, total < layers * n);
        return s;
    }

    /// <summary>Couche incomplète : positions du pourtour d'abord (appui des couches suivantes, CdG centré).</summary>
    private static List<Rect2> PartialSubset(List<Rect2> rects, int count)
    {
        var cx = rects.Average(r => r.X + r.W / 2);
        var cy = rects.Average(r => r.Y + r.H / 2);
        return rects.OrderByDescending(r => Math.Pow(r.X + r.W / 2 - cx, 2) + Math.Pow(r.Y + r.H / 2 - cy, 2)).Take(count).ToList();
    }

    // ------------------------------------------------------------------ Cylindres couchés (lits)

    private static void AddBeds(Context ctx, EngineResult result)
    {
        var a = ctx.Article;
        var d = a.Diameter;
        var len = a.AxisLength;
        if (d > ctx.UsefulHeight + 1e-6)
        {
            return;
        }

        foreach (var alongX in new[] { true, false })
        {
            var lax = alongX ? ctx.UsableX : ctx.UsableY;
            var lcross = alongX ? ctx.UsableY : ctx.UsableX;
            var g = ctx.C.Gap;
            var nAxis = (int)Math.Floor((lax + g) / (len + g) + 1e-9);
            var n1 = (int)Math.Floor((lcross + g) / (d + g) + 1e-9);
            if (nAxis == 0 || n1 == 0)
            {
                continue;
            }

            var n2 = (int)Math.Floor((lcross + g - (d + g) / 2) / (d + g) + 1e-9);
            var axisText = a.Kind == ArticleKind.Bobine ? "Axe horizontal" : "Couché";
            var dirText = alongX ? "axe dans la longueur" : "axe dans la largeur";
            var square = BuildBed(ctx, alongX, nAxis, n1, n1, d, false, $"{axisText}, {dirText} · lits superposés");
            if (square != null)
            {
                result.Solutions.Add(square);
            }

            if (n2 > 0 && (a.Kind != ArticleKind.Bobine || (a.MaxLayers ?? 1) > 1))
            {
                var hex = BuildBed(ctx, alongX, nAxis, n1, n2, d, true, $"{axisText}, {dirText} · lits en quinconce");
                if (hex != null)
                {
                    result.Solutions.Add(hex);
                }
            }
        }
    }

    private static Solution? BuildBed(Context ctx, bool alongX, int nAxis, int n1, int n2, double d, bool staggered, string description)
    {
        var a = ctx.Article;
        var c = ctx.C;
        var g = c.Gap;
        var pitch = staggered ? d * HexPitch : d;

        // Intercalaires : possibles entre lits superposés ; en quinconce, seul celui posé sur la palette est retenu
        // (un intercalaire empêcherait l'emboîtement des lits).
        bool Sheet(int level) => level == 0 ? ctx.SheetBelow(0) : !staggered && ctx.SheetBelow(level);

        var levelZ = new List<double>();
        double z = 0;
        while (true)
        {
            var level = levelZ.Count;
            var next = level == 0 ? (Sheet(0) ? c.SlipSheetThickness : 0) : z + pitch + (Sheet(level) ? c.SlipSheetThickness : 0);
            if (next + d > ctx.UsefulHeight + 1e-6)
            {
                break;
            }

            levelZ.Add(next);
            z = next;
        }

        var byHeight = levelZ.Count;
        if (byHeight <= 0)
        {
            return null;
        }

        int Count(int level) => nAxis * (level % 2 == 1 ? n2 : n1);

        var limits = new List<(int layers, string reason)> { (byHeight, "hauteur maximale") };
        var maxLayers = a.MaxLayers ?? (a.Kind == ArticleKind.Bobine ? 1 : int.MaxValue);
        limits.Add((maxLayers, a.MaxLayers.HasValue ? "couches maxi de l'article" : "bobines couchées : une couche (calage)"));
        if (a.EffectiveMaxLoadOnTop is { } top)
        {
            limits.Add((1 + (int)Math.Floor(top / a.Weight + 1e-9), "résistance (charge maxi sur le dessus)"));
        }

        // Poids : lits cumulés tant que le poids maximal n'est pas dépassé.
        var byWeight = 0;
        double weight = 0;
        while (byWeight < byHeight && weight + Count(byWeight) * a.Weight <= ctx.MaxWeight + 1e-9)
        {
            weight += Count(byWeight) * a.Weight;
            byWeight++;
        }

        limits.Add((byWeight, "poids maximal"));
        var (levels, reason) = limits.OrderBy(l => l.layers).First();
        var total = Enumerable.Range(0, Math.Max(0, levels)).Sum(Count);
        if (ctx.Target is { } target && target < total)
        {
            total = target;
            levels = 0;
            for (var acc = 0; acc < total; levels++)
            {
                acc += Count(levels);
            }

            reason = "quantité imposée";
        }

        if (total <= 0 || levels <= 0)
        {
            return null;
        }

        var lax = alongX ? ctx.UsableX : ctx.UsableY;
        var lcross = alongX ? ctx.UsableY : ctx.UsableX;
        var len = a.AxisLength;
        var usedAxis = nAxis * (len + g) - g;
        var usedCross = Math.Max(n1 * (d + g) - g, levels > 1 && staggered ? n2 * (d + g) - g + (d + g) / 2 : 0);
        var sAxis = c.CenterLoad ? (lax - usedAxis) / 2 : 0;
        var sCross = c.CenterLoad ? (lcross - usedCross) / 2 : 0;
        var oAxis = (alongX ? ctx.OriginX : ctx.OriginY) + sAxis;
        var oCross = (alongX ? ctx.OriginY : ctx.OriginX) + sCross;

        var unit = new LoadUnit();
        var placed = 0;
        var sheets = 0;
        for (var level = 0; level < levels; level++)
        {
            var offset = staggered && level % 2 == 1;
            var nRow = offset ? n2 : n1;
            var lz = levelZ[level];
            var count = 0;
            if (Sheet(level))
            {
                sheets++;
            }

            for (var i = 0; i < nAxis && placed < total; i++)
            {
                for (var j = 0; j < nRow && placed < total; j++)
                {
                    var u = oAxis + i * (len + g);
                    var v = oCross + j * (d + g) + (offset ? (d + g) / 2 : 0);
                    unit.Items.Add(new Placement
                    {
                        ArticleId = a.Id,
                        X = alongX ? u : v,
                        Y = alongX ? v : u,
                        Z = lz,
                        DX = alongX ? len : d,
                        DY = alongX ? d : len,
                        DZ = d,
                        Shape = alongX ? ShapeKind.CylinderX : ShapeKind.CylinderY,
                        Layer = level + 1,
                        Sequence = ++placed,
                        Weight = a.Weight
                    });
                    count++;
                }
            }

            unit.Layers.Add(new LayerInfo
            {
                Index = level + 1,
                Z = lz,
                Height = d,
                Pattern = offset ? "B" : "A",
                ArticleId = a.Id,
                Count = count,
                SlipSheetBelow = Sheet(level)
            });
        }

        if (c.Corners && !c.CornersFollowTubes && a.Kind == ArticleKind.Tube)
        {
            PalletLevelCorners(ctx, unit, alongX, d + g, staggered);
            total = unit.Items.Count;
            if (total == 0)
            {
                return null;
            }
        }

        var upper = staggered ? 0 : nAxis * n1;
        var s = NewSolution(ctx, unit, staggered ? StackPattern.Quinconce : StackPattern.Colonne, description, upper,
            nAxis * n1, levels, 1, reason, sheets, total < Enumerable.Range(0, levels).Sum(Count));
        if (unit.RemovedForCorners > 0)
        {
            s.Warnings.Add($"Tubes en débord : cornières au niveau de la palette, {unit.RemovedForCorners} tube(s) retiré(s) pour leur laisser la place " +
                           "(cochez « les cornières suivent les tubes » pour les placer au bout des tubes).");
        }

        if (a.Kind == ArticleKind.Bobine)
        {
            s.Warnings.Add(c.Corners
                ? "Bobines couchées : cornières en place, compléter par des cales sous le premier rang."
                : "Bobines couchées : calage obligatoire (berceaux, cales ou cornières) pour empêcher le roulement.");
        }
        else if (!c.Corners)
        {
            s.Warnings.Add(staggered && n2 < n1
                ? "Tubes couchés en quinconce : ajouter des cornières (coins) ou des montants pour bloquer les lits."
                : "Tubes couchés : cornières (coins) conseillées pour empêcher le roulement latéral.");
        }

        if (staggered && c.SlipSheetThickness > 0 && levels > 1)
        {
            s.Warnings.Add("Quinconce : pas d'intercalaire entre les lits (il empêcherait l'emboîtement).");
        }

        return s;
    }

    /// <summary>
    /// Tubes couchés qui dépassent de la palette dans le sens de leur axe : les cornières ne tiendraient pas au bout des
    /// tubes, elles restent donc au niveau de la palette (aux extrémités de la base, de part et d'autre du lit). Leur aile
    /// transversale occupe alors une bande du lit : les tubes qui la traversent sont retirés, puis ceux qui perdent leur
    /// appui (lits en quinconce). Sans débord, rien ne change.
    /// </summary>
    private static void PalletLevelCorners(Context ctx, LoadUnit unit, bool alongX, double pitch, bool staggered)
    {
        var c = ctx.C;
        if (unit.Items.Count == 0)
        {
            return;
        }

        double AxisMin(Placement p) => alongX ? p.X : p.Y;
        double AxisMax(Placement p) => alongX ? p.MaxX : p.MaxY;
        double CrossMin(Placement p) => alongX ? p.Y : p.X;
        double CrossMax(Placement p) => alongX ? p.MaxY : p.MaxX;
        double CrossMid(Placement p) => (CrossMin(p) + CrossMax(p)) / 2;

        var baseAxis = alongX ? ctx.Base.Length : ctx.Base.Width;
        var overhang = unit.Items.Min(AxisMin) < -0.5 || unit.Items.Max(AxisMax) > baseAxis + 0.5;
        if (!overhang)
        {
            return;
        }

        var crossMin = unit.Items.Min(CrossMin);
        var crossMax = unit.Items.Max(CrossMax);
        var t = Math.Max(0, c.CornerThickness);
        var leg = Math.Max(t, c.CornerLeg);
        var height = c.CornerHeight > 0 ? c.CornerHeight : unit.Items.Max(p => p.MaxZ);
        // Cadre intérieur des cornières : leur face extérieure affleure les extrémités de la palette (aile transversale sur [0, t]).
        unit.CornerFrame = alongX
            ? new CornerFrame { X0 = t, X1 = baseAxis - t, Y0 = crossMin, Y1 = crossMax }
            : new CornerFrame { X0 = crossMin, X1 = crossMax, Y0 = t, Y1 = baseAxis - t };

        // Ailes transversales aux deux extrémités de la palette : bandes [crossMin, crossMin + aile] et [crossMax − aile, crossMax].
        bool InLegBand(Placement p) =>
            p.Z < height - 0.5 &&
            (Geometry.OverlapLength(CrossMin(p), CrossMax(p), crossMin, crossMin + leg) > 0.5 ||
             Geometry.OverlapLength(CrossMin(p), CrossMax(p), crossMax - leg, crossMax) > 0.5) &&
            (Geometry.OverlapLength(AxisMin(p), AxisMax(p), 0, t) > 0.5 ||
             Geometry.OverlapLength(AxisMin(p), AxisMax(p), baseAxis - t, baseAxis) > 0.5);

        var removed = unit.Items.RemoveAll(InLegBand);

        // Appuis : un tube posé repose sur 1 tube (lits superposés) ou 2 tubes (quinconce) du lit inférieur.
        var needed = staggered ? 2 : 1;
        bool changed;
        do
        {
            changed = false;
            foreach (var p in unit.Items.Where(p => p.Layer > 1).OrderBy(p => p.Layer).ToList())
            {
                var supports = unit.Items.Count(q => q.Layer == p.Layer - 1 &&
                                                     Geometry.OverlapLength(AxisMin(q), AxisMax(q), AxisMin(p), AxisMax(p)) > 0.5 &&
                                                     Math.Abs(CrossMid(q) - CrossMid(p)) < pitch * 0.75);
                if (supports < needed)
                {
                    unit.Items.Remove(p);
                    removed++;
                    changed = true;
                }
            }
        }
        while (changed);

        unit.RemovedForCorners = removed;
        var seq = 0;
        foreach (var p in unit.Items.OrderBy(p => p.Sequence))
        {
            p.Sequence = ++seq;
        }

        foreach (var layer in unit.Layers)
        {
            layer.Count = unit.Items.Count(p => p.Layer == layer.Index);
        }

        unit.Layers.RemoveAll(l => l.Count == 0);
    }

    // ------------------------------------------------------------------ Solution, gerbage, contrôles

    private static Solution NewSolution(Context ctx, LoadUnit unit, StackPattern pattern, string description, int upperBound,
        int perLayer, int layers, double factor, string reason, int sheets, bool partialTop)
    {
        var a = ctx.Article;
        var c = ctx.C;
        var s = new Solution
        {
            Kind = PackagingKind.Homogene,
            Pattern = pattern,
            Base = ctx.Base,
            ArticleId = a.Id,
            ItemsPerLayer = perLayer,
            LayerCount = layers,
            ItemsPerUnit = unit.Items.Count,
            UpperBound = upperBound,
            LayerPatternKind = description,
            OrientationText = description.Split(" · ")[0],
            Description = description,
            StrengthFactor = factor,
            LayerLimitReason = reason,
            SlipSheets = sheets,
            CapHeight = c.CapHeight,
            RequestedItems = ctx.Target ?? unit.Items.Count
        };
        s.Units.Add(unit);
        unit.Metrics = MetricsCalculator.Compute(unit, ctx.Base, c);
        var m = unit.Metrics;
        (s.StackLevels, s.StackLimitReason) = StackLevels(ctx, s, factor);
        s.Title = $"{perLayer} × {layers} = {s.ItemsPerUnit} · {s.PatternLabel}";
        s.StabilityScore = Math.Clamp(m.SupportAvg / 100 * (1 - Math.Min(1, m.CogOffset / 100)), 0, 1);

        if (m.Slenderness > 2.5)
        {
            s.Warnings.Add($"Élancement élevé ({m.Slenderness.ToString("0.0", Fr)}) : filmage renforcé ou cerclage conseillé.");
        }

        if (m.CogOffset > 10)
        {
            s.Warnings.Add($"Centre de gravité décentré de {m.CogOffset.ToString("0", Fr)} % : charge à recentrer.");
        }

        if (ctx.IsCardboard && ctx.HasOverhang)
        {
            s.Warnings.Add("Débord des cartons : perte de résistance à la compression de 20 à 40 % (étude §6.2).");
        }

        if (pattern == StackPattern.Croise && ctx.IsCardboard)
        {
            s.Warnings.Add("Schéma croisé : résistance à la compression des cartons réduite d'environ 50 % (étude §6.2).");
        }

        s.PartialTopLayer = partialTop;
        if (partialTop)
        {
            s.Warnings.Add("Dernière couche incomplète : produits placés en périphérie.");
        }

        if (upperBound > perLayer && s.Kind == PackagingKind.Homogene && pattern != StackPattern.Quinconce && unit.Items.All(p => p.Shape == ShapeKind.Box))
        {
            s.Warnings.Add($"Plan de couche non prouvé optimal : {perLayer} produits pour une borne théorique de {upperBound}.");
        }

        var perPallet = m.LoadWeight / ctx.Base.PhysicalCount;
        if (ctx.Base.PalletDynamicLoad > 0 && perPallet > ctx.Base.PalletDynamicLoad + 1e-6)
        {
            s.Violations.Add($"Poids par palette physique {perPallet:0} kg > charge dynamique {ctx.Base.PalletDynamicLoad:0} kg.");
        }

        if (ctx.Target is { } target && target > s.ItemsPerUnit)
        {
            s.Violations.Add($"Quantité imposée {target} non atteignable : {s.ItemsPerUnit} au maximum ({reason}).");
        }

        s.Violations.AddRange(SolutionValidator.Validate(s, c, checkSupport: unit.Items.All(p => p.Shape is ShapeKind.Box or ShapeKind.CylinderZ)));
        return s;
    }

    /// <summary>Niveaux de gerbage (étude §10.4) : saisie, hauteur gerbée, résistance du produit du bas, charge statique.</summary>
    private static (int levels, string reason) StackLevels(Context ctx, Solution s, double factor)
    {
        var c = ctx.C;
        var a = ctx.Article;
        var m = s.FirstUnit!.Metrics;
        var limits = new List<(int, string)> { (Math.Max(1, c.MaxStackLevels), "gerbages maxi saisis") };
        if (c.MaxStackedHeight > 0 && m.EnclosureHeight > 0)
        {
            limits.Add((Math.Max(1, (int)Math.Floor(c.MaxStackedHeight / m.EnclosureHeight + 1e-9)), "hauteur gerbée maxi"));
        }

        var unitWeight = m.TotalWeight;
        var topLayerCount = Math.Max(1, s.FirstUnit.Layers.LastOrDefault()?.Count ?? 1);
        if (a.EffectiveMaxLoadOnTop is { } top)
        {
            var own = (s.LayerCount - 1) * a.Weight;
            var capacity = top * factor;
            var k = 1;
            while (k < 20 && own + k * unitWeight / topLayerCount <= capacity + 1e-9)
            {
                k++;
            }

            limits.Add((k, "résistance du produit du bas"));
        }

        if (s.PartialTopLayer)
        {
            limits.Add((1, "dernière couche incomplète"));
        }

        if (ctx.Base.StaticCapacity > 0)
        {
            var k = 1;
            while (k < 20 && m.LoadWeight + k * unitWeight <= ctx.Base.StaticCapacity + 1e-9)
            {
                k++;
            }

            limits.Add((k, "charge statique de la palette"));
        }

        var best = limits.OrderBy(l => l.Item1).First();
        return best;
    }

    // ------------------------------------------------------------------ Classement (étude §11.1)

    private static void Deduplicate(List<Solution> list)
    {
        var seen = new HashSet<string>();
        list.RemoveAll(s =>
        {
            var key = string.Join(";", s.FirstUnit!.Items.Select(p => $"{Math.Round(p.X)},{Math.Round(p.Y)},{Math.Round(p.Z)},{Math.Round(p.DX)},{Math.Round(p.DY)}"));
            return !seen.Add(key);
        });
    }

    internal static StackPattern PreferredPattern(Article a, Solution crossed)
    {
        switch (a.Kind)
        {
            case ArticleKind.Sac:
                return StackPattern.Croise;
            case ArticleKind.Bac:
            case ArticleKind.Plaque:
            case ArticleKind.Fut:
            case ArticleKind.Bobine:
                return StackPattern.Colonne;
        }

        if (a.Fragile)
        {
            return StackPattern.Colonne;
        }

        if (a.MaxLoadOnTop is { } top && top > 0)
        {
            var used = (crossed.LayerCount - 1) * a.Weight / (top * 0.5);
            if (used > 0.6)
            {
                return StackPattern.Colonne;
            }
        }

        var m = crossed.FirstUnit!.Metrics;
        return m.Interlock >= 50 && m.Slenderness >= 1.5 ? StackPattern.Croise : StackPattern.Colonne;
    }

    private static void Rank(List<Solution> list, Article a)
    {
        foreach (var s in list)
        {
            var m = s.FirstUnit!.Metrics;
            var preferred = s.Pattern switch
            {
                StackPattern.Croise => PreferredPattern(a, s) == StackPattern.Croise,
                StackPattern.Colonne => !list.Any(o => o.Pattern == StackPattern.Croise && o.ItemsPerUnit == s.ItemsPerUnit &&
                                                       o.OrientationText == s.OrientationText && PreferredPattern(a, o) == StackPattern.Croise),
                _ => true
            };
            s.Score = (s.IsCompliant ? 1_000_000 : 0) + s.ItemsPerUnit * 1000 + (preferred ? 300 : 0) + m.FillRate * 2 +
                      s.StabilityScore * 100 + (s.ProvenOptimal ? 50 : 0) + Math.Min(s.StackLevels, 5) * 10;
        }

        list.Sort((x, y) => y.Score.CompareTo(x.Score));
        var best = list[0];
        best.Recommended = true;
        best.Recommendation = Explain(best, a, list);
    }

    private static string Explain(Solution s, Article a, List<Solution> all)
    {
        var m = s.FirstUnit!.Metrics;
        var parts = new List<string>
        {
            $"{s.ItemsPerUnit} produits par conditionnement ({s.ItemsPerLayer} par couche × {s.LayerCount} couche(s), limite : {s.LayerLimitReason})"
        };
        if (s.ProvenOptimal)
        {
            parts.Add($"plan de couche optimal prouvé (borne {s.UpperBound})");
        }

        parts.Add(s.Pattern switch
        {
            StackPattern.Croise => $"schéma croisé : imbrication {m.Interlock.ToString("0", Fr)} %, meilleure cohésion de la charge sans risque d'écrasement",
            StackPattern.Quinconce => "lits en quinconce : densité supérieure aux lits superposés",
            _ when a.Kind == ArticleKind.Sac => "colonne (aucun plan croisé possible pour ce plan de couche)",
            _ when all.Any(o => o.Pattern == StackPattern.Croise && o.ItemsPerUnit == s.ItemsPerUnit) =>
                "schéma colonne : conserve 100 % de la résistance à la compression (le croisé en ferait perdre environ la moitié)",
            _ => "schéma colonne"
        });
        if (s.Stackings > 0)
        {
            parts.Add($"{s.Stackings} gerbage(s) possible(s)");
        }

        return "Recommandée : " + string.Join(" ; ", parts) + ".";
    }
}
