using System.Globalization;
using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

/// <summary>Ce que la forme du dessus permet : poser directement, poser sur un intercalaire, ou rien.</summary>
public enum TopStackMode
{
    /// <summary>Produit posé directement sur le produit du dessous.</summary>
    Direct,

    /// <summary>Gerbage seulement sur un intercalaire (carton fort, plateau) sous chaque couche.</summary>
    Intercalaire,

    /// <summary>Rien ne peut être posé dessus : une seule couche.</summary>
    NonGerbable
}

/// <summary>
/// Règle de gerbage déduite de la forme du dessus : mode, part du dessus réellement en appui, coefficient appliqué à la
/// charge admissible sur le dessus, et le raisonnement pas à pas (affiché dans les détails du calcul).
/// </summary>
public sealed record TopStackRule(TopStackMode Mode, double SupportPercent, double CapacityFactor, string Summary, IReadOnlyList<string> Steps)
{
    public string ModeLabel => Mode switch
    {
        TopStackMode.Direct => "gerbable directement",
        TopStackMode.Intercalaire => "gerbable sur intercalaire seulement",
        _ => "non gerbable"
    };
}

/// <summary>
/// Gerbage des produits à poignée, à anse ou à col (bidon / jerrican, seau, bouteille) selon la forme de leur dessus.
/// </summary>
/// <remarks>
/// Fondements (repris dans les détails du calcul) :
/// <list type="bullet">
/// <item>Glissement sur un plan incliné : un produit posé sur une surface inclinée de θ glisse dès que tan θ &gt; μ,
/// μ étant le coefficient de frottement statique. PEHD sur PEHD : μ ≈ 0,2 à 0,3 (retenu 0,25, soit 14,0°) ; carton ou
/// kraft sur PEHD : μ ≈ 0,35 à 0,45 (retenu 0,40, soit 21,8°).</item>
/// <item>Appui : un produit posé sur un dessus incliné ne touche plus que les arêtes hautes ; la surface en appui
/// décroît avec l'angle jusqu'à s'annuler à l'angle de glissement. Un dessus bombé (arrondi) ne porte que sur sa
/// partie haute : la moitié de l'appui d'un dessus droit de même angle.</item>
/// <item>Poignée saillante, col de bouteille : le point haut est la poignée ou le bouchon. Rien ne peut reposer dessus
/// directement ; un intercalaire rigide répartit la charge sur ces points hauts, dont la résistance est moindre que
/// celle des parois (charge admissible divisée par deux pour une poignée saillante).</item>
/// <item>Essai de gerbage des emballages de transport (UN / ADR 6.1.5.6 : charge équivalente à 3 m de gerbage pendant
/// 24 h, 28 jours à 40 °C pour les plastiques) : il qualifie la résistance des parois — c'est la charge admissible saisie
/// sur l'article, que la forme du dessus vient réduire quand l'appui est partiel.</item>
/// <item>En transport (EN 12195-1 : 0,5 g latéral, 0,8 g longitudinal), le frottement seul ne retient pas une couche :
/// filmage ou cerclage restent nécessaires ; les règles ci-dessus portent sur la stabilité à la pose.</item>
/// </list>
/// </remarks>
public static class TopStacking
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Frottement statique PEHD sur PEHD (0,2 à 0,3).</summary>
    public const double FrictionPlastic = 0.25;

    /// <summary>Frottement statique carton / kraft sur PEHD (0,35 à 0,45).</summary>
    public const double FrictionBoard = 0.40;

    /// <summary>Tolérance de moulage : en dessous, un dessus droit est considéré plat.</summary>
    public const double FlatTolerance = 3;

    /// <summary>Au-delà, un dessus bombé ne touche l'intercalaire qu'en un point : rien ne peut y reposer.</summary>
    public const double MaxRoundAngle = 45;

    /// <summary>Appui minimal pour poser un produit directement sur un autre.</summary>
    public const double MinDirectSupport = 40;

    /// <summary>Angle de glissement (°) pour un coefficient de frottement : arctan μ.</summary>
    public static double SlideAngle(double mu) => Math.Atan(mu) * 180 / Math.PI;

    /// <summary>Types concernés : produits à poignée, à anse ou à col.</summary>
    public static bool Applies(ArticleKind kind) => kind is ArticleKind.Bidon or ArticleKind.Seau or ArticleKind.Bouteille or ArticleKind.Fut;

    /// <summary>Forme du dessus renseignée (sinon : comportement d'avant, intercalaire seulement conseillé).</summary>
    public static bool IsDefined(Article a) => Applies(a.Kind) && (a.TopShape != null || a.TopAngle != null || a.Handle != null ||
                                                                   a.HandleLength != null || a.HandleWidth != null || a.HandleHeight != null ||
                                                                   a.HandleAngleLeft != null || a.HandleAngleRight != null || a.HandleSolid);

    /// <summary>Forme effective : valeurs saisies, sinon valeurs usuelles du type.</summary>
    public static TopForm FormOf(Article a)
    {
        var shape = a.TopShape ?? (a.Kind == ArticleKind.Bouteille ? TopShape.Arrondi : TopShape.Droit);
        var handle = a.Handle ?? a.Kind switch
        {
            ArticleKind.Bidon => HandleKind.Encastree,
            ArticleKind.Seau => HandleKind.Rabattable,
            _ => HandleKind.Aucune
        };
        var angle = Math.Clamp(a.TopAngle ?? (shape == TopShape.Arrondi ? 30 : 0), 0, 90);
        var (refL, refW, refH) = a.HandleReference;
        static double? P(double? mm, double reference) => mm is { } x && reference > 0 ? Math.Clamp(x / reference * 100, 0, 100) : null;
        static double Side(double? v) => Math.Clamp(v ?? 0, 0, ArticleSchema.MaxHandleSideAngle);
        return new TopForm(shape, angle, handle, P(a.HandleLength, refL), P(a.HandleWidth, refW), P(a.HandleHeight, refH), a.HandleRounded == true,
            a.HandleRounded == true ? Side(a.HandleAngleLeft) : 0, a.HandleRounded == true ? Side(a.HandleAngleRight) : 0, a.HandleSolid,
            a.HandleLength, a.HandleWidth, a.HandleHeight);
    }

    /// <summary>
    /// Part du dessus d'un dessus droit et plat réellement en appui (puits de poignée, bouchon, rebord). Poignée encastrée
    /// dimensionnée : 100 % moins l'empreinte du puits (longueur % × largeur %).
    /// </summary>
    private static double FlatShare(ArticleKind kind, TopForm form) => kind switch
    {
        ArticleKind.Bouteille => 0,
        _ when form.Handle == HandleKind.Encastree && form.HandleLength is { } l && form.HandleWidth is { } w => Math.Max(0, 100 - l * w / 100),
        ArticleKind.Seau or ArticleKind.Fut => 85,
        _ => form.Handle == HandleKind.Encastree ? 70 : 85
    };

    /// <summary>Description de la poignée saisie : « 50 % × 16 % × 12 % (L × l × h), arrondie ».</summary>
    private static string HandleSize(TopForm f)
    {
        string M(double? mm, double? pct) => mm is { } x ? $"{x.ToString("0.#", Fr)} mm" : pct is { } p ? $"{p.ToString("0.#", Fr)} %" : "—";
        return f.HasHandleSize || f.HandleRounded
            ? $" ; poignée {M(f.HandleLengthMm, f.HandleLength)} × {M(f.HandleWidthMm, f.HandleWidth)} × {M(f.HandleHeightMm, f.HandleHeight)} (longueur × largeur × hauteur){(f.HandleRounded ? ", arrondie" : ", dessus plat")}" +
              (f.HandleRounded && (f.HandleAngleLeft > 0 || f.HandleAngleRight > 0) ? $", côtés inclinés de {f.HandleAngleLeft.ToString("0.#", Fr)}° et {f.HandleAngleRight.ToString("0.#", Fr)}°" : "") +
              (f.HandleSolid ? ", pleine" : "")
            : f.HandleSolid ? " ; poignée pleine" : "";
    }

    /// <summary>Règle de gerbage de l'article (null : type non concerné ou forme non renseignée).</summary>
    public static TopStackRule? For(Article a)
    {
        if (!IsDefined(a))
        {
            return null;
        }

        // Longueur et hauteur du produit : longueur réelle du dessus d'une poignée arrondie aux côtés inclinés.
        var length = a.Kind is ArticleKind.Fut or ArticleKind.Seau or ArticleKind.Bouteille ? a.Diameter : Math.Max(a.Length, a.Width);
        return For(a.Kind, FormOf(a), length, a.Height);
    }

    public static TopStackRule For(ArticleKind kind, TopForm form, double productLength = 0, double productHeight = 0)
    {
        var steps = new List<string>();
        var (shape, angle, handle) = (form.Shape, form.Angle, form.Handle);
        var slidePlastic = SlideAngle(FrictionPlastic);
        var slideBoard = SlideAngle(FrictionBoard);
        var tan = Math.Tan(angle * Math.PI / 180);
        string A(double v) => v.ToString("0.#", Fr);

        steps.Add($"Dessus {(shape == TopShape.Droit ? "droit" : "arrondi (bombé)")}, angle {A(angle)}° par rapport à l'horizontale ; poignée : {HandleLabel(handle)}{HandleSize(form)}.");
        steps.Add($"Glissement : un produit posé sur une pente de θ glisse si tan θ > μ. PEHD sur PEHD μ = {FrictionPlastic.ToString("0.00", Fr)} → " +
                  $"{A(slidePlastic)}° ; sur intercalaire carton μ = {FrictionBoard.ToString("0.00", Fr)} → {A(slideBoard)}°. Ici tan {A(angle)}° = {tan.ToString("0.00", Fr)}.");

        // Points hauts qui empêchent toute pose directe.
        if (kind == ArticleKind.Bouteille)
        {
            steps.Add("Bouteille : le point haut est le col et le bouchon ; rien ne repose directement dessus.");
            steps.Add("Avec un intercalaire sous chaque couche, la charge passe par les bouchons : c'est la charge admissible d'une bouteille (essai de compression bouchée), sans réduction.");
            return new(TopStackMode.Intercalaire, 0, 1, "col et bouchon : intercalaire sous chaque couche", steps);
        }

        if (form.HandleSolid)
        {
            steps.Add("Poignée pleine (moulée d'un seul tenant, sans ouverture) : même appui retenu qu'une poignée ouverte de mêmes dimensions.");
        }

        if (handle == HandleKind.Saillante)
        {
            steps.Add("Poignée saillante : elle dépasse le dessus, le produit du dessus reposerait sur la poignée et basculerait ; pose directe impossible.");
            if (shape == TopShape.Arrondi && angle > MaxRoundAngle)
            {
                steps.Add($"Dessus bombé à plus de {A(MaxRoundAngle)}° : même un intercalaire ne trouve qu'un point d'appui ; non gerbable.");
                return new(TopStackMode.NonGerbable, 0, 0, "poignée saillante et dessus très bombé : rien dessus", steps);
            }

            if (form.HandleLength is { } hl && form.HandleWidth is { } hw)
            {
                // L'intercalaire ne touche que le dessus des poignées : contact = empreinte de la poignée (arrondie : une ligne).
                // Côtés inclinés : le dessus est plus court que la base de h × (tan α gauche + tan α droit).
                var topLength = hl;
                if (form.HandleRounded && (form.HandleAngleLeft > 0 || form.HandleAngleRight > 0) && productLength > 0 && productHeight > 0)
                {
                    var hh = (form.HandleHeight ?? 16) / 100 * productHeight;
                    var shorten = hh * (Math.Tan(form.HandleAngleLeft * Math.PI / 180) + Math.Tan(form.HandleAngleRight * Math.PI / 180));
                    topLength = Math.Max(0, hl - shorten / productLength * 100);
                    steps.Add($"Côtés inclinés : dessus de la poignée = base − hauteur × (tan {A(form.HandleAngleLeft)}° + tan {A(form.HandleAngleRight)}°) = " +
                              $"{A(hl)} % − {A(shorten / productLength * 100)} % = {A(topLength)} % de la longueur du produit.");
                }

                hl = topLength;
                var contact = hl * hw / 100 * (form.HandleRounded ? 0.5 : 1);
                if (contact < 5)
                {
                    steps.Add($"Avec un intercalaire rigide, il ne repose que sur le dessus des poignées : {A(hl)} % × {A(hw)} %{(form.HandleRounded ? " × 0,5 (arrondie : contact sur une ligne)" : "")} = " +
                              $"{A(contact)} % du dessus, appui quasi ponctuel (< 5 %) : charge admissible × 0,3.");
                    return new(TopStackMode.Intercalaire, contact, 0.3, $"poignée saillante, contact {A(contact)} % : intercalaire rigide, charge × 0,3", steps);
                }

                steps.Add($"Avec un intercalaire rigide, il repose sur le dessus des poignées : {A(hl)} % × {A(hw)} %{(form.HandleRounded ? " × 0,5 (arrondie)" : "")} = {A(contact)} % du dessus ; " +
                          "poignées moins résistantes que les parois : charge admissible × 0,5.");
                return new(TopStackMode.Intercalaire, contact, 0.5, $"poignée saillante, contact {A(contact)} % : intercalaire rigide, charge × 0,5", steps);
            }

            steps.Add("Avec un intercalaire rigide, la charge repose sur les poignées et les bouchons, moins résistants que les parois : charge admissible × 0,5.");
            return new(TopStackMode.Intercalaire, 0, 0.5, "poignée saillante : intercalaire rigide, charge × 0,5", steps);
        }

        // Appui direct : part plate du dessus, réduite par la pente jusqu'à s'annuler à l'angle de glissement.
        var share = FlatShare(kind, form);
        if (form.Handle == HandleKind.Encastree && form.HandleLength is { } wl && form.HandleWidth is { } ww)
        {
            steps.Add($"Part plate du dessus = 100 % − puits de la poignée ({A(wl)} % de la longueur × {A(ww)} % de la largeur) = {A(share)} %.");
        }

        var slopeFactor = angle <= FlatTolerance ? 1 : Math.Max(0, 1 - tan / FrictionPlastic);
        var roundFactor = shape == TopShape.Arrondi ? 0.5 : 1;
        var support = share * slopeFactor * roundFactor;
        steps.Add($"Appui direct = part plate du dessus × réduction par la pente{(shape == TopShape.Arrondi ? " × 0,5 (bombé : seule la partie haute porte)" : "")} = " +
                  $"{A(share)} % × {(angle <= FlatTolerance ? $"1 (pente ≤ {A(FlatTolerance)}°, tolérance de moulage)" : $"(1 − {tan.ToString("0.00", Fr)} / {FrictionPlastic.ToString("0.00", Fr)}{(slopeFactor <= 0 ? ", au moins 0" : "")}) = {slopeFactor.ToString("0.00", Fr)}")}" +
                  $"{(shape == TopShape.Arrondi ? " × 0,5" : "")} = {A(support)} %.");

        if (tan <= FrictionPlastic && support >= MinDirectSupport)
        {
            var factor = Math.Round(support / share, 2);
            steps.Add($"Pente sous l'angle de glissement ({A(angle)}° ≤ {A(slidePlastic)}°) et appui ≥ {A(MinDirectSupport)} % : gerbage direct possible" +
                      (factor < 1 ? $" ; charge admissible × {factor.ToString("0.00", Fr)} (appui partiel)." : " ; charge admissible entière."));
            return new(TopStackMode.Direct, support, factor, factor < 1 ? $"gerbage direct, appui {A(support)} %, charge × {factor.ToString("0.00", Fr)}" : $"gerbage direct, appui {A(support)} %", steps);
        }

        steps.Add(tan > FrictionPlastic
            ? $"Pente au-delà de l'angle de glissement ({A(angle)}° > {A(slidePlastic)}°) : le produit du dessus glisserait ; pose directe impossible."
            : $"Appui de {A(support)} % < {A(MinDirectSupport)} % : le produit du dessus serait instable ; pose directe impossible.");

        if (shape == TopShape.Arrondi && angle > MaxRoundAngle)
        {
            steps.Add($"Dessus bombé à plus de {A(MaxRoundAngle)}° : l'intercalaire ne toucherait qu'un point ; non gerbable.");
            return new(TopStackMode.NonGerbable, support, 0, "dessus trop bombé : rien dessus", steps);
        }

        var boardFactor = shape == TopShape.Arrondi ? 0.6 : 0.8;
        steps.Add($"Avec un intercalaire rigide sous chaque couche, la charge se répartit sur les parties hautes du dessus : charge admissible × {boardFactor.ToString("0.0", Fr)}" +
                  $" ({(shape == TopShape.Arrondi ? "dessus bombé" : "dessus incliné")}).");
        return new(TopStackMode.Intercalaire, support, boardFactor, $"intercalaire sous chaque couche, charge × {boardFactor.ToString("0.0", Fr)}", steps);
    }

    public static string HandleLabel(HandleKind h) => h switch
    {
        HandleKind.Encastree => "encastrée (sous le plan du dessus)",
        HandleKind.Saillante => "saillante (dépasse le dessus)",
        HandleKind.Rabattable => "anse rabattable (à plat sur le couvercle)",
        _ => "aucune"
    };

    /// <summary>
    /// Couches maxi imposées par la forme : 1 si non gerbable, ou si l'intercalaire est obligatoire et manque sous une
    /// couche (<paramref name="sheetBelow"/> : intercalaire sous la couche k, k ≥ 1).
    /// </summary>
    public static int? MaxLayers(TopStackRule? rule, Func<int, bool> sheetBelow)
    {
        if (rule == null || rule.Mode == TopStackMode.Direct)
        {
            return null;
        }

        if (rule.Mode == TopStackMode.NonGerbable)
        {
            return 1;
        }

        var layers = 1;
        while (layers < 500 && sheetBelow(layers))
        {
            layers++;
        }

        return layers;
    }
}
