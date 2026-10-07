using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PalTunes.App.Views.Controls;
using PalTunes.Core.Engine;
using PalTunes.Core.Export;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.App.Services;

/// <summary>
/// Fiches imprimées. Fiche palette (étude §12) : spécification complète, plans de couche A / B, vues de côté et de face,
/// plan de palettisation. Fiche de colisage : produit, caisse, quantité par caisse, plans et vue 3D de la caisse.
/// Fiche de conditionnement : les deux à la suite (caisse puis palette). « Microsoft Print to PDF » produit un PDF.
/// </summary>
public static class PrintService
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Fiche palette : palettisation du conditionnement affiché.</summary>
    public static void PrintSheet(Packaging p, Solution s, LoadUnit unit, Database db, IReadOnlyDictionary<Guid, Color> colors, Article? article = null) =>
        Print($"PalTunes – fiche palette {p.Code}", (w, h) => [BuildDocument(p, s, unit, db, colors, w, h, article)]);

    /// <summary>Fiche de colisage : produit dans sa caisse.</summary>
    public static void PrintCaseSheet(CaseEngine.CaseSheet sheet, string? caseArticleCode, Database db, IReadOnlyDictionary<Guid, Color> colors) =>
        Print($"PalTunes – fiche de colisage {caseArticleCode ?? sheet.Content.Code}", (w, h) => [BuildCaseDocument(sheet, caseArticleCode, db, colors, w, h)]);

    /// <summary>Fiche de conditionnement : fiche palette de la caisse, puis fiche de colisage sur une nouvelle page.</summary>
    public static void PrintPackagingSheet(CaseEngine.CaseSheet sheet, Article box, Packaging p, Solution s, LoadUnit unit, Database db,
        IReadOnlyDictionary<Guid, Color> caseColors, IReadOnlyDictionary<Guid, Color> palletColors) =>
        Print($"PalTunes – fiche de conditionnement {box.Code}", (w, h) => [BuildPackagingDocument(sheet, box, p, s, unit, db, caseColors, palletColors, w, h)]);

    /// <summary>Fiche de conditionnement en un document : fiche palette complète, puis fiche de colisage qui repart en haut d'une page.</summary>
    public static FlowDocument BuildPackagingDocument(CaseEngine.CaseSheet sheet, Article box, Packaging p, Solution s, LoadUnit unit, Database db,
        IReadOnlyDictionary<Guid, Color> caseColors, IReadOnlyDictionary<Guid, Color> palletColors, double pageWidth, double pageHeight) =>
        Combine(BuildDocument(p, s, unit, db, palletColors, pageWidth, pageHeight, box), BuildCaseDocument(sheet, box.Code, db, caseColors, pageWidth, pageHeight));

    /// <summary>Fiche de conditionnement d'un article caisse dont seul le nombre de produits par caisse est connu.</summary>
    public static void PrintPackagingSummarySheet(Article box, Packaging p, Solution s, LoadUnit unit, Database db, IReadOnlyDictionary<Guid, Color> palletColors) =>
        Print($"PalTunes – fiche de conditionnement {box.Code}", (w, h) =>
        [
            BuildDocument(p, s, unit, db, palletColors, w, h, box),
            BuildCaseSummaryDocument(box, p, db, w, h)
        ]);

    /// <summary>Fiche de colisage d'un article caisse dont seul le nombre de produits par caisse est connu.</summary>
    public static void PrintCaseSummarySheet(Article box, Packaging? palletPackaging, Database db) =>
        Print($"PalTunes – fiche de colisage {box.Code}", (w, h) => [BuildCaseSummaryDocument(box, palletPackaging, db, w, h)]);

    /// <summary>Fiche de colisage d'une caisse : complète (colisage recalculé) ou résumée (quantité par caisse seule).</summary>
    public sealed record ColisageDoc(Article Box, CaseEngine.CaseSheet? Sheet, IReadOnlyDictionary<Guid, Color> Colors);

    private static FlowDocument CaseDocument(ColisageDoc c, Packaging? palletPackaging, Database db, double w, double h) =>
        c.Sheet != null ? BuildCaseDocument(c.Sheet, c.Box.Code, db, c.Colors, w, h) : BuildCaseSummaryDocument(c.Box, palletPackaging, db, w, h);

    /// <summary>Fiches de colisage de toutes les caisses d'une palette, chacune sur une nouvelle page, en un seul document.</summary>
    public static void PrintCaseSheets(string title, IReadOnlyList<ColisageDoc> colisages, Database db) =>
        Print($"PalTunes – {(colisages.Count > 1 ? "fiches" : "fiche")} de colisage {title}", (w, h) => [.. colisages.Select(c => CaseDocument(c, null, db, w, h))]);

    /// <summary>
    /// Fiche de conditionnement d'une palette à plusieurs caisses : fiche palette, puis toutes les fiches de colisage,
    /// chacune repartant en haut d'une page.
    /// </summary>
    public static void PrintPackagingSheets(Packaging p, Solution s, LoadUnit unit, Database db, IReadOnlyDictionary<Guid, Color> palletColors,
        IReadOnlyList<ColisageDoc> colisages, Article? article = null) =>
        Print($"PalTunes – fiche de conditionnement {p.Code}", (w, h) =>
        [
            BuildDocument(p, s, unit, db, palletColors, w, h, article),
            .. colisages.Select(c => CaseDocument(c, p.Kind == PackagingKind.Homogene ? p : null, db, w, h))
        ]);

    /// <summary>
    /// Documents mis bout à bout : chaque document suivant est placé dans une section qui commence sur une nouvelle page
    /// (rien de la fiche précédente ne la partage).
    /// </summary>
    public static FlowDocument Combine(params FlowDocument[] docs)
    {
        var doc = docs[0];
        foreach (var next in docs.Skip(1))
        {
            var blocks = next.Blocks.ToList();
            next.Blocks.Clear();
            var section = new Section { BreakPageBefore = true };
            foreach (var block in blocks)
            {
                section.Blocks.Add(block);
            }

            doc.Blocks.Add(section);
        }

        return doc;
    }

    /// <summary>Choix de l'imprimante, puis documents mis bout à bout (chacun commence sur une nouvelle page).</summary>
    private static void Print(string jobName, Func<double, double, IReadOnlyList<FlowDocument>> build)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var doc = Combine([.. build(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight)]);
        dialog.PrintDocument(Paginate(doc, jobName.Replace("PalTunes – ", "")), jobName);
    }

    /// <summary>Pages du document avec un pied de page : filet, titre de la fiche à gauche, « page x / n » à droite.</summary>
    public static DocumentPaginator Paginate(FlowDocument doc, string title)
    {
        var inner = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        inner.ComputePageCount();
        return new FooterPaginator(inner, title, doc.PagePadding);
    }

    private sealed class FooterPaginator(DocumentPaginator inner, string title, Thickness padding) : DocumentPaginator
    {
        public override DocumentPage GetPage(int pageNumber)
        {
            var page = inner.GetPage(pageNumber);
            var visual = new ContainerVisual();
            visual.Children.Add(page.Visual);
            var footer = new DrawingVisual();
            using (var dc = footer.RenderOpen())
            {
                var size = page.Size;
                var muted = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
                var y = size.Height - padding.Bottom * 0.62;
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0xD5, 0xDB, 0xDB)), 0.75), new Point(padding.Left, y - 4), new Point(size.Width - padding.Right, y - 4));
                var face = new Typeface("Segoe UI");
                var left = new FormattedText($"PalTunes · {title}", Fr, FlowDirection.LeftToRight, face, 9, muted, 1.0);
                var right = new FormattedText($"page {pageNumber + 1} / {inner.PageCount}", Fr, FlowDirection.LeftToRight, face, 9, muted, 1.0);
                dc.DrawText(left, new Point(padding.Left, y));
                dc.DrawText(right, new Point(size.Width - padding.Right - right.Width, y));
            }

            visual.Children.Add(footer);
            return new DocumentPage(visual, page.Size, page.BleedBox, page.ContentBox);
        }

        public override bool IsPageCountValid => inner.IsPageCountValid;
        public override int PageCount => inner.PageCount;

        public override Size PageSize
        {
            get => inner.PageSize;
            set => inner.PageSize = value;
        }

        public override IDocumentPaginatorSource Source => inner.Source;
    }

    /// <summary>
    /// Densité de la première page : 0 = mise en page normale ; chaque niveau réduit les images du cartouche, puis le
    /// texte et les marges, puis passe la spécification sur deux colonnes, jusqu'à ce que tout tienne sur une page.
    /// </summary>
    private sealed record Density(int Level)
    {
        public const int Max = 7;

        private double TextScale => Level switch { 0 or 1 => 1, 2 => 0.93, 3 => 0.88, 4 => 0.88, 5 => 0.82, 6 => 0.76, _ => 0.7 };

        /// <summary>Images du cartouche (palette, caisse).</summary>
        public double ImageScale => Level switch { 0 => 1, 1 => 0.7, 2 => 0.6, 3 => 0.5, _ => 0.42 };

        /// <summary>Spécification sur deux colonnes côte à côte.</summary>
        public bool TwoColumns => Level >= 4;

        public bool Tight => Level >= 2;

        public double Font(double size) => Math.Round(size * TextScale, 1);

        public double Space(double v) => Level == 0 ? v : Math.Round(v * (Level == 1 ? 0.7 : 0.45), 1);
    }

    [ThreadStatic] private static Density? _densityField;

    private static Density _density => _densityField ??= new Density(0);

    /// <summary>
    /// Première page (en-tête, cartouche, spécification, messages) : construite en mise en page normale, puis de plus en
    /// plus compacte tant qu'elle déborde sur une deuxième page. Rien n'est retiré : seules les images du cartouche,
    /// les tailles de texte et les marges diminuent, et la spécification passe sur deux colonnes si besoin.
    /// </summary>
    private static void AddFirstPage(FlowDocument doc, Func<List<Block>> build)
    {
        List<Block> blocks = [];
        try
        {
            for (var level = 0; level <= Density.Max; level++)
            {
                _densityField = new Density(level);
                blocks = build();
                if (level == Density.Max || FitsOnePage(doc.PageWidth, doc.PageHeight, blocks))
                {
                    break;
                }
            }
        }
        finally
        {
            _densityField = null;
        }

        foreach (var block in blocks)
        {
            doc.Blocks.Add(block);
        }
    }

    private static bool FitsOnePage(double width, double height, List<Block> blocks)
    {
        var probe = NewDocument(width, height);
        foreach (var block in blocks)
        {
            probe.Blocks.Add(block);
        }

        var paginator = ((IDocumentPaginatorSource)probe).DocumentPaginator;
        paginator.ComputePageCount();
        var fits = paginator.PageCount <= 1;
        probe.Blocks.Clear();
        return fits;
    }

    /// <summary>Spécification : un tableau, ou deux côte à côte (groupes répartis à parts égales) en densité forte.</summary>
    private static Block SpecBlock(IReadOnlyList<SpecRow> rows, Brush dark, Brush muted)
    {
        if (!_density.TwoColumns || rows.Count < 8)
        {
            return SpecTable(rows, dark, muted);
        }

        var groups = rows.GroupBy(r => r.Group).Select(g => g.ToList()).ToList();
        var half = (rows.Count + groups.Count) / 2.0;
        var left = new List<SpecRow>();
        var right = new List<SpecRow>();
        var size = 0;
        foreach (var g in groups)
        {
            var target = size + (g.Count + 1) / 2.0 <= half || left.Count == 0 ? left : right;
            target.AddRange(g);
            if (ReferenceEquals(target, left))
            {
                size += g.Count + 1;
            }
        }

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, _density.Space(6), 0, _density.Space(6)) };
        table.Columns.Add(new TableColumn());
        table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        var row = new TableRow();
        row.Cells.Add(new TableCell(SpecTable(left, dark, muted, flush: true)) { Padding = new Thickness(0, 0, 5, 0) });
        row.Cells.Add(new TableCell(right.Count > 0 ? SpecTable(right, dark, muted, flush: true) : new Paragraph()) { Padding = new Thickness(5, 0, 0, 0) });
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>Recommandation (vert) et avertissements (orange) de la solution.</summary>
    private static List<Block> Messages(Solution s)
    {
        var blocks = new List<Block>();
        var size = _density.Font(11);
        if (s.Recommendation != null)
        {
            blocks.Add(new Paragraph(new Run(s.Recommendation)) { Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x84, 0x49)), FontSize = size, Margin = new Thickness(0, _density.Space(4), 0, _density.Space(4)) });
        }

        foreach (var w in s.Warnings.Concat(s.Violations))
        {
            blocks.Add(new Paragraph(new Run("• " + w)) { Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x77, 0x0E)), FontSize = size, Margin = new Thickness(0, 1, 0, 1) });
        }

        return blocks;
    }

    private static FlowDocument NewDocument(double pageWidth, double pageHeight) => new()
    {
        PageWidth = pageWidth,
        PageHeight = pageHeight,
        PagePadding = new Thickness(40),
        ColumnWidth = double.PositiveInfinity,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 11
    };

    /// <summary>En-tête commun : logo, titre de la fiche, sous-titre, date d'édition et version.</summary>
    private static Table Header(string titleText, string subtitle, Brush dark, Brush muted)
    {
        var header = new Table { CellSpacing = 0 };
        header.Columns.Add(new TableColumn { Width = new GridLength(60) });
        header.Columns.Add(new TableColumn());
        var hg = new TableRowGroup();
        var hr = new TableRow();
        var d = _density;
        var logoSize = d.Level >= 4 ? 36 : 48;
        header.Columns[0].Width = new GridLength(logoSize + 12);
        var logo = new Image { Source = Application.Current?.TryFindResource("LogoImage") as ImageSource, Width = logoSize, Height = logoSize };
        hr.Cells.Add(new TableCell(new BlockUIContainer(logo)));
        var title = new Paragraph { Margin = new Thickness(0) };
        title.Inlines.Add(new Run(titleText) { FontSize = d.Font(20), FontWeight = FontWeights.Bold, Foreground = dark });
        title.Inlines.Add(new LineBreak());
        title.Inlines.Add(new Run(subtitle) { FontSize = d.Font(12), Foreground = dark });
        title.Inlines.Add(new LineBreak());
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        title.Inlines.Add(new Run($"Édité le {DateTime.Now:dd/MM/yyyy HH:mm} · PalTunes {version?.ToString(3)}") { Foreground = muted, FontSize = d.Font(11) });
        hr.Cells.Add(new TableCell(title));
        hg.Rows.Add(hr);
        header.RowGroups.Add(hg);
        header.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x9B, 0xDC));
        header.BorderThickness = new Thickness(0, 0, 0, 2);
        header.Padding = new Thickness(0, 0, 0, d.Space(6));
        header.Margin = new Thickness(0, 0, 0, d.Space(10));
        return header;
    }

    /// <summary>
    /// Cartouche de première page : informations à gauche, à droite les images de la palette choisie et de la caisse
    /// (caisse du catalogue seulement), chacune avec sa légende.
    /// </summary>
    private static Table Cartouche(IEnumerable<(string Label, string Value)> infos, IEnumerable<(BitmapSource Image, string Caption)> pictures, Brush dark, Brush muted)
    {
        var d = _density;
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, d.Space(10)) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1.25, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var group = new TableRowGroup();
        var row = new TableRow();
        var left = new Section();
        foreach (var (label, value) in infos.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
        {
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, d.Space(5)), TextAlignment = TextAlignment.Left };
            para.Inlines.Add(new Run(label.ToUpperInvariant()) { FontSize = Math.Max(7, d.Font(8.5)), FontWeight = FontWeights.SemiBold, Foreground = muted });
            para.Inlines.Add(new LineBreak());
            para.Inlines.Add(new Run(value) { FontSize = d.Font(11.5), Foreground = dark });
            left.Blocks.Add(para);
        }

        row.Cells.Add(new TableCell(left) { Padding = new Thickness(0, 0, 12, 0) });
        var right = new Section();
        var list = pictures.ToList();
        if (list.Count > 0)
        {
            var pics = new Table { CellSpacing = 4 };
            foreach (var _ in list)
            {
                pics.Columns.Add(new TableColumn());
            }

            var pg = new TableRowGroup();
            var imagesRow = new TableRow();
            var captionsRow = new TableRow();
            foreach (var (image, caption) in list)
            {
                imagesRow.Cells.Add(new TableCell(new BlockUIContainer(new Image { Source = image, Width = (list.Count == 1 ? 260 : 150) * d.ImageScale })));
                captionsRow.Cells.Add(new TableCell(new Paragraph(new Run(caption)) { FontSize = Math.Max(7, d.Font(9)), Foreground = muted, TextAlignment = TextAlignment.Center, Margin = new Thickness(0) }));
            }

            pg.Rows.Add(imagesRow);
            pg.Rows.Add(captionsRow);
            pics.RowGroups.Add(pg);
            right.Blocks.Add(pics);
        }

        row.Cells.Add(new TableCell(right));
        group.Rows.Add(row);
        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>Image d'une palette du catalogue (vue 3/4).</summary>
    private static (BitmapSource, string) PalletPicture(PalletType pallet) =>
        (RenderModel(Scene3DBuilder.BuildPallet(pallet).Root, 300, 200), $"{pallet.Code} – {pallet.Name}");

    /// <summary>Image d'une caisse du catalogue, fermée (vue 3/4).</summary>
    private static (BitmapSource, string) CasePicture(CaseType type) =>
        (RenderModel(Scene3DBuilder.BuildCasePreview(type), 300, 200), $"{type.Code} – {type.Name}");

    /// <summary>
    /// Page « Schémas » (nouvelle page) : titre, grille d'images deux par deux (légendes au-dessus), vue 3D en pleine
    /// largeur ; dimensionnée pour tenir sur une page A4.
    /// </summary>
    private static Section SchemasPage(string subtitle, IReadOnlyList<(string Caption, BitmapSource Image)> plans, BitmapSource? view3D, Brush dark, Brush muted)
    {
        var section = new Section { BreakPageBefore = true };
        section.Blocks.Add(new Paragraph(new Run("Schémas")) { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = dark, Margin = new Thickness(0, 0, 0, 0) });
        section.Blocks.Add(new Paragraph(new Run(subtitle)) { Foreground = muted, Margin = new Thickness(0, 0, 0, 8) });
        var grid = new Table { CellSpacing = 8 };
        grid.Columns.Add(new TableColumn());
        grid.Columns.Add(new TableColumn());
        var ig = new TableRowGroup();
        for (var i = 0; i < plans.Count; i += 2)
        {
            var row = new TableRow();
            for (var k = i; k < Math.Min(plans.Count, i + 2); k++)
            {
                row.Cells.Add(new TableCell(new BlockUIContainer(new Image { Source = plans[k].Image, Width = 330 })) { BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xED)), BorderThickness = new Thickness(1) });
            }

            ig.Rows.Add(row);
        }

        grid.RowGroups.Add(ig);
        section.Blocks.Add(grid);
        if (view3D != null)
        {
            section.Blocks.Add(new BlockUIContainer(new Image { Source = view3D, Width = 680 }) { Margin = new Thickness(0, 8, 0, 0) });
        }

        return section;
    }

    /// <param name="article">Article palettisé quand il n'est pas (encore) dans la base : caisse issue du colisage.</param>
    public static FlowDocument BuildDocument(Packaging p, Solution s, LoadUnit unit, Database db, IReadOnlyDictionary<Guid, Color> colors, double pageWidth,
        double pageHeight, Article? article = null)
    {
        var doc = NewDocument(pageWidth, pageHeight);
        var dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        var muted = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        Article? Find(Guid? id) => article != null && id == article.Id ? article : db.FindArticle(id);

        var subtitle = $"{p.Code}{(string.IsNullOrWhiteSpace(p.Name) ? "" : " – " + p.Name)} · {p.KindLabel} · {s.Title}";

        // Première page : cartouche (informations, palette choisie, caisse du catalogue), spécification.
        var homogeneous = p.Kind == PackagingKind.Homogene ? Find(p.ArticleId) : null;
        var infos = new List<(string, string)>();
        if (homogeneous is { } a)
        {
            infos.Add(("Article", $"{a.DisplayName} · {a.KindLabel}"));
            infos.Add(("Dimensions et poids", $"{a.DimensionsText} mm · {a.Weight.ToString(Formats.UnitWeight, Fr)} kg" +
                                               (a.CaseQuantity is { } q ? $" · {q.ToString("#,0", Fr)} produit(s) par caisse" : "")));
            infos.Add(("Client", string.IsNullOrWhiteSpace(a.Client) ? "" : db.ClientLabel(a.Client)));
        }
        else
        {
            infos.Add(("Contenu", string.Join(" · ", p.Lines.Select(l => $"{l.Quantity.ToString("#,0", Fr)} × {db.FindArticle(l.ArticleId)?.Code}")) +
                                  (s.Units.Count > 1 ? $" (unité {unit.Index} / {s.Units.Count})" : "")));
        }

        infos.Add(("Palette", s.Base.Label + (s.Base.PhysicalCount > 1 ? $" ({s.Base.PalletName})" : $" – {s.Base.PalletName}")));
        infos.Add(("Résultat", $"{unit.Items.Count.ToString("#,0", Fr)} produits · {unit.Layers.Count} couche(s) · {unit.Metrics.TotalWeight.ToString("#,0.#", Fr)} kg · " +
                               $"{unit.Metrics.EnclosureLength:0} × {unit.Metrics.EnclosureWidth:0} × {unit.Metrics.EnclosureHeight:0} mm"));
        var pictures = new List<(BitmapSource, string)>();
        if ((db.FindPallet(p.PalletId) ?? db.FindPallet(s.Base.PalletId)) is { } pallet)
        {
            pictures.Add(PalletPicture(pallet));
        }

        if (homogeneous?.CaseContent?.CaseTypeCode is { } caseCode && db.Cases.FirstOrDefault(x => string.Equals(x.Code, caseCode, StringComparison.OrdinalIgnoreCase)) is { } caseType)
        {
            pictures.Add(CasePicture(caseType));
        }

        var view = ReferenceEquals(unit, s.FirstUnit)
            ? s
            : new Solution
            {
                Kind = s.Kind, Base = s.Base, Units = [unit], ItemsPerUnit = unit.Items.Count, LayerCount = unit.Layers.Count,
                StackLevels = s.StackLevels, StackLimitReason = s.StackLimitReason, Pattern = s.Pattern
            };
        var rows = PackagingSpec.Rows(view, p.Constraints, homogeneous);
        AddFirstPage(doc, () => [Header("Fiche palette", subtitle, dark, muted), Cartouche(infos, pictures, dark, muted), SpecBlock(rows, dark, muted), .. Messages(s)]);

        // Deuxième page : schémas (couches 1 et 2, côté, face) et vue 3D.
        const int width = 330;
        var plans = new List<(string, BitmapSource)> { ("Couche 1", Render(s, unit, p.Constraints, colors, PlanViewMode.Top, 1, width, 240)) };
        if (unit.Layers.Count > 1)
        {
            plans.Add(("Couche 2", Render(s, unit, p.Constraints, colors, PlanViewMode.Top, 2, width, 240)));
        }

        plans.Add(("Côté", Render(s, unit, p.Constraints, colors, PlanViewMode.Side, 1, width, 250)));
        plans.Add(("Face", Render(s, unit, p.Constraints, colors, PlanViewMode.Front, 1, width, 250)));
        doc.Blocks.Add(SchemasPage($"{p.Code} · {s.Title}", plans, Render3D(s, unit, p.Constraints, colors, 680, 300), dark, muted));

        AddPalletizationPlan(doc, p, s, unit, id => Find(id)?.Code ?? "", colors, dark, muted);
        return doc;
    }

    /// <summary>Tableau de spécification : groupes en bandeau, libellé, valeur, unité ou précision.</summary>
    private static Table SpecTable(IEnumerable<SpecRow> rows, Brush dark, Brush muted, bool flush = false)
    {
        var d = _density;
        var table = new Table { CellSpacing = 0, Margin = flush ? new Thickness(0) : new Thickness(0, d.Space(6), 0, d.Space(6)), FontSize = d.Font(11) };
        table.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(1.2, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var group = new TableRowGroup();
        foreach (var g in rows.GroupBy(r => r.Group))
        {
            var gr = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)) };
            gr.Cells.Add(new TableCell(new Paragraph(new Run(g.Key.ToUpperInvariant())) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = d.Tight ? new Thickness(5, 1.5, 5, 1.5) : new Thickness(6, 3, 6, 3) }) { ColumnSpan = 3 });
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
        return table;
    }

    private static void AddMessages(FlowDocument doc, Solution s)
    {
        if (s.Recommendation != null)
        {
            doc.Blocks.Add(new Paragraph(new Run(s.Recommendation)) { Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x84, 0x49)), Margin = new Thickness(0, 4, 0, 4) });
        }

        foreach (var w in s.Warnings.Concat(s.Violations))
        {
            doc.Blocks.Add(new Paragraph(new Run("• " + w)) { Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x77, 0x0E)), Margin = new Thickness(0, 1, 0, 1) });
        }
    }

    /// <summary>
    /// Fiche de colisage : produit, caisse (référence, dimensions intérieures et extérieures, paroi, tare), quantité par
    /// caisse, couches, poids brut, palettisation des caisses si une palette de destination est connue ; plans de la
    /// couche, côté et face, vue 3D de la caisse ouverte.
    /// </summary>
    public static FlowDocument BuildCaseDocument(CaseEngine.CaseSheet sheet, string? caseArticleCode, Database db, IReadOnlyDictionary<Guid, Color> colors,
        double pageWidth, double pageHeight)
    {
        if (sheet.IsMixed)
        {
            return BuildMixedCaseDocument(sheet, caseArticleCode, db, colors, pageWidth, pageHeight);
        }

        var (a, type, spec, s, axis, _, _) = sheet;
        var unit = s.FirstUnit!;
        var doc = NewDocument(pageWidth, pageHeight);
        var dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        var muted = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        var caseName = type != null ? $"{type.Code} – {type.Name}" : "Caisse spécifique";
        var subtitle = $"{(caseArticleCode != null ? caseArticleCode + " · " : "")}{a.Code} en {caseName} · {s.Title}";

        // Première page : cartouche (produit, caisse, palette de destination), spécification.
        var pictures = new List<(BitmapSource, string)>();
        if (type != null)
        {
            pictures.Add(CasePicture(type));
        }

        if (s.DestinationPallet != null && db.Pallets.FirstOrDefault(x => x.Code == s.DestinationPallet) is { } destination)
        {
            pictures.Add(PalletPicture(destination));
        }

        List<(string, string)> infos =
        [
            ("Produit", $"{a.DisplayName} · {a.KindLabel}"),
            ("Dimensions et poids", $"{a.DimensionsText} mm · {a.Weight.ToString(Formats.UnitWeight, Fr)} kg"),
            ("Client", string.IsNullOrWhiteSpace(a.Client) ? "" : db.ClientLabel(a.Client)),
            ("Article caisse", caseArticleCode ?? ""),
            ("Caisse", caseName),
            ("Résultat", $"{s.ItemsPerUnit.ToString("#,0", Fr)} produits par caisse · {(unit.Items.Sum(x => x.Weight) + spec.Tare).ToString(Formats.TotalWeight, Fr)} kg brut")
        ];

        string Mm(double v) => v.ToString("0", Fr);
        var productsWeight = unit.Items.Sum(p => p.Weight);
        var fill = type != null ? CaseEngine.InnerFill(s, db.Cases) : unit.Metrics.FillRate;
        var rows = new List<SpecRow>
        {
            new("Colisage", "Quantité par caisse", s.ItemsPerUnit.ToString("#,0", Fr), "produits"),
            new("Colisage", "Produits par couche", s.ItemsPerLayer.ToString("#,0", Fr)),
            new("Colisage", "Nombre de couches", s.LayerCount.ToString(Fr), s.LayerLimitReason),
            new("Colisage", "Schéma", s.PatternLabel, s.OrientationText),
            new("Colisage", "Remplissage du volume intérieur", fill.ToString("0.#", Fr), "%"),
            new("Caisse", "Caisse", type?.Code ?? "spécifique", type?.Name ?? ""),
            new("Caisse", "Dimensions intérieures", $"{Mm(spec.InnerLength)} × {Mm(spec.InnerWidth)} × {Mm(spec.InnerHeight)}", "mm"),
            new("Caisse", "Dimensions extérieures", $"{Mm(spec.OuterLength)} × {Mm(spec.OuterWidth)} × {Mm(spec.OuterHeight)}", "mm"),
            new("Caisse", "Épaisseur de paroi", spec.WallThickness.ToString("0.#", Fr), "mm")
        };
        if (type != null)
        {
            rows.Add(new("Caisse", "Matière", type.MaterialLabel));
        }

        if (spec.Gap > 0)
        {
            rows.Add(new("Caisse", "Jeu entre produits", spec.Gap.ToString("0.#", Fr), "mm"));
        }

        rows.Add(new("Poids", "Poids des produits", productsWeight.ToString(Formats.TotalWeight, Fr), "kg"));
        rows.Add(new("Poids", "Tare de la caisse", spec.Tare.ToString("0.###", Fr), "kg"));
        rows.Add(new("Poids", "Poids brut de la caisse", (productsWeight + spec.Tare).ToString(Formats.TotalWeight, Fr), "kg" +
            (type is { MaxWeight: > 0 } ? $" (charge maxi de la caisse {type.MaxWeight.ToString("0.#", Fr)} kg)" : "")));
        if (s.DestinationPallet != null)
        {
            rows.Add(new("Palettisation des caisses", "Palette de destination", s.DestinationPallet));
            rows.Add(new("Palettisation des caisses", "Caisses par palette", s.CasesPerPallet > 0 ? s.CasesPerPallet.ToString("#,0", Fr) : "non palettisable"));
            if (s.CasesPerPallet > 0)
            {
                rows.Add(new("Palettisation des caisses", "Produits par palette", s.ItemsPerPallet.ToString("#,0", Fr),
                    $"{s.CasesPerPallet.ToString("#,0", Fr)} caisses × {s.ItemsPerUnit.ToString("#,0", Fr)}"));
            }
        }

        AddFirstPage(doc, () => [Header("Fiche de colisage", subtitle, dark, muted), Cartouche(infos, pictures, dark, muted), SpecBlock(rows, dark, muted), .. Messages(s)]);

        // Deuxième page : schémas (couche, côté, face, caisse ouverte) et vue 3D.
        var constraints = CaseEngine.CaseConstraints(spec, axis);
        var color = type?.Color ?? "#C9A26B";
        const int width = 330;
        var plans = new List<(string, BitmapSource)>
        {
            ("Couche 1", Render(s, unit, constraints, colors, PlanViewMode.Top, 1, width, 240, caseWall: spec.WallThickness, caseColor: color)),
            ("Côté", Render(s, unit, constraints, colors, PlanViewMode.Side, 1, width, 240, caseWall: spec.WallThickness, caseColor: color)),
            ("Face", Render(s, unit, constraints, colors, PlanViewMode.Front, 1, width, 240, caseWall: spec.WallThickness, caseColor: color))
        };
        doc.Blocks.Add(SchemasPage($"{a.Code} en {caseName}", plans,
            Render3D(s, unit, constraints, colors, 680, 300, new Scene3DBuilder.CaseRender(spec.WallThickness, color, true)), dark, muted));
        return doc;
    }

    /// <summary>
    /// Fiche de colisage d'une caisse mixte (plusieurs articles) : même mise en page que la fiche d'un article —
    /// cartouche, spécification, schémas et vue 3D de la caisse ouverte — avec la composition (chaque article, sa
    /// quantité, son poids) et la façon dont la caisse est remplie (stratégie, niveaux, règles de pose).
    /// </summary>
    private static FlowDocument BuildMixedCaseDocument(CaseEngine.CaseSheet sheet, string? caseArticleCode, Database db, IReadOnlyDictionary<Guid, Color> colors,
        double pageWidth, double pageHeight)
    {
        var (_, type, spec, s, axis, _, _) = sheet;
        var unit = sheet.ShownUnit;
        Article? Find(Guid id) => db.FindArticle(id) ?? sheet.Lines?.FirstOrDefault(l => l.Article.Id == id).Article;
        var lines = sheet.ContentLines(Find);
        var content = sheet.ContentText(Find);
        var doc = NewDocument(pageWidth, pageHeight);
        var dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        var muted = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        var caseName = type != null ? $"{type.Code} – {type.Name}" : "Caisse spécifique";
        var total = unit.Items.Count;
        var productsWeight = unit.Items.Sum(p => p.Weight);
        var subtitle = $"{(caseArticleCode != null ? caseArticleCode + " · " : "")}caisse mixte en {caseName} · {content}";

        var pictures = new List<(BitmapSource, string)>();
        if (type != null)
        {
            pictures.Add(CasePicture(type));
        }

        if (s.DestinationPallet != null && db.Pallets.FirstOrDefault(x => x.Code == s.DestinationPallet) is { } destination)
        {
            pictures.Add(PalletPicture(destination));
        }

        var clients = lines.Select(l => l.Article.Client).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<(string, string)> infos =
        [
            ("Contenu", $"{content} = {total.ToString("#,0", Fr)} produits"),
            ("Articles", string.Join(" · ", lines.Select(l => $"{l.Article.Code} : {l.Article.DimensionsText} mm, {l.Article.Weight.ToString(Formats.UnitWeight, Fr)} kg"))),
            ("Client", clients.Count == 1 ? db.ClientLabel(clients[0]) : ""),
            ("Article caisse", caseArticleCode ?? ""),
            ("Caisse", caseName),
            ("Résultat", $"{total.ToString("#,0", Fr)} produits par caisse · {(productsWeight + spec.Tare).ToString(Formats.TotalWeight, Fr)} kg brut")
        ];

        string Mm(double v) => v.ToString("0", Fr);
        var fill = unit.Metrics.FillRate;
        var support = CaseEngine.CaseConstraints(spec, axis).MinSupportPercent;
        var rows = new List<SpecRow>();
        foreach (var (article, quantity) in lines)
        {
            var weight = unit.Items.Where(p => p.ArticleId == article.Id).Sum(p => p.Weight);
            rows.Add(new("Composition", $"{article.Code}{(string.IsNullOrWhiteSpace(article.Designation) ? "" : " – " + article.Designation)}", quantity.ToString("#,0", Fr),
                $"× {article.Weight.ToString(Formats.UnitWeight, Fr)} kg = {weight.ToString(Formats.TotalWeight, Fr)} kg · {article.KindLabel}"));
        }

        rows.Add(new("Colisage", "Quantité par caisse", total.ToString("#,0", Fr), $"produits ({content})"));
        rows.Add(new("Colisage", "Niveaux de pose", unit.Layers.Count.ToString(Fr), "du fond vers le haut"));
        rows.Add(new("Colisage", "Stratégie de remplissage", s.Title, s.Description));
        rows.Add(new("Colisage", "Règles de pose", $"appui ≥ {support.ToString("0", Fr)} %", "lourd sous léger, charge reçue par chaque produit limitée"));
        rows.Add(new("Colisage", "Remplissage du volume intérieur", fill.ToString("0.#", Fr), "%"));
        rows.Add(new("Caisse", "Caisse", type?.Code ?? "spécifique", type?.Name ?? ""));
        rows.Add(new("Caisse", "Dimensions intérieures", $"{Mm(spec.InnerLength)} × {Mm(spec.InnerWidth)} × {Mm(spec.InnerHeight)}", "mm"));
        rows.Add(new("Caisse", "Dimensions extérieures", $"{Mm(spec.OuterLength)} × {Mm(spec.OuterWidth)} × {Mm(spec.OuterHeight)}", "mm"));
        rows.Add(new("Caisse", "Épaisseur de paroi", spec.WallThickness.ToString("0.#", Fr), "mm"));
        if (type != null)
        {
            rows.Add(new("Caisse", "Matière", type.MaterialLabel));
        }

        if (spec.Gap > 0)
        {
            rows.Add(new("Caisse", "Jeu entre produits", spec.Gap.ToString("0.#", Fr), "mm"));
        }

        rows.Add(new("Poids", "Poids des produits", productsWeight.ToString(Formats.TotalWeight, Fr), "kg"));
        rows.Add(new("Poids", "Tare de la caisse", spec.Tare.ToString("0.###", Fr), "kg"));
        rows.Add(new("Poids", "Poids brut de la caisse", (productsWeight + spec.Tare).ToString(Formats.TotalWeight, Fr), "kg" +
            (type is { MaxWeight: > 0 } ? $" (charge maxi de la caisse {type.MaxWeight.ToString("0.#", Fr)} kg)" : "")));
        if (s.DestinationPallet != null)
        {
            rows.Add(new("Palettisation des caisses", "Palette de destination", s.DestinationPallet));
            rows.Add(new("Palettisation des caisses", "Caisses par palette", s.CasesPerPallet > 0 ? s.CasesPerPallet.ToString("#,0", Fr) : "non palettisable"));
            if (s.CasesPerPallet > 0)
            {
                rows.Add(new("Palettisation des caisses", "Produits par palette", (s.CasesPerPallet * total).ToString("#,0", Fr),
                    $"{s.CasesPerPallet.ToString("#,0", Fr)} caisses × {total.ToString("#,0", Fr)}"));
            }
        }

        var explanation = "Remplissage : chaque produit est posé au fond de la caisse ou sur d'autres produits, dans un coin libre, sans chevauchement ; " +
                          $"il doit reposer sur au moins {support.ToString("0", Fr)} % de sa surface, les plus lourds en dessous, et ne porter que la charge qu'il supporte. " +
                          "Les articles sont regroupés pour faciliter la préparation.";
        AddFirstPage(doc, () =>
        [
            Header("Fiche de colisage", subtitle, dark, muted), Cartouche(infos, pictures, dark, muted), SpecBlock(rows, dark, muted),
            new Paragraph(new Run(explanation)) { Foreground = muted, FontSize = _density.Font(11), Margin = new Thickness(0, 4, 0, 4) },
            .. Messages(s)
        ]);

        // Deuxième page : schémas (niveaux, côté, face) et vue 3D de la caisse ouverte, couleurs par article.
        var constraints = CaseEngine.CaseConstraints(spec, axis);
        var color = type?.Color ?? "#C9A26B";
        const int width = 330;
        var plans = new List<(string, BitmapSource)>
        {
            ("Niveau 1 (fond)", Render(s, unit, constraints, colors, PlanViewMode.Top, 1, width, 240, caseWall: spec.WallThickness, caseColor: color))
        };
        if (unit.Layers.Count > 1)
        {
            plans.Add(("Niveau 2", Render(s, unit, constraints, colors, PlanViewMode.Top, 2, width, 240, caseWall: spec.WallThickness, caseColor: color)));
        }

        plans.Add(("Côté", Render(s, unit, constraints, colors, PlanViewMode.Side, 1, width, 240, caseWall: spec.WallThickness, caseColor: color)));
        plans.Add(("Face", Render(s, unit, constraints, colors, PlanViewMode.Front, 1, width, 240, caseWall: spec.WallThickness, caseColor: color)));
        doc.Blocks.Add(SchemasPage($"Caisse mixte en {caseName} · {content}", plans,
            Render3D(s, unit, constraints, colors, 680, 300, new Scene3DBuilder.CaseRender(spec.WallThickness, color, true)), dark, muted));
        doc.Blocks.Add(Legend(lines, colors, dark));
        return doc;
    }

    /// <summary>Légende des couleurs des schémas : un carré de couleur par article.</summary>
    private static Paragraph Legend(IReadOnlyList<(Article Article, int Quantity)> lines, IReadOnlyDictionary<Guid, Color> colors, Brush dark)
    {
        var p = new Paragraph { Margin = new Thickness(0, 6, 0, 0), FontSize = 11, Foreground = dark };
        foreach (var (article, quantity) in lines)
        {
            var c = colors.TryGetValue(article.Id, out var col) ? col : Colors.SteelBlue;
            p.Inlines.Add(new Run("■ ") { Foreground = new SolidColorBrush(c), FontSize = 14 });
            p.Inlines.Add(new Run($"{article.Code} × {quantity.ToString("#,0", Fr)}     "));
        }

        return p;
    }

    /// <summary>
    /// Fiche de colisage d'un article caisse dont le contenu détaillé n'est pas connu (quantité par caisse seulement :
    /// caisse importée ou créée avant 0.1.3) : caisse, quantité, poids, palettisation enregistrée ; pas de plans.
    /// </summary>
    public static FlowDocument BuildCaseSummaryDocument(Article box, Packaging? palletPackaging, Database db, double pageWidth, double pageHeight)
    {
        var doc = NewDocument(pageWidth, pageHeight);
        var dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50));
        var muted = new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
        var subtitle = $"{box.DisplayName} · {box.CaseQuantity?.ToString("#,0", Fr)} produit(s) par caisse";
        var pictures = new List<(BitmapSource, string)>();
        if (palletPackaging != null && db.FindPallet(palletPackaging.PalletId) is { } pallet)
        {
            pictures.Add(PalletPicture(pallet));
        }

        List<(string, string)> infos =
        [
            ("Article caisse", $"{box.DisplayName} · {box.KindLabel}"),
            ("Client", string.IsNullOrWhiteSpace(box.Client) ? "" : db.ClientLabel(box.Client)),
            ("Contenu", "Quantité par caisse connue, produit contenu non renseigné : pas de plan de colisage.")
        ];
        var rows = new List<SpecRow>
        {
            new("Colisage", "Quantité par caisse", box.CaseQuantity?.ToString("#,0", Fr) ?? "", "produits"),
            new("Caisse", "Dimensions extérieures", box.DimensionsText, "mm"),
            new("Caisse", "Poids brut de la caisse", box.Weight.ToString(Formats.TotalWeight, Fr), "kg")
        };
        if (palletPackaging?.Solution is { } ps && box.CaseQuantity is { } quantity)
        {
            rows.Add(new("Palettisation des caisses", "Conditionnement", palletPackaging.DisplayName));
            rows.Add(new("Palettisation des caisses", "Caisses par palette", ps.ItemsPerUnit.ToString("#,0", Fr), ps.Base.Label));
            rows.Add(new("Palettisation des caisses", "Produits par palette", ((long)ps.ItemsPerUnit * quantity).ToString("#,0", Fr),
                $"{ps.ItemsPerUnit.ToString("#,0", Fr)} caisses × {quantity.ToString("#,0", Fr)}"));
        }

        AddFirstPage(doc, () => [Header("Fiche de colisage", subtitle, dark, muted), Cartouche(infos, pictures, dark, muted), SpecBlock(rows, dark, muted)]);
        return doc;
    }

    /// <summary>
    /// Plan de palettisation (fin de fiche, nouvelle page) : vue 3D, tableau des couches (produits par couche, cote,
    /// intercalaire), puis pour chaque plan de couche distinct la vue de dessus numérotée et la position de chaque produit.
    /// </summary>
    private static void AddPalletizationPlan(FlowDocument doc, Packaging p, Solution s, LoadUnit unit, Func<Guid, string> code,
        IReadOnlyDictionary<Guid, Color> colors, Brush dark, Brush muted)
    {
        var groups = PalletizationPlan.Groups(unit, code);
        var section = new Section { BreakPageBefore = true };
        section.Blocks.Add(new Paragraph(new Run("Plan de palettisation")) { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = dark, Margin = new Thickness(0, 0, 0, 2) });
        section.Blocks.Add(new Paragraph(new Run(
            $"{p.Code} · {unit.Items.Count} produit(s) en {unit.Layers.Count} couche(s) sur {s.Base.Label} · " +
            $"intercalaires : {PalletizationPlan.SlipSheetSummary(unit)}" +
            (s.Units.Count > 1 ? $" · unité {unit.Index} / {s.Units.Count}" : "")))
        { Foreground = muted, Margin = new Thickness(0, 0, 0, 8) });


        // Tableau des couches (du bas vers le haut).
        var table = new Table { CellSpacing = 0 };
        foreach (var w in new[] { 1.0, 1.0, 1.2, 1.2, 1.2, 1.6 })
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
        }

        var rows = new TableRowGroup();
        var header = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)) };
        foreach (var h in new[] { "Couche", "Plan", "Produits", "Z (mm)", "Hauteur (mm)", "Intercalaire dessous" })
        {
            header.Cells.Add(new TableCell(new Paragraph(new Run(h)) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 3, 6, 3) }));
        }

        rows.Rows.Add(header);
        var odd = false;
        foreach (var r in PalletizationPlan.LayerRows(unit, groups))
        {
            var row = new TableRow { Background = odd ? new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF7)) : Brushes.White };
            row.Cells.Add(Cell(r.Layer.ToString(Fr), dark, bold: true));
            row.Cells.Add(Cell(r.Plan, dark));
            row.Cells.Add(Cell(r.Count.ToString(Fr), dark, bold: true));
            row.Cells.Add(Cell(r.Z.ToString("0", Fr), dark));
            row.Cells.Add(Cell(r.Height.ToString("0", Fr), dark));
            row.Cells.Add(Cell(r.SlipSheetBelow ? "oui" : "", dark));
            rows.Rows.Add(row);
            odd = !odd;
        }

        table.RowGroups.Add(rows);
        section.Blocks.Add(table);
        doc.Blocks.Add(section);

        // Un plan par disposition de couche distincte : vue numérotée + positions.
        foreach (var g in groups)
        {
            var plan = new Section { BreakPageBefore = true };
            plan.Blocks.Add(new Paragraph(new Run($"Plan {g.Name} — couche(s) {g.LayersText}")) { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = dark, Margin = new Thickness(0, 0, 0, 2) });
            plan.Blocks.Add(new Paragraph(new Run(
                $"{g.Count} produit(s) par couche · {g.Layers.Count} couche(s) · intercalaire dessous : " +
                (g.SlipSheetLayers.Count == 0 ? "non" : g.SlipSheetLayers.Count == g.Layers.Count ? "oui" : $"couches {PalletizationPlan.Ranges(g.SlipSheetLayers)}") +
                " · position = coin du produit depuis le coin de la palette (X le long de la longueur, Y le long de la largeur)"))
            { Foreground = muted, Margin = new Thickness(0, 0, 0, 6) });
            var dense = g.Count > MaxNumberedPositions;
            var image = Render(s, unit, p.Constraints, colors, PlanViewMode.Top, g.RepresentativeLayer, 700, 400, showNumbers: !dense);
            plan.Blocks.Add(new BlockUIContainer(new Image { Source = image, Width = 700 }) { Margin = new Thickness(0, 0, 0, 6) });
            if (dense)
            {
                // Couche très dense (petits produits) : description par rangées au lieu d'une ligne par produit.
                plan.Blocks.Add(RowsTable(g, dark, muted));
                doc.Blocks.Add(plan);
                continue;
            }

            var pos = new Table { CellSpacing = 0 };
            foreach (var w in new[] { 0.7, 1.6, 1.0, 1.0, 1.6, 1.8 })
            {
                pos.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
            }

            var pr = new TableRowGroup();
            var ph = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)) };
            foreach (var h in new[] { "N°", "Article", "X (mm)", "Y (mm)", "Empreinte posée (mm)", "Forme" })
            {
                ph.Cells.Add(new TableCell(new Paragraph(new Run(h)) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 3, 6, 3) }));
            }

            pr.Rows.Add(ph);
            var alt = false;
            foreach (var q in g.Positions)
            {
                var row = new TableRow { Background = alt ? new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF7)) : Brushes.White };
                row.Cells.Add(Cell(q.Number.ToString(Fr), dark, bold: true));
                row.Cells.Add(Cell(q.Article, dark));
                row.Cells.Add(Cell(q.X.ToString("0", Fr), dark));
                row.Cells.Add(Cell(q.Y.ToString("0", Fr), dark));
                row.Cells.Add(Cell($"{q.DX:0} × {q.DY:0}", dark));
                row.Cells.Add(Cell(q.Shape, muted));
                pr.Rows.Add(row);
                alt = !alt;
            }

            pos.RowGroups.Add(pr);
            plan.Blocks.Add(pos);
            doc.Blocks.Add(plan);
        }
    }

    /// <summary>Au-delà, une couche est décrite par rangées (le plan numéroté deviendrait illisible et ferait des dizaines de pages).</summary>
    private const int MaxNumberedPositions = 150;

    /// <summary>Rangées d'une couche (le long de la largeur, Y) : nombre de produits par rangée, par article.</summary>
    private static Table RowsTable(PlanGroup g, Brush dark, Brush muted)
    {
        var rowsByArticle = g.Positions
            .GroupBy(q => (q.Article, q.Shape, Footprint: $"{q.DX:0} × {q.DY:0}"))
            .Select(a =>
            {
                var rows = a.GroupBy(q => Math.Round(q.Y / 5)).Select(r => r.Count()).ToList();
                var counts = rows.GroupBy(n => n).OrderByDescending(n => n.Count())
                    .Select(n => $"{n.Count()} rangée(s) de {n.Key}");
                return (a.Key.Article, Count: a.Count(), a.Key.Footprint, a.Key.Shape, Rows: string.Join(" + ", counts),
                    X: a.Min(q => q.X), Y: a.Min(q => q.Y));
            }).ToList();

        var table = new Table { CellSpacing = 0 };
        foreach (var w in new[] { 1.4, 0.9, 1.3, 1.4, 2.6, 1.2 })
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
        }

        var group = new TableRowGroup();
        var header = new TableRow { Background = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E)) };
        foreach (var h in new[] { "Article", "Produits", "Empreinte (mm)", "Forme", "Disposition (rangées le long de la largeur)", "Départ X / Y" })
        {
            header.Cells.Add(new TableCell(new Paragraph(new Run(h)) { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 3, 6, 3) }));
        }

        group.Rows.Add(header);
        foreach (var r in rowsByArticle)
        {
            var row = new TableRow();
            row.Cells.Add(Cell(r.Article, dark, bold: true));
            row.Cells.Add(Cell(r.Count.ToString(Fr), dark, bold: true));
            row.Cells.Add(Cell(r.Footprint, dark));
            row.Cells.Add(Cell(r.Shape, muted));
            row.Cells.Add(Cell(r.Rows, dark));
            row.Cells.Add(Cell($"{r.X.ToString("0", Fr)} / {r.Y.ToString("0", Fr)}", dark));
            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>Vue 3D 3/4 de l'unité rendue hors écran (Viewport3D WPF), pour la fiche imprimée.</summary>
    public static BitmapSource Render3D(Solution s, LoadUnit unit, PackagingConstraints c, IReadOnlyDictionary<Guid, Color> colors, int width, int height,
        Scene3DBuilder.CaseRender? caseRender = null)
    {
        var model = Scene3DBuilder.Build(s, unit, c, id => colors.TryGetValue(id, out var col) ? col : Colors.SteelBlue, int.MaxValue, caseRender).Root;
        return RenderModel(model, width, height);
    }

    /// <summary>Rendu hors écran d'une scène 3D (vue 3/4, cadrage sur la scène sans son ombre), fond clair.</summary>
    public static BitmapSource RenderModel(System.Windows.Media.Media3D.Model3D model, int width, int height)
    {
        const double scale = 2;
        var bounds = Scene3DBuilder.BoundsOf(model);
        var center = new System.Windows.Media.Media3D.Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
        var radius = Math.Max(0.05, Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ) / 2);
        const double fov = 30;
        var halfH = fov / 2 * Math.PI / 180;
        var halfV = Math.Atan(Math.Tan(halfH) * height / width);
        var distance = radius / Math.Sin(Math.Min(halfH, halfV)) * 1.1;
        var dir = new System.Windows.Media.Media3D.Vector3D(-0.75, -0.9, 0.6);
        dir.Normalize();
        var position = center + dir * distance;
        var camera = new System.Windows.Media.Media3D.PerspectiveCamera(position, center - position, new System.Windows.Media.Media3D.Vector3D(0, 0, 1), fov)
        {
            NearPlaneDistance = Math.Max(0.001, (distance - radius) * 0.5),
            FarPlaneDistance = distance + radius * 4
        };
        var lights = new System.Windows.Media.Media3D.Model3DGroup();
        lights.Children.Add(new System.Windows.Media.Media3D.AmbientLight(Color.FromRgb(0x60, 0x60, 0x60)));
        lights.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(Color.FromRgb(0xC0, 0xC0, 0xC0), new System.Windows.Media.Media3D.Vector3D(1, 1.5, -2)));
        lights.Children.Add(new System.Windows.Media.Media3D.DirectionalLight(Color.FromRgb(0x50, 0x50, 0x50), new System.Windows.Media.Media3D.Vector3D(-1, -0.5, -1)));
        var viewport = new Viewport3D { Camera = camera, Width = width, Height = height };
        viewport.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D { Content = lights });
        viewport.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D { Content = model });
        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xF9)), Child = viewport, Width = width, Height = height };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(host);
        bmp.Freeze();
        return bmp;
    }

    private static TableCell Cell(string text, Brush brush, bool bold = false, bool right = false) =>
        new(new Paragraph(new Run(text))
        {
            Margin = _density.Tight ? new Thickness(5, 0.5, 5, 0.5) : new Thickness(6, 2, 6, 2),
            Foreground = brush,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left
        });

    public static BitmapSource Render(Solution s, LoadUnit unit, PackagingConstraints c, IReadOnlyDictionary<Guid, Color> colors,
        PlanViewMode mode, int layer, int width, int height, bool showNumbers = false, double caseWall = 0, string? caseColor = null)
    {
        const double scale = 2;
        var view = new PlanView2D
        {
            Solution = s, Unit = unit, Constraints = c, ColorMap = colors, Mode = mode, Layer = layer, Width = width, Height = height, ShowNumbers = showNumbers,
            CaseWall = caseWall, CaseColor = caseColor
        };
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
