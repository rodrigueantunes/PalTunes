using System.Globalization;
using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

/// <summary>Classe de comportement en gerbage (étude hétérogène §3.1).</summary>
public enum StackClass
{
    Carton,
    Rigide,
    Souple,
    Roulant
}

/// <summary>Zone conseillée dans la palette (étude hétérogène §3.4).</summary>
public enum StackZone
{
    Bas = 0,
    Milieu = 1,
    Haut = 2
}

/// <summary>
/// Profil de gerbage d'un article, entièrement déduit des données existantes (type, dimensions, poids) et affiné par les
/// champs facultatifs (charge maxi, fragile). Aucune donnée obligatoire supplémentaire (étude hétérogène §2–§3).
/// </summary>
public sealed class StackingProfile
{
    /// <summary>Hauteur de charge de référence de l'auto-gerbage : 1 800 mm palette EUR (144 mm) comprise.</summary>
    public const double ReferenceLoadHeight = 1656;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public required StackClass Class { get; init; }
    public required StackZone Zone { get; init; }

    /// <summary>Poids admissible sur le dessus d'un exemplaire (kg) par d'autres articles.</summary>
    public required double Capacity { get; init; }

    /// <summary>
    /// Capacité quand toute la charge reçue arrive par des colonnes parfaitement alignées (même empreinte) : pas de perte
    /// de désalignement (étude §6.2), k = 1. Égale à <see cref="Capacity"/> sauf pour un carton à capacité déduite.
    /// </summary>
    public double AlignedCapacity { get; init; }

    public required bool CapacityEntered { get; init; }

    /// <summary>Explication lisible de la capacité (saisie, fragile ou calcul déduit).</summary>
    public required string CapacityText { get; init; }

    /// <summary>Densité apparente (kg/dm³).</summary>
    public required double Density { get; init; }

    /// <summary>Taux d'appui exigé quand l'article ne repose pas sur la palette (0 = paramètre du conditionnement).</summary>
    public double MinSupportOffFloor { get; init; }

    /// <summary>Seuil d'appui imposé à tout article posé sur celui-ci (sacs : dessus bombé).</summary>
    public double SupportRequiredOnTop { get; init; }

    /// <summary>Seuls des exemplaires du même article peuvent être posés dessus (cylindres couchés).</summary>
    public bool SameArticleOnTopOnly { get; init; }

    /// <summary>Dessus circulaire : la surface de contact vaut π/4 de l'enveloppe (fûts, bobines debout).</summary>
    public bool RoundTop { get; init; }

    /// <summary>Résistance relative : capacité / poids propre.</summary>
    public double RelativeStrength { get; init; }

    public string ClassLabel => Class switch
    {
        StackClass.Rigide => "Rigide",
        StackClass.Souple => "Souple (dessus bombé)",
        StackClass.Roulant => "Roulant (cylindre couché)",
        _ => "Carton"
    };

    public string ZoneLabel => Zone switch
    {
        StackZone.Bas => "Bas de palette",
        StackZone.Haut => "Haut de palette",
        _ => "Milieu"
    };

