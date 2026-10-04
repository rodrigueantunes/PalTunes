using System.Globalization;
using PalTunes.Core.Models;

namespace PalTunes.Core.Export;

/// <summary>Produit d'un plan de couche : numéro dans la couche, position du coin depuis le coin de la palette, empreinte posée.</summary>
public sealed record PlanPosition(int Number, string Article, double X, double Y, double DX, double DY, string Shape);

/// <summary>Plan de couche distinct et les couches qui l'utilisent.</summary>
public sealed record PlanGroup(string Name, IReadOnlyList<int> Layers, int Count, IReadOnlyList<int> SlipSheetLayers, IReadOnlyList<PlanPosition> Positions)
{
    public int RepresentativeLayer => Layers[0];
    public string LayersText => PalletizationPlan.Ranges(Layers);
}

/// <summary>Ligne du tableau des couches.</summary>
public sealed record PlanLayerRow(int Layer, string Plan, int Count, double Z, double Height, bool SlipSheetBelow);

/// <summary>
/// Plan de palettisation (fiche imprimée, export) : informations minimales pour la mise sur palette — nombre de produits
/// par couche, intercalaires, et pour chaque plan de couche distinct la position de chaque produit (numérotée).
/// </summary>
public static class PalletizationPlan
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Ordre de lecture d'une couche : rangées le long de la largeur (Y), puis le long de la longueur (X).</summary>
    public static List<Placement> ReadingOrder(IEnumerable<Placement> items) =>
        items.OrderBy(p => Math.Round(p.Y / 5)).ThenBy(p => p.X).ToList();

    public static List<PlanGroup> Groups(LoadUnit unit, Func<Guid, string> code)
    {
        var groups = new List<(string Signature, List<int> Layers, List<int> Sheets, List<PlanPosition> Positions)>();
        foreach (var layer in unit.Layers.OrderBy(l => l.Index))
        {
            var items = ReadingOrder(unit.Items.Where(p => p.Layer == layer.Index));
            if (items.Count == 0)
            {
                continue;
            }

            var positions = items.Select((p, i) => new PlanPosition(i + 1, code(p.ArticleId), Math.Round(p.X), Math.Round(p.Y),
                Math.Round(p.DX), Math.Round(p.DY), ShapeText(p.Shape))).ToList();
            var signature = string.Join(";", positions.Select(q => $"{q.Article}|{q.X:0}|{q.Y:0}|{q.DX:0}|{q.DY:0}|{q.Shape}"));
            var existing = groups.FindIndex(g => g.Signature == signature);
            if (existing < 0)
            {
                groups.Add((signature, [layer.Index], layer.SlipSheetBelow ? [layer.Index] : [], positions));
            }
            else
            {
                groups[existing].Layers.Add(layer.Index);
                if (layer.SlipSheetBelow)
                {
                    groups[existing].Sheets.Add(layer.Index);
                }
            }
        }

        return groups.Select((g, i) => new PlanGroup(PlanName(i), g.Layers, g.Positions.Count, g.Sheets, g.Positions)).ToList();
    }

    public static List<PlanLayerRow> LayerRows(LoadUnit unit, IReadOnlyList<PlanGroup> groups) =>
        unit.Layers.OrderBy(l => l.Index).Select(l => new PlanLayerRow(
            l.Index,
            groups.FirstOrDefault(g => g.Layers.Contains(l.Index))?.Name ?? "",
            unit.Items.Count(p => p.Layer == l.Index),
            Math.Round(l.Z),
            Math.Round(l.Height),
            l.SlipSheetBelow)).ToList();

    /// <summary>Résumé pour l'export : « C1-C6 : plan A, 8 produits ; … ».</summary>
    public static string Summary(LoadUnit unit, Func<Guid, string> code) =>
        string.Join(" ; ", Groups(unit, code).Select(g => $"C{g.LayersText} : plan {g.Name}, {g.Count} produit(s)"));

    public static string SlipSheetSummary(LoadUnit unit)
    {
        var layers = unit.Layers.Where(l => l.SlipSheetBelow).Select(l => l.Index).ToList();
        return layers.Count == 0 ? "aucun" : "sous C" + Ranges(layers);
    }

    private static string PlanName(int index) => index < 26 ? ((char)('A' + index)).ToString() : $"P{index + 1}";

    private static string ShapeText(ShapeKind shape) => shape switch
    {
        ShapeKind.CylinderZ => "cylindre debout",
        ShapeKind.CylinderX => "couché, axe en longueur",
        ShapeKind.CylinderY => "couché, axe en largeur",
        _ => "pavé"
    };

    /// <summary>« 1-3, 5, 7-9 ».</summary>
    public static string Ranges(IReadOnlyList<int> values)
    {
        var sorted = values.Distinct().OrderBy(v => v).ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var start = sorted[i];
            while (i + 1 < sorted.Count && sorted[i + 1] == sorted[i] + 1)
            {
                i++;
            }

            parts.Add(start == sorted[i] ? start.ToString(Fr) : $"{start}-{sorted[i]}");
        }

        return string.Join(", ", parts);
    }
}
