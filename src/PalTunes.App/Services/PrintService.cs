using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PalTunes.App.Views.Controls;
using PalTunes.Core.Export;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.App.Services;

/// <summary>
/// Fiche de palettisation (étude §12) : spécification complète, plans de couche A / B, vues de côté et de face.
/// « Microsoft Print to PDF » produit un PDF.
/// </summary>
public static class PrintService
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static void PrintSheet(Packaging p, Solution s, LoadUnit unit, Database db, IReadOnlyDictionary<Guid, Color> colors)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var doc = BuildDocument(p, s, unit, db, colors, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"PalTunes – {p.Code}");
    }

    public static FlowDocument BuildDocument(Packaging p, Solution s, LoadUnit unit, Database db, IReadOnlyDictionary<Guid, Color> colors, double pageWidth, double pageHeight)
    {
        var doc = new FlowDocument
        {
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            PagePadding = new Thickness(40),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11
        };
        var dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        var muted = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));

        // En-tête
        var header = new Table { CellSpacing = 0 };
        header.Columns.Add(new TableColumn { Width = new GridLength(60) });
        header.Columns.Add(new TableColumn());
        var hg = new TableRowGroup();
        var hr = new TableRow();
        var logo = new Image { Source = Application.Current?.TryFindResource("LogoImage") as ImageSource, Width = 48, Height = 48 };
        hr.Cells.Add(new TableCell(new BlockUIContainer(logo)));
        var title = new Paragraph { Margin = new Thickness(0) };
        title.Inlines.Add(new Run("Fiche de palettisation") { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = dark });
        title.Inlines.Add(new LineBreak());
        title.Inlines.Add(new Run($"{p.Code}{(string.IsNullOrWhiteSpace(p.Name) ? "" : " – " + p.Name)} · {p.KindLabel} · {s.Title}") { FontSize = 12, Foreground = dark });
        title.Inlines.Add(new LineBreak());
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        title.Inlines.Add(new Run($"Édité le {DateTime.Now:dd/MM/yyyy HH:mm} · PalTunes {version?.ToString(3)}") { Foreground = muted });
        hr.Cells.Add(new TableCell(title));
        hg.Rows.Add(hr);
        header.RowGroups.Add(hg);
        doc.Blocks.Add(header);

        // Contenu
        var content = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
        if (p.Kind == PackagingKind.Homogene && db.FindArticle(p.ArticleId) is { } a)
        {
            content.Inlines.Add(new Run("Article : ") { FontWeight = FontWeights.SemiBold });
            content.Inlines.Add(new Run($"{a.DisplayName} · {a.KindLabel} · {a.DimensionsText} · {a.Weight.ToString(Formats.UnitWeight, Fr)} kg" +
                                        (string.IsNullOrWhiteSpace(a.Client) ? "" : $" · client {a.Client}")));
        }
        else
        {
            content.Inlines.Add(new Run("Contenu : ") { FontWeight = FontWeights.SemiBold });
            content.Inlines.Add(new Run(string.Join(" · ", p.Lines.Select(l => $"{l.Quantity} × {db.FindArticle(l.ArticleId)?.Code}"))));
            if (s.Units.Count > 1)
            {
                content.Inlines.Add(new Run($"  (unité {unit.Index} / {s.Units.Count})") { Foreground = muted });
            }
        }

        doc.Blocks.Add(content);

        // Spécification : deux colonnes de groupes
        var view = ReferenceEquals(unit, s.FirstUnit)
            ? s
            : new Solution
            {
                Kind = s.Kind, Base = s.Base, Units = [unit], ItemsPerUnit = unit.Items.Count, LayerCount = unit.Layers.Count,
                StackLevels = s.StackLevels, StackLimitReason = s.StackLimitReason, Pattern = s.Pattern
            };
        var rows = PackagingSpec.Rows(view, p.Constraints);
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 6) };
        table.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1.2, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var group = new TableRowGroup();
        foreach (var g in rows.GroupBy(r => r.Group))
        {
            var gr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)) };
            gr.Cells.Add(new TableCell(new Paragraph(new Run(g.Key.ToUpperInvariant())) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 3, 6, 3) }) { ColumnSpan = 3 });
            group.Rows.Add(gr);
            var odd = false;
            foreach (var r in g)
            {
                var row = new TableRow { Background = odd ? new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF7)) : Brushes.White };
                row.Cells.Add(Cell(r.Label, dark));
                row.Cells.Add(Cell(r.Value, dark, bold: true, right: true));
                row.Cells.Add(Cell(r.Unit, muted));
                group.Rows.Add(row);
                odd = !odd;
            }
        }

        table.RowGroups.Add(group);
        doc.Blocks.Add(table);

        if (s.Recommendation != null)
        {
            doc.Blocks.Add(new Paragraph(new Run(s.Recommendation)) { Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x84, 0x49)), Margin = new Thickness(0, 4, 0, 4) });
        }

        foreach (var w in s.Warnings.Concat(s.Violations))
        {
            doc.Blocks.Add(new Paragraph(new Run("• " + w)) { Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x77, 0x0E)), Margin = new Thickness(0, 1, 0, 1) });
        }

        // Plans : couche 1 (A), couche 2 (B si différente), côté, face.
        var images = new List<(string, BitmapSource)>();
        var width = 340;
        images.Add(("Couche 1", Render(s, unit, p.Constraints, colors, PlanViewMode.Top, 1, width, 240)));
        if (unit.Layers.Count > 1)
        {
            images.Add(("Couche 2", Render(s, unit, p.Constraints, colors, PlanViewMode.Top, 2, width, 240)));
        }

        images.Add(("Côté", Render(s, unit, p.Constraints, colors, PlanViewMode.Side, 1, width, 260)));
        images.Add(("Face", Render(s, unit, p.Constraints, colors, PlanViewMode.Front, 1, width, 260)));
        var grid = new Table { CellSpacing = 6 };
        grid.Columns.Add(new TableColumn());
        grid.Columns.Add(new TableColumn());
        var ig = new TableRowGroup();
        for (var i = 0; i < images.Count; i += 2)
        {
            var row = new TableRow();
            for (var k = i; k < Math.Min(images.Count, i + 2); k++)
            {
                row.Cells.Add(new TableCell(new BlockUIContainer(new Image { Source = images[k].Item2, Width = width })));
            }

            ig.Rows.Add(row);
        }

        grid.RowGroups.Add(ig);
        doc.Blocks.Add(grid);
        return doc;
    }

    private static TableCell Cell(string text, Brush brush, bool bold = false, bool right = false) =>
        new(new Paragraph(new Run(text))
        {
            Margin = new Thickness(6, 2, 6, 2),
            Foreground = brush,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left
        });

    public static BitmapSource Render(Solution s, LoadUnit unit, PackagingConstraints c, IReadOnlyDictionary<Guid, Color> colors,
        PlanViewMode mode, int layer, int width, int height)
    {
        const double scale = 2;
        var view = new PlanView2D { Solution = s, Unit = unit, Constraints = c, ColorMap = colors, Mode = mode, Layer = layer, Width = width, Height = height };
        var host = new Border { Background = Brushes.White, Child = view, Width = width, Height = height };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(host);
        bmp.Freeze();
        return bmp;
    }
}
