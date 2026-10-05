using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PalTunes.App.Services;

public interface IDialogService
{
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string defaultName);
    void ShowError(string message);
    bool Confirm(string message);
}

public sealed class DialogService : IDialogService
{
    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFile(string title, string filter, string defaultName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowError(string message) =>
        MessageBox.Show(message, "PalTunes", MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message) =>
        MessageBox.Show(message, "PalTunes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

public sealed class AppSettings
{
    /// <summary>Dernière version dont les nouveautés ont été affichées.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>Fichier de la base (null = %APPDATA%\PalTunes\paltunes-base.json).</summary>
    public string? DatabasePath { get; set; }

    public string TreeGrouping { get; set; } = "Client";

    /// <summary>Rangement de l'espace « Gestion des conditionnements ».</summary>
    public string PackagingTreeGrouping { get; set; } = "Client";
    public string Section { get; set; } = "Packagings";

    /// <summary>Vues des conditionnements : couleurs des fiches articles (sinon couleurs bien distinctes, par défaut).</summary>
    public bool UseArticleColors { get; set; }
}

/// <summary>Préférences locales (%APPDATA%\PalTunes\settings.json).</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PalTunes", "settings.json")
            : path;
        Current = Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Préférences non critiques.
        }
    }

    private AppSettings Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings() : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }
}

/// <summary>Couleurs d'affichage des articles : couleur saisie, sinon palette du thème selon l'ordre d'apparition.</summary>
public static class ArticleColors
{
    private static readonly string[] Palette =
    [
        "#5DADE2", "#F39C12", "#2ECC71", "#AF7AC5", "#E74C3C", "#48C9B0", "#F5B041", "#5D6D7E",
        "#EC7063", "#58D68D", "#85C1E9", "#DC7633", "#45B39D", "#BB8FCE", "#F7DC6F", "#7FB3D5"
    ];

    public static Color Parse(string? hex, Color fallback)
    {
        try
        {
            if (hex is { Length: > 0 } && ColorConverter.ConvertFromString(hex) is Color c)
            {
                return c;
            }
        }
        catch (FormatException)
        {
        }

        return fallback;
    }

    public static Color ByIndex(int index) => (Color)ColorConverter.ConvertFromString(Palette[Math.Abs(index) % Palette.Length]);

    /// <summary>Couleurs très contrastées (articles d'un même conditionnement bien dépareillés), dans l'ordre des lignes.</summary>
    private static readonly string[] Distinct =
    [
        "#2E86DE", "#F39C12", "#27AE60", "#E74C3C", "#8E44AD", "#F1C40F", "#16A085", "#D35400",
        "#C0392B", "#2C3E50", "#E84393", "#7F8C8D", "#00B894", "#6C5CE7", "#A0522D", "#00CEC9"
    ];

    public static Color DistinctByIndex(int index) => (Color)ColorConverter.ConvertFromString(Distinct[Math.Abs(index) % Distinct.Length]);

    public static IReadOnlyList<string> Swatches => Palette;
}
