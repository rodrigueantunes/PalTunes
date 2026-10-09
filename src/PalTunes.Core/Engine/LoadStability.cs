using System.Globalization;
using PalTunes.Core.Models;

namespace PalTunes.Core.Engine;

/// <summary>
/// Tenue physique d'une palette homogène, distincte de la faisabilité géométrique (dimensions, hauteur, poids) :
/// produits debout élancés, tubes couchés en lits, produits à cheval sur des palettes jumelées.
/// Accélérations de calcul du transport routier (EN 12195-1) : 0,8 g vers l'avant, 0,5 g sur les côtés et vers
/// l'arrière. Un produit rigide posé seul, centre de gravité à mi-hauteur, bascule quand l'accélération dépasse
/// (b/2)/(h/2) = b/h (b : petit côté ou diamètre au sol, h : hauteur), soit dès un angle d'inclinaison arctan(b/h).
/// Essai de référence de la tenue d'une unité de charge : EUMOS 40509 (0,5 g sans déformation excessive).
/// </summary>
public static class LoadStability
{
    /// <summary>Accélération latérale de calcul (g) du transport routier, EN 12195-1.</summary>
    public const double LateralG = 0.5;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public sealed record Verdict(List<string> Lines, List<string> Warnings);

    private static string Mm(double v) => v.ToString("#,0.#", Fr);

