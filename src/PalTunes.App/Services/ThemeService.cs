using System.Windows;

namespace PalTunes.App.Services;

/// <summary>
/// Mode clair / sombre : la palette (Themes/Palette.Light.xaml ou Palette.Dark.xaml) est chargée avant les styles, qui
/// sont rechargés pour la prendre en compte ; les contrôles Fluent suivent le mode de l'application. La fenêtre est
/// ensuite reconstruite sur le même modèle (aucune saisie perdue).
/// </summary>
public static class ThemeService
{
    public const string Light = "Clair";
    public const string Dark = "Sombre";
    public const string System = "Système";

    private static readonly string[] Dictionaries = ["Themes/Theme.xaml", "Themes/Extra.xaml", "Themes/Logo.xaml"];

    /// <summary>Mode sombre appliqué.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Mode demandé : Clair, Sombre, ou Système (suit le réglage « couleur des applications » de Windows).</summary>
    public static bool Resolve(string? mode) => mode switch
    {
        Dark => true,
        System => !WindowsAppsUseLightTheme(),
        _ => false
    };

    private static bool WindowsAppsUseLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Charge la palette et recharge les styles ; à appeler avant de (re)créer la fenêtre principale.</summary>
    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        IsDark = dark;
#pragma warning disable WPF0001 // ThemeMode (Fluent) : API marquée expérimentale
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var merged = app.Resources.MergedDictionaries;
        // Dictionnaires PalTunes seulement (pas le dictionnaire Fluent de WPF, géré par ThemeMode).
        static bool Ours(ResourceDictionary d) => d.Source?.OriginalString is { } src &&
            (src.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase) || src.Contains("PalTunes;component/Themes/", StringComparison.OrdinalIgnoreCase));
        foreach (var old in merged.Where(Ours).ToList())
        {
            merged.Remove(old);
        }

        merged.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/PalTunes;component/Themes/Palette.{(dark ? "Dark" : "Light")}.xaml") });
        foreach (var path in Dictionaries)
        {
            merged.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/PalTunes;component/{path}") });
        }
    }
}
