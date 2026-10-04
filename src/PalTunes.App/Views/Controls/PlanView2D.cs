using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PalTunes.App.Services;
using PalTunes.Core.Models;

namespace PalTunes.App.Views.Controls;

public enum PlanViewMode
{
    Top,
    Side,
    Front
}

/// <summary>
/// Vue 2D cotée d'une unité de charge : dessus (une couche, la couche inférieure en pointillés pour lire le croisement),
/// côté (X × Z) ou face (Y × Z). Palettes physiques, débords, intercalaires, coiffe et cornières sont représentés.
/// </summary>
public sealed class PlanView2D : FrameworkElement
{
    public static readonly DependencyProperty SolutionProperty = Register(nameof(Solution), typeof(Solution), null);
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit), typeof(LoadUnit), null);
    public static readonly DependencyProperty ModeProperty = Register(nameof(Mode), typeof(PlanViewMode), PlanViewMode.Top);
    public static readonly DependencyProperty LayerProperty = Register(nameof(Layer), typeof(int), 1);
    public static readonly DependencyProperty ConstraintsProperty = Register(nameof(Constraints), typeof(PackagingConstraints), null);
    public static readonly DependencyProperty ColorMapProperty = Register(nameof(ColorMap), typeof(IReadOnlyDictionary<Guid, Color>), null);
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), null);
    public static readonly DependencyProperty CaseClosedProperty = Register(nameof(CaseClosed), typeof(bool), false);
    public static readonly DependencyProperty CaseWallProperty = Register(nameof(CaseWall), typeof(double), 0.0);
    public static readonly DependencyProperty CaseColorProperty = Register(nameof(CaseColor), typeof(string), null);
    public static readonly DependencyProperty ShowNumbersProperty = Register(nameof(ShowNumbers), typeof(bool), false);

    private static DependencyProperty Register(string name, Type type, object? def) =>
        DependencyProperty.Register(name, type, typeof(PlanView2D), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public Solution? Solution { get => (Solution?)GetValue(SolutionProperty); set => SetValue(SolutionProperty, value); }
    public LoadUnit? Unit { get => (LoadUnit?)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public PlanViewMode Mode { get => (PlanViewMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public int Layer { get => (int)GetValue(LayerProperty); set => SetValue(LayerProperty, value); }
    public PackagingConstraints? Constraints { get => (PackagingConstraints?)GetValue(ConstraintsProperty); set => SetValue(ConstraintsProperty, value); }
    public IReadOnlyDictionary<Guid, Color>? ColorMap { get => (IReadOnlyDictionary<Guid, Color>?)GetValue(ColorMapProperty); set => SetValue(ColorMapProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Colisage : caisse fermée, vue de l'extérieur (dimensions extérieures) au lieu du plan intérieur.</summary>
    public bool CaseClosed { get => (bool)GetValue(CaseClosedProperty); set => SetValue(CaseClosedProperty, value); }

    public double CaseWall { get => (double)GetValue(CaseWallProperty); set => SetValue(CaseWallProperty, value); }

    /// <summary>Vue de dessus : numéro de chaque produit dans la couche (ordre du plan de palettisation imprimé).</summary>
    public bool ShowNumbers { get => (bool)GetValue(ShowNumbersProperty); set => SetValue(ShowNumbersProperty, value); }

    /// <summary>Couleur de la caisse fermée (#RRGGBB) ; kraft par défaut.</summary>
    public string? CaseColor { get => (string?)GetValue(CaseColorProperty); set => SetValue(CaseColorProperty, value); }

    private static readonly Pen OutlinePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)), 1));
    private static readonly Pen BelowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0x5D, 0x6D, 0x7E)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen DimPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)), 1));
    private static readonly Pen LoadDimPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xC1)), 1));
    private static readonly Pen EncDimPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), 1));
    private static readonly Pen BoardPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x00, 0x00)), 0.8));
    private static readonly Brush CaseFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF6, 0xEE, 0xE2)));
    private static readonly Pen CasePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x63)), 2));
    private static readonly Brush ClosedCaseFill = Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0xB8, 0x84)));
    private static readonly Brush TapeBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xF0, 0xE0, 0xB8)));
    private static readonly Pen FoldPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x63)), 1) { DashStyle = DashStyles.Dash });
    private static readonly Pen FilmPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x17, 0xA5, 0x89)), 1.6) { DashStyle = DashStyles.Dash });
    private static readonly Brush StrapBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xD0, 0x1F, 0x61, 0x8D)));
    private static readonly Pen StrapPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x61, 0x8D)), 2.5));
    private static readonly Brush CornerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x63)));
    private static readonly Brush SheetBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0xC1, 0x8B)));
    private static readonly Brush CapBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xB9, 0x92, 0x5E)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2C, 0x3E, 0x50)));
    private static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D)));
    private static readonly Typeface Font = new("Segoe UI");
    private static readonly Typeface FontBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private double _scale;
    private double _ox;
    private double _oy;
    private double _u0;
    private double _v0;

    /// <summary>Monde (u, v) → écran ; v vers le haut sauf en vue de dessus (Y vers le bas, comme un plan).</summary>
    private Point P(double u, double v) => Mode == PlanViewMode.Top
        ? new Point(_ox + (u - _u0) * _scale, _oy + (v - _v0) * _scale)
        : new Point(_ox + (u - _u0) * _scale, _oy - (v - _v0) * _scale);

    private Rect R(double u0, double v0, double u1, double v1) => new(P(u0, v0), P(u1, v1));

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var s = Solution;
        var unit = Unit ?? s?.FirstUnit;
        if (s == null || unit == null || ActualWidth < 80 || ActualHeight < 80)
        {
            return;
        }

        var c = Constraints ?? new PackagingConstraints();
        var b = s.Base;
        var m = unit.Metrics;
        if (b.IsCase && CaseClosed)
        {
            DrawClosedCase(dc, b, c.MaxTotalHeight);
            return;
        }

        var wrap = (c.Corners ? c.CornerThickness : 0) + c.FilmThickness;
        var minX = Math.Min(0, m.MinX - wrap);
        var maxX = Math.Max(b.Length, m.MaxX + wrap);
        var minY = Math.Min(0, m.MinY - wrap);
        var maxY = Math.Max(b.Width, m.MaxY + wrap);
        var top = Math.Max(m.LoadHeight + c.CapHeight, b.IsCase ? c.MaxTotalHeight : 0);

        double u0, u1, v0, v1;
        switch (Mode)
        {
            case PlanViewMode.Top:
                (u0, u1, v0, v1) = (minX, maxX, minY, maxY);
                break;
            case PlanViewMode.Side:
                (u0, u1, v0, v1) = (minX, maxX, -b.PalletHeight, top);
                break;
            default:
                (u0, u1, v0, v1) = (minY, maxY, -b.PalletHeight, top);
                break;
        }

        var left = Mode == PlanViewMode.Top ? 96.0 : 70.0;
        var right = Mode == PlanViewMode.Top ? 30.0 : 70.0;
        const double topMargin = 34, bottom = 80;
        _scale = Math.Min((ActualWidth - left - right) / Math.Max(1, u1 - u0), (ActualHeight - topMargin - bottom) / Math.Max(1, v1 - v0));
        if (_scale <= 0)
        {
            return;
        }

        var drawW = (u1 - u0) * _scale;
        var drawH = (v1 - v0) * _scale;
        _ox = left + (ActualWidth - left - right - drawW) / 2;
        _u0 = u0;
        _v0 = v0;
        _oy = Mode == PlanViewMode.Top ? topMargin + (ActualHeight - topMargin - bottom - drawH) / 2 : topMargin + (ActualHeight - topMargin - bottom - drawH) / 2 + drawH;

        var palletColor = ArticleColors.Parse(b.Color, Color.FromRgb(0xC8, 0xA1, 0x65));
        var palletBrush = Frozen(new SolidColorBrush(palletColor));
        var palletDark = Frozen(new SolidColorBrush(Scene3DBuilder.Shade(palletColor, 0.8)));

        if (Mode == PlanViewMode.Top)
        {
            DrawTop(dc, s, unit, c, palletBrush);
        }
        else
        {
            DrawElevation(dc, s, unit, c, palletBrush, palletDark);
        }

        var title = Title ?? (Mode switch
        {
            PlanViewMode.Top => LayerTitle(unit),
            PlanViewMode.Side => "Vue de côté (longueur)",
            _ => "Vue de face (largeur)"
        });
        Text(dc, title, new Point(12, 8), 12.5, TextBrush, bold: true);
    }

    /// <summary>Caisse fermée : silhouette extérieure (rabats, bande adhésive) et cotes extérieures.</summary>
    private void DrawClosedCase(DrawingContext dc, BaseInfo b, double innerHeight)
    {
        var t = Math.Max(0, CaseWall);
        var outerL = b.Length + 2 * t;
        var outerW = b.Width + 2 * t;
        var outerH = innerHeight + 2 * t;
        var (wu, wv) = Mode switch
        {
            PlanViewMode.Top => (outerL, outerW),
            PlanViewMode.Side => (outerL, outerH),
            _ => (outerW, outerH)
        };

        const double left = 70, right = 70, topMargin = 34, bottom = 60;
        _scale = Math.Min((ActualWidth - left - right) / wu, (ActualHeight - topMargin - bottom) / wv);
        if (_scale <= 0)
        {
            return;
        }

        _u0 = 0;
        _v0 = 0;
        _ox = left + (ActualWidth - left - right - wu * _scale) / 2;
        _oy = Mode == PlanViewMode.Top
            ? topMargin + (ActualHeight - topMargin - bottom - wv * _scale) / 2
            : topMargin + (ActualHeight - topMargin - bottom - wv * _scale) / 2 + wv * _scale;

        var fill = CaseColor is { Length: > 0 }
            ? Frozen(new SolidColorBrush(ArticleColors.Parse(CaseColor, Color.FromRgb(0xD9, 0xB8, 0x84))))
            : ClosedCaseFill;
        dc.DrawRectangle(fill, CasePen, R(0, 0, wu, wv));
        if (Mode == PlanViewMode.Top)
        {
            // Jonction des deux rabats et bande adhésive dans la longueur.
            dc.DrawLine(CasePen, P(0, wv / 2), P(wu, wv / 2));
            dc.DrawRectangle(TapeBrush, null, R(0, wv / 2 - 25, wu, wv / 2 + 25));
        }
        else
        {
            // Rainage des rabats et bande adhésive retombant sur les faces.
            var flap = Mode == PlanViewMode.Side ? Math.Min(outerW / 2, outerH * 0.4) : Math.Min(outerW / 2, outerH * 0.4);
            dc.DrawLine(FoldPen, P(0, wv - flap), P(wu, wv - flap));
            if (Mode == PlanViewMode.Front)
            {
                dc.DrawRectangle(TapeBrush, null, R(wu / 2 - 25, wv - 60, wu / 2 + 25, wv));
            }
        }

        var bottomY = Mode == PlanViewMode.Top ? P(0, wv).Y : P(0, 0).Y;
        DimH(dc, 0, wu, bottomY + 16, $"extérieur {wu:0}", EncDimPen);
        DimV(dc, 0, wv, P(0, 0).X - 18, $"{wv:0}", EncDimPen);
        Text(dc, Mode switch
        {
            PlanViewMode.Top => $"Caisse fermée · dessus · extérieur {outerL:0} × {outerW:0} mm",
            PlanViewMode.Side => $"Caisse fermée · côté · extérieur {outerL:0} × {outerH:0} mm",
            _ => $"Caisse fermée · face · extérieur {outerW:0} × {outerH:0} mm"
        }, new Point(12, 8), 12.5, TextBrush, bold: true);
    }

    private string LayerTitle(LoadUnit unit)
    {
        var layer = unit.Layers.FirstOrDefault(l => l.Index == Layer);
        return layer == null
            ? "Vue de dessus"
            : $"Couche {layer.Index} / {unit.Layers.Count} · plan {layer.Pattern} · {layer.Count} produit(s) · z = {layer.Z:0} mm";
    }

    private Color ColorOf(Guid id) => ColorMap != null && ColorMap.TryGetValue(id, out var c) ? c : Color.FromRgb(0x5D, 0xAD, 0xE2);

    private void DrawTop(DrawingContext dc, Solution s, LoadUnit unit, PackagingConstraints c, Brush palletBrush)
    {
        var b = s.Base;
        if (b.IsCase)
        {
            dc.DrawRectangle(CaseFill, CasePen, R(0, 0, b.Length, b.Width));
        }

        for (var i = 0; i < b.CountAlongLength && !b.IsCase; i++)
        {
            for (var j = 0; j < b.CountAlongWidth; j++)
            {
                var x = i * b.PalletLength;
                var y = j * b.PalletWidth;
                dc.DrawRectangle(palletBrush, OutlinePen, R(x, y, x + b.PalletLength, y + b.PalletWidth));
                if (b.Construction is PalletConstruction.Blocs9Semelles3 or PalletConstruction.Blocs9)
                {
                    // Planches de dessus dans le sens de la longueur propre de la palette.
                    var count = (b.Rotated ? b.PalletLength : b.PalletWidth) > 1000 ? 7 : 5;
                    for (var k = 1; k < count; k++)
                    {
                        if (b.Rotated)
                        {
                            var u = x + b.PalletLength * k / count;
                            dc.DrawLine(BoardPen, P(u, y), P(u, y + b.PalletWidth));
                        }
                        else
                        {
                            var v = y + b.PalletWidth * k / count;
                            dc.DrawLine(BoardPen, P(x, v), P(x + b.PalletLength, v));
                        }
                    }
                }
            }
        }

        var layer = Math.Clamp(Layer, 1, Math.Max(1, unit.Layers.Count));
        foreach (var p in unit.Items.Where(p => p.Layer == layer - 1))
        {
            DrawTopItem(dc, p, null, BelowPen);
        }

        foreach (var p in unit.Items.Where(p => p.Layer == layer))
        {
            var brush = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, ColorOf(p.ArticleId).R, ColorOf(p.ArticleId).G, ColorOf(p.ArticleId).B)));
            DrawTopItem(dc, p, brush, OutlinePen);
        }

        if (ShowNumbers)
        {
            var number = 0;
            foreach (var p in PalTunes.Core.Export.PalletizationPlan.ReadingOrder(unit.Items.Where(p => p.Layer == layer)))
            {
                number++;
                var size = Math.Clamp(Math.Min(p.DX, p.DY) * _scale * 0.35, 7, 16);
                var ft = Format(number.ToString(CultureInfo.CurrentCulture), size, TextBrush, true);
                var center = P(p.X + p.DX / 2, p.Y + p.DY / 2);
                dc.DrawEllipse(Brushes.White, null, center, ft.Width / 2 + 3, ft.Height / 2);
                dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
            }
        }

        var m = unit.Metrics;
        if (c.Corners && unit.Items.Count > 0)
        {
            var t = Math.Max(1, c.CornerThickness);
            var leg = Math.Max(10, c.CornerLeg);
            var (fx0, fy0, fx1, fy1) = unit.CornerBounds(m);
            foreach (var (cx, cy, sx, sy) in new[] { (fx0, fy0, 1, 1), (fx1, fy0, -1, 1), (fx0, fy1, 1, -1), (fx1, fy1, -1, -1) })
            {
                dc.DrawRectangle(CornerBrush, null, R(Math.Min(cx - sx * t, cx + sx * leg), Math.Min(cy - sy * t, cy), Math.Max(cx - sx * t, cx + sx * leg), Math.Max(cy - sy * t, cy)));
                dc.DrawRectangle(CornerBrush, null, R(Math.Min(cx - sx * t, cx), Math.Min(cy - sy * t, cy + sy * leg), Math.Max(cx - sx * t, cx), Math.Max(cy - sy * t, cy + sy * leg)));
            }
        }

        var wrap = Wrap(c);
        if (unit.Items.Count > 0)
        {
            if (c.FilmThickness > 0)
            {
                dc.DrawRectangle(null, FilmPen, R(m.MinX - wrap, m.MinY - wrap, m.MaxX + wrap, m.MaxY + wrap));
            }

            // Cerclages dans le sens de la longueur, répartis sur la largeur.
            foreach (var y in StrapPositions(m.MinY, m.MaxY, c.Straps))
            {
                dc.DrawRectangle(StrapBrush, null, R(m.MinX - wrap - 4, y - StrapWidth / 2, m.MaxX + wrap + 4, y + StrapWidth / 2));
            }
        }

        // Cotes : base (gris), charge (bleu), encombrement (orange) sous le dessin et à gauche.
        var eMinX = Math.Min(0, m.MinX - wrap);
        var eMaxX = Math.Max(b.Length, m.MaxX + wrap);
        var eMinY = Math.Min(0, m.MinY - wrap);
        var eMaxY = Math.Max(b.Width, m.MaxY + wrap);
        var bottomY = P(0, unit.Items.Count > 0 ? eMaxY : b.Width).Y;
        var leftX = P(unit.Items.Count > 0 ? eMinX : 0, 0).X;
        DimH(dc, 0, b.Length, bottomY + 14, $"{b.Length:0}", DimPen);
        DimV(dc, 0, b.Width, leftX - 16, $"{b.Width:0}", DimPen);
        if (unit.Items.Count > 0)
        {
            DimH(dc, m.MinX, m.MaxX, bottomY + 32, $"charge {m.LoadLength:0}", LoadDimPen);
            DimH(dc, eMinX, eMaxX, bottomY + 50, $"encombrement {m.EnclosureLength:0}", EncDimPen);
            DimV(dc, m.MinY, m.MaxY, leftX - 40, $"{m.LoadWidth:0}", LoadDimPen);
            DimV(dc, eMinY, eMaxY, leftX - 64, $"enc. {m.EnclosureWidth:0}", EncDimPen);
        }
    }

    private const double StrapWidth = 16;

    private static double Wrap(PackagingConstraints c) => (c.Corners ? c.CornerThickness : 0) + c.FilmThickness;

    /// <summary>Positions des cerclages, répartis régulièrement sur la dimension donnée.</summary>
    public static IEnumerable<double> StrapPositions(double min, double max, int count) =>
        Enumerable.Range(0, Math.Max(0, count)).Select(i => min + (i + 0.5) * (max - min) / count);

    private void DrawTopItem(DrawingContext dc, Placement p, Brush? fill, Pen pen)
    {
        if (p.Shape == ShapeKind.CylinderZ)
        {
            var center = P(p.X + p.DX / 2, p.Y + p.DY / 2);
            dc.DrawEllipse(fill, pen, center, p.DX / 2 * _scale, p.DY / 2 * _scale);
            if (fill != null && p.DX * _scale > 24)
            {
                dc.DrawEllipse(Brushes.White, pen, center, p.DX * 0.08 * _scale, p.DY * 0.08 * _scale);
            }

            return;
        }

        var rect = R(p.X, p.Y, p.MaxX, p.MaxY);
        rect.Inflate(-0.5, -0.5);
        dc.DrawRectangle(fill, pen, rect);
        if (fill != null && p.Shape is ShapeKind.CylinderX or ShapeKind.CylinderY)
        {
            var mid = p.Shape == ShapeKind.CylinderX
                ? (P(p.X, p.Y + p.DY / 2), P(p.MaxX, p.Y + p.DY / 2))
                : (P(p.X + p.DX / 2, p.Y), P(p.X + p.DX / 2, p.MaxY));
            dc.DrawLine(BelowPen, mid.Item1, mid.Item2);
        }
    }

    private void DrawElevation(DrawingContext dc, Solution s, LoadUnit unit, PackagingConstraints c, Brush palletBrush, Brush palletDark)
    {
        var b = s.Base;
        var side = Mode == PlanViewMode.Side;
        var h = b.PalletHeight;
        var count = side ? b.CountAlongLength : b.CountAlongWidth;
        var size = side ? b.PalletLength : b.PalletWidth;
        var t = h * 22 / 144;
        if (b.IsCase)
        {
            dc.DrawRectangle(CaseFill, CasePen, R(0, 0, side ? b.Length : b.Width, c.MaxTotalHeight));
            count = 0;
        }

        for (var i = 0; i < count; i++)
        {
            var u0 = i * size;
            var u1 = u0 + size;
            switch (b.Construction)
            {
                case PalletConstruction.Plein:
                    dc.DrawRectangle(palletBrush, OutlinePen, R(u0, -h, u1, 0));
                    break;
                case PalletConstruction.Pieds9:
                case PalletConstruction.Patins3:
                    var deck = h * 0.22;
                    dc.DrawRectangle(palletBrush, OutlinePen, R(u0, -deck, u1, 0));
                    var bw = size * 0.12;
                    foreach (var u in new[] { u0, u0 + size / 2 - bw / 2, u1 - bw })
                    {
                        dc.DrawRectangle(palletDark, OutlinePen, R(u, -h, u + bw, -deck));
                    }

                    if (b.Construction == PalletConstruction.Patins3)
                    {
                        dc.DrawRectangle(palletBrush, OutlinePen, R(u0, -h, u1, -h + h * 0.16));
                    }

                    break;
                default:
                    var skids = b.Construction == PalletConstruction.Blocs9Semelles3;
                    dc.DrawRectangle(palletBrush, OutlinePen, R(u0, -t, u1, 0));
                    dc.DrawRectangle(palletBrush, OutlinePen, R(u0, -2 * t, u1, -t));
                    var blockW = size * 0.12;
                    foreach (var u in new[] { u0, u0 + size / 2 - blockW / 2, u1 - blockW })
                    {
                        dc.DrawRectangle(palletDark, OutlinePen, R(u, skids ? -h + t : -h, u + blockW, -2 * t));
                    }

                    if (skids)
                    {
                        dc.DrawRectangle(palletBrush, OutlinePen, R(u0, -h, u1, -h + t));
                    }

                    break;
            }
        }

        // Produits : les plus éloignés d'abord (peintre), vue depuis Y = 0 (côté) ou X = 0 (face).
        IEnumerable<Placement> source = unit.Items;
        if (unit.Items.Count > 5000)
        {
            // Petits produits par milliers : seules les rangées de devant sont visibles, les autres sont cachées.
            var front = side ? unit.Items.Min(p => p.Y) : unit.Items.Min(p => p.X);
            var depth = side ? unit.Items.Max(p => p.DY) : unit.Items.Max(p => p.DX);
            source = unit.Items.Where(p => (side ? p.Y : p.X) <= front + 2 * depth);
        }

        var items = side ? source.OrderByDescending(p => p.Y) : source.OrderByDescending(p => p.X);
        foreach (var p in items)
        {
            var col = ColorOf(p.ArticleId);
            var brush = Frozen(new SolidColorBrush(p.Layer % 2 == 0 ? Scene3DBuilder.Shade(col, 0.9) : col));
            var (a0, a1) = side ? (p.X, p.MaxX) : (p.Y, p.MaxY);
            var circle = (side && p.Shape == ShapeKind.CylinderY) || (!side && p.Shape == ShapeKind.CylinderX);
            if (circle)
            {
                dc.DrawEllipse(brush, OutlinePen, P((a0 + a1) / 2, p.Z + p.DZ / 2), (a1 - a0) / 2 * _scale, p.DZ / 2 * _scale);
            }
            else
            {
                var rect = R(a0, p.Z, a1, p.MaxZ);
                rect.Inflate(-0.4, -0.4);
                dc.DrawRectangle(brush, OutlinePen, rect);
            }
        }

        var m = unit.Metrics;
        var (l0, l1) = side ? (m.MinX, m.MaxX) : (m.MinY, m.MaxY);
        if (unit.Items.Count > 0)
        {
            foreach (var layer in unit.Layers.Where(l => l.SlipSheetBelow))
            {
                dc.DrawRectangle(SheetBrush, null, R(l0, layer.Z - Math.Max(2, c.SlipSheetThickness), l1, layer.Z));
            }

            if (c.CapHeight > 0)
            {
                dc.DrawRectangle(CapBrush, OutlinePen, R(l0, m.LoadHeight, l1, m.LoadHeight + c.CapHeight));
            }

            if (c.Corners)
            {
                var ct = Math.Max(3, c.CornerThickness);
                var ch = c.CornerHeight > 0 ? c.CornerHeight : m.LoadHeight;
                var (fx0, fy0, fx1, fy1) = unit.CornerBounds(m);
                var (k0, k1) = side ? (fx0, fx1) : (fy0, fy1);
                dc.DrawRectangle(CornerBrush, null, R(k0 - ct, 0, k0, ch));
                dc.DrawRectangle(CornerBrush, null, R(k1, 0, k1 + ct, ch));
            }

            var w = Wrap(c);
            var topZ = m.LoadHeight + c.CapHeight;
            if (c.FilmThickness > 0)
            {
                // Le film enveloppe la charge et le haut de la palette.
                dc.DrawRectangle(null, FilmPen, R(l0 - w, -h * 0.35, l1 + w, topZ));
            }

            if (c.Straps > 0)
            {
                var under = -h * 0.2;
                if (side)
                {
                    // Vue de côté : la boucle du cerclage (dessus, extrémités, passage sous le plateau).
                    var loop = new Rect(P(l0 - w - 4, topZ + 4), P(l1 + w + 4, under));
                    dc.DrawRectangle(null, StrapPen, loop);
                }
                else
                {
                    foreach (var y in StrapPositions(m.MinY, m.MaxY, c.Straps))
                    {
                        dc.DrawRectangle(StrapBrush, null, R(y - StrapWidth / 2, under, y + StrapWidth / 2, topZ + 4));
                    }
                }
            }
        }

        // Cotes verticales à droite : bois, charge, total ; horizontales : base et charge.
        var length = side ? b.Length : b.Width;
        var rightU = Math.Max(length, l1 + Wrap(c));
        var x = P(rightU, 0).X;
        if (h > 0)
        {
            DimV(dc, -h, 0, x + 14, $"{h:0}", DimPen);
        }
        if (unit.Items.Count > 0)
        {
            DimV(dc, 0, m.LoadHeight, x + 14, $"{m.LoadHeight:0}", LoadDimPen);
            DimV(dc, -h, m.LoadHeight + c.CapHeight, x + 44, $"{m.EnclosureHeight:0}", EncDimPen);
            DimH(dc, l0, l1, P(0, -h).Y + 32, $"charge {(side ? m.LoadLength : m.LoadWidth):0}", LoadDimPen);
            var ew = Wrap(c);
            DimH(dc, Math.Min(0, l0 - ew), Math.Max(length, l1 + ew), P(0, -h).Y + 50,
                $"encombrement {(side ? m.EnclosureLength : m.EnclosureWidth):0}", EncDimPen);
        }

        DimH(dc, 0, length, P(0, -h).Y + 14, $"{length:0}", DimPen);
    }

    // ------------------------------------------------------------------ Cotes et texte

    private void DimH(DrawingContext dc, double u0, double u1, double y, string label, Pen pen)
    {
        var a = new Point(P(u0, 0).X, y);
        var c = new Point(P(u1, 0).X, y);
        dc.DrawLine(pen, a, c);
        dc.DrawLine(pen, new Point(a.X, y - 4), new Point(a.X, y + 4));
        dc.DrawLine(pen, new Point(c.X, y - 4), new Point(c.X, y + 4));
        var ft = Format(label, 11, pen.Brush, false);
        var x = (a.X + c.X) / 2 - ft.Width / 2;
        dc.DrawRectangle(Brushes.White, null, new Rect(x - 2, y - ft.Height / 2, ft.Width + 4, ft.Height));
        dc.DrawText(ft, new Point(x, y - ft.Height / 2));
    }

    private void DimV(DrawingContext dc, double v0, double v1, double x, string label, Pen pen)
    {
        var a = new Point(x, P(0, v0).Y);
        var c = new Point(x, P(0, v1).Y);
        dc.DrawLine(pen, a, c);
        dc.DrawLine(pen, new Point(x - 4, a.Y), new Point(x + 4, a.Y));
        dc.DrawLine(pen, new Point(x - 4, c.Y), new Point(x + 4, c.Y));
        var ft = Format(label, 11, pen.Brush, false);
        dc.PushTransform(new RotateTransform(-90, x, (a.Y + c.Y) / 2));
        var px = x - ft.Width / 2;
        var py = (a.Y + c.Y) / 2 - ft.Height / 2;
        dc.DrawRectangle(Brushes.White, null, new Rect(px - 2, py, ft.Width + 4, ft.Height));
        dc.DrawText(ft, new Point(px, py));
        dc.Pop();
    }

    private FormattedText Format(string text, double size, Brush brush, bool bold) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? FontBold : Font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private void Text(DrawingContext dc, string text, Point at, double size, Brush brush, bool bold = false) =>
        dc.DrawText(Format(text, size, brush, bold), at);
}
