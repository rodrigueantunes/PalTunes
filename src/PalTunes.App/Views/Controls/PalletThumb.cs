using System.Windows;
using System.Windows.Media;
using PalTunes.App.Services;
using PalTunes.Core.Models;

namespace PalTunes.App.Views.Controls;

/// <summary>Vignette d'une palette : vue de face selon sa construction et sa couleur (listes).</summary>
public sealed class PalletThumb : FrameworkElement
{
    public static readonly DependencyProperty PalletProperty = DependencyProperty.Register(
        nameof(Pallet), typeof(PalletType), typeof(PalletThumb), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public PalletType? Pallet
    {
        get => (PalletType?)GetValue(PalletProperty);
        set => SetValue(PalletProperty, value);
    }

    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromArgb(0x90, 0x2C, 0x3E, 0x50)), 0.8);

    static PalletThumb() => Outline.Freeze();

    protected override void OnRender(DrawingContext dc)
    {
        var p = Pallet;
        if (p == null || ActualWidth < 10 || ActualHeight < 6)
        {
            return;
        }

        var color = ArticleColors.Parse(p.Color, Color.FromRgb(0xC8, 0xA1, 0x65));
        var light = new SolidColorBrush(color);
        var dark = new SolidColorBrush(Scene3DBuilder.Shade(color, 0.78));
        var w = ActualWidth;
        // Hauteur proportionnelle (exagérée × 2,5 pour rester lisible).
        var h = Math.Min(ActualHeight, w * p.Height / Math.Max(1, p.Length) * 2.5);
        var y0 = (ActualHeight - h) / 2;
        Rect Part(double u0, double v0, double u1, double v1) => new(u0 * w, y0 + v0 * h, (u1 - u0) * w, (v1 - v0) * h);

        switch (p.Construction)
        {
            case PalletConstruction.Plein:
                dc.DrawRoundedRectangle(light, Outline, Part(0, 0, 1, 1), 1.5, 1.5);
                break;
            case PalletConstruction.Pieds9:
            case PalletConstruction.Patins3:
                dc.DrawRoundedRectangle(light, Outline, Part(0, 0, 1, 0.3), 1.5, 1.5);
                foreach (var u in new[] { 0.02, 0.44, 0.86 })
                {
                    dc.DrawRectangle(dark, Outline, Part(u, 0.3, u + 0.12, p.Construction == PalletConstruction.Patins3 ? 0.82 : 1));
                }

                if (p.Construction == PalletConstruction.Patins3)
                {
                    dc.DrawRoundedRectangle(light, Outline, Part(0, 0.82, 1, 1), 1, 1);
                }

                break;
            default:
                dc.DrawRectangle(light, Outline, Part(0, 0, 1, 0.16));
                dc.DrawRectangle(light, Outline, Part(0, 0.16, 1, 0.32));
                var skids = p.Construction == PalletConstruction.Blocs9Semelles3;
                foreach (var u in new[] { 0, 0.44, 0.88 })
                {
                    dc.DrawRectangle(dark, Outline, Part(u, 0.32, u + 0.12, skids ? 0.84 : 1));
                }

                if (skids)
                {
                    dc.DrawRectangle(light, Outline, Part(0, 0.84, 1, 1));
                }

                break;
        }
    }
}
