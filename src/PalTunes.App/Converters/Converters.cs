using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PalTunes.Core.Models;

namespace PalTunes.App.Converters;

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>null, chaîne vide ou 0 → Visible ; sinon Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value == null || value is string { Length: 0 } || value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Non null (ni chaîne vide, ni 0) → Visible.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value == null || value is string { Length: 0 } || value is 0 ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>« #RRGGBB » → pinceau (gris clair si invalide).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Brush(value as string);

    public static Brush Brush(string? hex)
    {
        try
        {
            if (hex is { Length: > 0 } && ColorConverter.ConvertFromString(hex) is Color c)
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException)
        {
        }

        return Brushes.LightGray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Valeur == paramètre (RadioButton liés à une énumération).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value != null && parameter != null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null)
        {
            return Binding.DoNothing;
        }

        var enumType = targetType.IsEnum ? targetType : Nullable.GetUnderlyingType(targetType);
        return enumType is { IsEnum: true } ? Enum.Parse(enumType, parameter.ToString()!) : parameter;
    }
}

/// <summary>Valeur == paramètre → Visible. Paramètre « A|B » : l'une des valeurs ; « !A » : différent de A.</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var p = parameter?.ToString() ?? "";
        var negate = p.StartsWith('!');
        var options = (negate ? p[1..] : p).Split('|');
        var match = options.Contains(value?.ToString() ?? "");
        return match ^ negate ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Libellé métier d'une énumération du modèle.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Label(value);

    public static string Label(object? value) => value switch
    {
        ArticleKind k => ArticleSchema.KindLabel(k),
        OrientationRule.HautImpose => "Haut imposé",
        OrientationRule.Libre => "Libre (6 orientations)",
        CoilAxis.Vertical => "Axe vertical",
        CoilAxis.Horizontal => "Axe horizontal",
        CoilAxis.Indifferent => "Indifférent (le meilleur)",
        PalletConstruction c => PalletType.ConstructionLabel(c),
        CaseMaterial m => CaseType.MaterialName(m),
        PalletMaterial.Plastique => "Plastique",
        PalletMaterial.Carton => "Carton",
        PalletMaterial.Metal => "Métal",
        PalletMaterial.BoisMoule => "Bois moulé",
        PalletMaterial.Bois => "Bois",
        PackagingKind.Homogene => "Homogène (mono-article)",
        PackagingKind.Heterogene => "Hétérogène (multi-articles)",
        null => "",
        _ => value.ToString() ?? ""
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Type d'article → glyphe Segoe MDL2 Assets.</summary>
public sealed class KindGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ArticleKind k ? ArticleSchema.KindGlyph(k) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