    /// <summary>Lignes de l'explication et avertissements (palette homogène ; caisse : rien, les parois tiennent les produits).</summary>
    public static Verdict Assess(Solution s, LoadUnit unit, PackagingConstraints c, Article article)
    {
        var lines = new List<string>();
        var warnings = new List<string>();
        if (unit.Items.Count == 0 || s.Base.IsCase)
        {
            return new(lines, warnings);
        }

        var held = new List<string>();
        if (c.FilmThickness > 0)
        {
            held.Add("film étirable");
        }

        if (c.Straps > 0)
        {
            held.Add($"{c.Straps} cerclage(s)");
        }

        if (c.Corners)
        {
            held.Add("cornières");
        }

        var heldText = held.Count == 0 ? "aucun maintien saisi" : string.Join(", ", held);
        var m = unit.Metrics;
        var p0 = unit.Items[0];
        var issues = 0;

        if (p0.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY)
        {
            var layers = unit.Layers.Count;
            var d = p0.DZ;
            if (layers <= 1)
            {
                lines.Add($"Un seul lit de produits couchés : caler le premier rang (cales ou cornières) contre le roulement. Maintien : {heldText}.");
            }
            else
            {
                var counts = unit.Layers.Select(l => l.Count).ToList();
                lines.Add(s.Pattern == StackPattern.Quinconce
                    ? $"Lits en quinconce : chaque tube intérieur repose dans le creux de deux tubes du lit inférieur, mais les tubes de rive " +
                      $"(lit de {counts[1]} décalé d'un demi-diamètre, {Mm(d / 2)} mm, sur un lit de {counts[0]}) ne reposent que sur un tube : sans appui latéral, ils roulent vers l'extérieur."
                    : "Lits superposés : chaque tube repose sur un seul tube, en équilibre instable : sans appui latéral, les lits glissent dans les creux du lit inférieur.");
                if (c.Corners)
                {
                    lines.Add($"Appui latéral : les tubes de rive s'appuient contre les cornières des quatre angles ; maintien : {heldText}.");
                    if (c.Straps == 0 && c.FilmThickness <= 0)
                    {
                        issues++;
                        warnings.Add("Tubes couchés en plusieurs lits : cerclage ou film nécessaire pour serrer les cornières contre les lits.");
                    }
                }
                else
                {
                    issues++;
                    lines.Add("Aucune cornière : maintien latéral obligatoire (cornières, montants ou cadre), sinon les tubes de rive roulent.");
                    warnings.Add("Tubes couchés en plusieurs lits sans cornières : maintien latéral obligatoire (cornières, montants ou cadre), sinon les tubes de rive roulent.");
                }
            }
        }
        else
        {
            var h = p0.DZ;
            var b = Math.Min(p0.DX, p0.DY);
            if (h > 0 && b > 0)
            {
                var ratio = b / h;
                var angle = Math.Atan(ratio) * 180 / Math.PI;
                var what = p0.Shape == ShapeKind.CylinderZ ? "de diamètre" : "de petit côté au sol";
                if (ratio < LateralG)
                {
                    lines.Add($"Produit debout élancé : {Mm(h)} mm de haut pour {Mm(b)} mm {what} (hauteur / base = {(h / b).ToString("0.#", Fr)}). " +
                              $"Seul, il bascule dès {angle.ToString("0.#", Fr)}° d'inclinaison ou {ratio.ToString("0.##", Fr)} g d'accélération (tan θ = base / hauteur), " +
                              $"bien moins que les {LateralG.ToString("0.#", Fr)} g latéraux du transport routier (EN 12195-1).");
                    var block = Math.Min(m.LoadLength, m.LoadWidth);
                    lines.Add($"La charge ne tient que si les produits travaillent en bloc ({Mm(m.LoadLength)} × {Mm(m.LoadWidth)} × {Mm(m.LoadHeight)} mm, " +
                              $"base / hauteur = {(block / Math.Max(1, m.LoadHeight)).ToString("0.##", Fr)}) : produits serrés, film étirable et cerclage en haut et en bas.");
                    if (c.FilmThickness > 0 || c.Straps > 0)
                    {
                        lines.Add($"Maintien prévu : {heldText}. À valider par un essai de tenue (EUMOS 40509, 0,5 g) ; pendant la construction, poser par rangées serrées.");
                    }
                    else
                    {
                        issues++;
                        lines.Add("Aucun film ni cerclage saisi : la charge n'est pas transportable en l'état.");
                        warnings.Add($"Produits debout élancés (hauteur / base {(h / b).ToString("0.#", Fr)}) sans film ni cerclage : prévoir film étirable et cerclage (Accessoires).");
                    }
                }
                else
                {
                    lines.Add($"Produit debout : base / hauteur = {Mm(b)} / {Mm(h)} = {ratio.ToString("0.##", Fr)} ≥ {LateralG.ToString("0.#", Fr)} : " +
                              $"un produit seul ne bascule pas sous les {LateralG.ToString("0.#", Fr)} g latéraux du transport routier (EN 12195-1). Maintien : {heldText}.");
                }
            }
        }

        if (s.Base.PhysicalCount > 1)
        {
            var b = s.Base;
            var cuts = Enumerable.Range(1, b.CountAlongLength - 1).Select(i => (X: true, At: i * b.PalletLength))
                .Concat(Enumerable.Range(1, b.CountAlongWidth - 1).Select(j => (X: false, At: j * b.PalletWidth))).ToList();
            var across = unit.Items.Count(p => cuts.Any(k => k.X ? p.X < k.At - 1 && p.MaxX > k.At + 1 : p.Y < k.At - 1 && p.MaxY > k.At + 1));
            if (across > 0)
            {
                issues++;
                lines.Add($"{across} produit(s) à cheval sur les {b.PhysicalCount} palettes : elles doivent former une seule unité — palettes solidarisées " +
                          "(planches de liaison, cerclage commun) et manutentionnées ensemble (fourches longues), sinon elles s'écartent et la charge tombe entre elles.");
                warnings.Add($"Base de {b.PhysicalCount} palettes : produits à cheval, solidariser les palettes et les manutentionner ensemble.");
            }
            else
            {
                lines.Add($"Base de {b.PhysicalCount} palettes : chaque produit repose sur une seule palette ; les manutentionner ensemble reste conseillé.");
            }
        }

        lines.Add(issues > 0
            ? "Bilan : solution géométriquement valide (dimensions, hauteur, poids) ; sa tenue en transport dépend des points ci-dessus."
            : held.Count > 0
                ? "Bilan : géométrie contrôlée (dimensions, hauteur, poids, appuis) ; tenue assurée par le maintien prévu."
                : "Bilan : géométrie contrôlée (dimensions, hauteur, poids, appuis) ; pas de point de tenue particulier, un film étirable reste conseillé pour le transport.");
        return new(lines, warnings);
    }
}