    /// <summary>
    /// <paramref name="axis"/> : axe retenu pour les cylindres (null = axe de l'article). Une bobine ou un tube
    /// « indifférent » est traité comme debout pour la classe, l'orientation effective étant vérifiée à la pose.
    /// </summary>
    public static StackingProfile For(Article a, CoilAxis? axis = null)
    {
        var effectiveAxis = axis ?? a.CoilAxis;
        var lying = a.Kind is ArticleKind.Tube or ArticleKind.Bobine && effectiveAxis == CoilAxis.Horizontal;
        var cls = a.Kind switch
        {
            ArticleKind.Caisse or ArticleKind.Autre => StackClass.Carton,
            ArticleKind.Sac => StackClass.Souple,
            ArticleKind.Tube when lying || effectiveAxis != CoilAxis.Vertical => StackClass.Roulant,
            ArticleKind.Bobine when lying => StackClass.Roulant,
            _ => StackClass.Rigide
        };

        var height = a.Kind switch
        {
            ArticleKind.Bobine or ArticleKind.Tube or ArticleKind.Fut => cls == StackClass.Roulant ? a.Diameter : a.AxisLength,
            _ => a.Height
        };
        var w = Math.Max(1e-9, a.Weight);
        var volumeDm3 = Math.Max(1e-9, a.Volume / 1e6);
        var density = w / volumeDm3;

        double capacity;
        double aligned;
        string text;
        var entered = false;
        if (a.Fragile)
        {
            capacity = 0;
            aligned = 0;
            text = "0 kg (fragile : rien dessus)";
        }
        else if (a.MaxLoadOnTop is { } top)
        {
            capacity = top;
            aligned = top;
            entered = true;
            text = $"{top.ToString("0.###", Fr)} kg (saisie)";
        }
        else if (cls == StackClass.Roulant)
        {
            capacity = 0;
            aligned = 0;
            text = "0 kg pour les autres articles (cylindre couché : seuls des exemplaires identiques dessus)";
        }
        else
        {
            // Auto-gerbage : l'emballage supporte une palette homogène de lui-même sur la hauteur de référence.
            var n = height > 0 ? (int)Math.Floor(ReferenceLoadHeight / height + 1e-9) : 1;
            var k = cls == StackClass.Carton ? 0.5 : 1.0;
            var computed = k * w * Math.Max(0, n - 1);
            var floor = 0.5 * w;
            capacity = Math.Max(computed, floor);
            aligned = Math.Max(w * Math.Max(0, n - 1), floor);
            var formula = $"{k.ToString("0.#", Fr)} × {w.ToString("0.#####", Fr)} kg × ({n} − 1)";
            text = computed >= floor
                ? $"{capacity.ToString("0.##", Fr)} kg (déduite : {formula}, auto-gerbage sur {ReferenceLoadHeight:0} mm)"
                : $"{capacity.ToString("0.##", Fr)} kg (déduite : plancher 0,5 × poids ; {formula} = {computed.ToString("0.##", Fr)})";
            if (k < 1 && aligned > capacity)
            {
                text += $" ; {aligned.ToString("0.##", Fr)} kg en colonne alignée";
            }
        }

        // Zone conseillée (étude hétérogène §3.4) : plaques et sacs en bas ; fûts, bobines et cylindres couchés en bas
        // seulement s'ils sont lourds (≥ 15 kg) ; un cylindre couché léger ne porte que ses semblables : en haut.
        var heavy = w >= 15;
        var zone = StackZone.Milieu;
        if (a.Fragile || (capacity < w && cls != StackClass.Roulant) || density < 0.08 || (cls == StackClass.Roulant && !heavy))
        {
            zone = StackZone.Haut;
        }

        if (!a.Fragile && (a.Kind is ArticleKind.Plaque or ArticleKind.Sac ||
                           (heavy && (a.Kind is ArticleKind.Fut or ArticleKind.Bobine || cls == StackClass.Roulant)) ||
                           (density >= 0.6 && heavy)))
        {
            zone = StackZone.Bas;
        }

        return new StackingProfile
        {
            Class = cls,
            Zone = zone,
            Capacity = capacity,
            AlignedCapacity = aligned,
            CapacityEntered = entered,
            CapacityText = text,
            Density = density,
            MinSupportOffFloor = a.Kind == ArticleKind.Plaque ? 0.95 : cls == StackClass.Roulant ? 0.90 : 0,
            SupportRequiredOnTop = cls == StackClass.Souple ? 0.90 : 0,
            SameArticleOnTopOnly = cls == StackClass.Roulant,
            RoundTop = !lying && a.Kind is ArticleKind.Fut or ArticleKind.Bobine || (a.Kind == ArticleKind.Tube && effectiveAxis == CoilAxis.Vertical),
            RelativeStrength = capacity / w
        };
    }
}
