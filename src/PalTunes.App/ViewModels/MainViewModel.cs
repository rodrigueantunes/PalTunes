using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.App.Services;
using PalTunes.Core.Engine;
using PalTunes.Core.Export;
using PalTunes.Core.Import;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    private readonly DatabaseStore _store;

    public MainViewModel(IDialogService dialogs, SettingsService settings)
    {
        Dialogs = dialogs;
        Settings = settings;
        // Une base pointant vers un fichier réservé (ex. PalTunes.deps.json) ou étranger est écartée au démarrage.
        var path = settings.Current.DatabasePath ?? DatabaseStore.DefaultPath;
        if (DatabaseStore.CheckUsable(path, mustExist: false) is { } problem)
        {
            dialogs.ShowError($"{problem}\n\nLa base par défaut est utilisée : {DatabaseStore.DefaultPath}");
            path = DatabaseStore.DefaultPath;
            settings.Current.DatabasePath = null;
            settings.Save();
        }

        _store = new DatabaseStore(path);
        Db = LoadDatabase(_store);
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        Clients = new ClientsViewModel(this);
        Articles = new ArticlesViewModel(this);
        Pallets = new PalletsViewModel(this);
        Packagings = new PackagingsViewModel(this);
        PackagingLibrary = new PackagingLibraryViewModel(this);
        Cases = new CaseViewModel(this);
        Search = new GlobalSearchViewModel(this);
        ConnectColumnWidths();
        Scene3DBuilder.KindOf = id => Db.FindArticle(id)?.Kind;
        Scene3DBuilder.TopOf = id => Db.FindArticle(id) is { } a && TopStacking.IsDefined(a) ? TopStacking.FormOf(a) : null;
        Print = new PrintCenter(this);
        Print.Attach(Articles, Packagings, PackagingLibrary, Cases);
        _selectedSection = settings.Current.Section is "Clients" or "Articles" or "Pallets" or "CaseTypes" or "Packagings" or "PackagingLibrary" or "Cases"
            ? settings.Current.Section
            : "Packagings";
        if (_selectedSection == "PackagingLibrary")
        {
            PackagingLibrary.Rebuild();
        }
        _isReleaseNotesOpen = settings.Current.LastSeenVersion != VersionNumber;
        // Fiches imprimables de l'article affiché : vérifiées après l'affichage de la fenêtre.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Print.Refresh, System.Windows.Threading.DispatcherPriority.Background);
        if (_selectedSection == "Cases")
        {
            // Rouvert sur le colisage : calcul après l'affichage de la fenêtre.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(Cases.EnsureComputed, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private Database LoadDatabase(DatabaseStore store)
    {
        try
        {
            return store.Load();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            Dialogs.ShowError($"Base illisible ({store.Path}) : {ex.Message}\nUne base vide est utilisée pour cette session.");
            return Database.CreateDefault(withDemo: false);
        }
    }

    public IDialogService Dialogs { get; }
    public SettingsService Settings { get; }
    public Database Db { get; private set; }
    public ClientsViewModel Clients { get; }
    public ArticlesViewModel Articles { get; }
    public PalletsViewModel Pallets { get; }
    public PackagingsViewModel Packagings { get; }

    /// <summary>Espace « Gestion des conditionnements » : tous les conditionnements rangés par client, famille, type.</summary>
    public PackagingLibraryViewModel PackagingLibrary { get; }
    public CaseViewModel Cases { get; }

    /// <summary>Recherche globale (Ctrl+K).</summary>
    public GlobalSearchViewModel Search { get; }

    public string DatabasePath => _store.Path;
    public string DatabaseName => Path.GetFileName(_store.Path);

    public string AppVersion { get; } =
        "v" + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               .Split('+')[0] ?? "0.0.1");

    public string VersionNumber => AppVersion.TrimStart('v');
    public IReadOnlyList<ReleaseNote> ReleaseNotesList => ReleaseNotes.All;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(GoBackCommand), nameof(GoForwardCommand))] private string _selectedSection;

    // ------------------------------------------------------------------ Historique de navigation (Précédent / Suivant)

    private readonly List<string> _back = [];
    private readonly List<string> _forward = [];
    private bool _historyMove;

    partial void OnSelectedSectionChanged(string? oldValue, string newValue)
    {
        if (_historyMove || oldValue == null || oldValue == newValue)
        {
            return;
        }

        _back.Add(oldValue);
        if (_back.Count > 50)
        {
            _back.RemoveAt(0);
        }

        _forward.Clear();
    }

    private bool CanGoBack() => _back.Count > 0;
    private bool CanGoForward() => _forward.Count > 0;

    /// <summary>Alt+← ou bouton « précédent » de la souris : écran précédent.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => MoveInHistory(_back, _forward);

    /// <summary>Alt+→ ou bouton « suivant » de la souris.</summary>
    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => MoveInHistory(_forward, _back);

    private void MoveInHistory(List<string> from, List<string> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        var target = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(SelectedSection);
        _historyMove = true;
        try
        {
            SelectedSection = target;
        }
        finally
        {
            _historyMove = false;
        }

        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    public string BackTip => _back.Count > 0 ? $"Précédent : {SectionLabel(_back[^1])} (Alt+←)" : "Précédent (Alt+←)";
    public string ForwardTip => _forward.Count > 0 ? $"Suivant : {SectionLabel(_forward[^1])} (Alt+→)" : "Suivant (Alt+→)";

    public static string SectionLabel(string section) => section switch
    {
        "Clients" => "Clients",
        "Articles" => "Articles",
        "Pallets" => "Palettes",
        "CaseTypes" => "Caisses",
        "Packagings" => "Conditionnements",
        "Cases" => "Colisage",
        "PackagingLibrary" => "Gestion des conditionnements",
        _ => section
    };

    // ------------------------------------------------------------------ Recherche globale et raccourcis

    [RelayCommand]
    private void OpenSearch() => Search.Open();

    [ObservableProperty] private bool _isShortcutsOpen;

    /// <summary>Colonnes redimensionnables : largeurs mémorisées dans les réglages de ce modèle (appelé par la fenêtre active).</summary>
    public void ConnectColumnWidths()
    {
        Views.Controls.ResizableColumns.LoadWidths = key => Settings.Current.ColumnWidths.TryGetValue(key, out var w) ? w : null;
        Views.Controls.ResizableColumns.SaveWidths = (key, widths) =>
        {
            if (widths == null)
            {
                Settings.Current.ColumnWidths.Remove(key);
            }
            else
            {
                Settings.Current.ColumnWidths[key] = widths;
            }

            Settings.Save();
        };
    }

    // ------------------------------------------------------------------ Mode sombre

    /// <summary>La fenêtre se reconstruit sur ce modèle avec le nouveau thème (rien n'est perdu).</summary>
    public event Action? ThemeChangeRequested;

    public bool IsDarkTheme => ThemeService.IsDark;
    public string ThemeLabel => ThemeService.IsDark ? "Mode clair" : "Mode sombre";
    public string ThemeGlyph => ThemeService.IsDark ? "\uE706" : "\uE708";

    // ------------------------------------------------------------------ Animations

    public sealed record SpeedChoice(double Value, string Label);

    /// <summary>Vitesses de la construction 3D proposées à côté de « Construction » / « Mise en caisse ».</summary>
    public IReadOnlyList<SpeedChoice> BuildSpeeds { get; } =
    [
        new(0.5, "Lente"),
        new(1, "Normale"),
        new(2, "Rapide"),
        new(4, "Très rapide")
    ];

    public double BuildSpeed
    {
        get => Settings.Current.BuildSpeed;
        set
        {
            var v = Math.Clamp(value, 0.25, 4);
            if (Math.Abs(v - Settings.Current.BuildSpeed) < 1e-9)
            {
                return;
            }

            Settings.Current.BuildSpeed = v;
            Views.Controls.Motion.BuildSpeed = v;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public string AnimationsLabel => Settings.Current.Animations ? "Animations : activées" : "Animations : désactivées";

    /// <summary>Active ou coupe les animations (construction 3D, transitions), mémorisé.</summary>
    [RelayCommand]
    private void ToggleAnimations()
    {
        Settings.Current.Animations = !Settings.Current.Animations;
        Views.Controls.Motion.UserEnabled = Settings.Current.Animations;
        Settings.Save();
        OnPropertyChanged(nameof(AnimationsLabel));
        ShowToast(Settings.Current.Animations
            ? System.Windows.SystemParameters.ClientAreaAnimation ? "Animations activées." : "Animations activées dans PalTunes, mais Windows n'affiche pas les animations (Paramètres › Accessibilité › Effets visuels)."
            : "Animations désactivées : tout s'affiche directement.", "Ok");
    }

    public void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(ThemeGlyph));
    }

    /// <summary>Ctrl+Maj+D ou bouton de la navigation : bascule clair / sombre, mémorisé.</summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        Settings.Current.Theme = ThemeService.IsDark ? ThemeService.Light : ThemeService.Dark;
        Settings.Save();
        ThemeChangeRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleShortcuts() => IsShortcutsOpen = !IsShortcutsOpen;

    public sealed record Shortcut(string Keys, string Action);

    public IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new("Ctrl + K", "Rechercher partout : articles, conditionnements, colisages, clients, palettes, caisses, espaces"),
        new("Alt + ← / Alt + →", "Écran précédent / suivant (aussi : boutons latéraux de la souris)"),
        new("Ctrl + 1 … 7", "Clients · Articles · Palettes · Caisses · Conditionnements · Colisage · Gestion des conditionnements"),
        new("Ctrl + S", "Enregistrer la fiche de l'écran affiché"),
        new("F5", "Calculer le conditionnement"),
        new("Ctrl + P", "Imprimer la fiche palette"),
        new("Ctrl + E", "Exporter les conditionnements (mono-article)"),
        new("Ctrl + I", "Importer des articles (CSV)"),
        new("F1", "Format du fichier d'import des articles"),
        new("Ctrl + F1", "Cette liste des raccourcis"),
        new("Ctrl + Maj + D", "Mode sombre / mode clair"),
        new("Clic dans la vue 3D", "Termine aussitôt l'animation de construction ; « Construction » / « Mise en caisse » la rejoue"),
        new("Séparateurs", "Glisser entre deux colonnes pour élargir ou réduire (double-clic : largeur d'origine)"),
        new("Échap", "Fermer la fenêtre ouverte (recherche, aide, rapport, notification)"),
        new("Double-clic", "Ouvrir l'élément (arborescences, gestion des conditionnements)")
    ];

    partial void OnSelectedSectionChanged(string value)
    {
        Settings.Current.Section = value;
        Settings.Save();
        if (value == "Articles")
        {
            Articles.RefreshLinks();
        }
        OnPropertyChanged(nameof(BackTip));
        OnPropertyChanged(nameof(ForwardTip));
        if (value == "Cases")
        {
            Cases.EnsureComputed();
        }

        if (value == "PackagingLibrary")
        {
            PackagingLibrary.Rebuild();
        }

        Print.Refresh();
    }

    // ------------------------------------------------------------------ Navigation entre les espaces

    /// <summary>Espace Colisage réglé sur le colisage d'un article caisse (produit, caisse, quantité par caisse), calculé.</summary>
    public void OpenColisage(Article box) => Cases.OpenColisage(box);

    /// <summary>Fiche d'un article (espace Articles).</summary>
    public void ShowArticle(Article a)
    {
        SelectedSection = "Articles";
        Articles.SelectedArticle = Db.FindArticle(a.Id) ?? a;
        Articles.RebuildTree();
        Articles.RefreshLinks();
    }

    /// <summary>Gestion des conditionnements filtrée sur un code (article, caisse, produit contenu).</summary>
    /// <summary>Conditionnement sélectionné dans la gestion des conditionnements.</summary>
    public void ShowPackaging(Packaging p)
    {
        SelectedSection = "PackagingLibrary";
        PackagingLibrary.SearchText = "";
        PackagingLibrary.Selected = Db.Packagings.FirstOrDefault(x => x.Id == p.Id) ?? p;
    }

    public void ShowClient(Client c)
    {
        SelectedSection = "Clients";
        Clients.Filter = "";
        Clients.SelectedRow = Clients.Rows.FirstOrDefault(r => r.Client.Id == c.Id) ?? Clients.SelectedRow;
    }

    public void ShowPallet(PalletType p)
    {
        SelectedSection = "Pallets";
        Pallets.SelectedPallet = Db.Pallets.FirstOrDefault(x => x.Id == p.Id) ?? p;
    }

    public void ShowCaseType(CaseType c)
    {
        SelectedSection = "CaseTypes";
        Cases.CatalogSelected = Cases.CatalogCases.FirstOrDefault(x => x.Id == c.Id) ?? c;
    }

    public void ShowPackagingsFor(string search)
    {
        SelectedSection = "PackagingLibrary";
        PackagingLibrary.SearchText = search;
    }

    /// <summary>Caisse créée au colisage dont le produit est connu : son colisage peut être rouvert.</summary>
    public bool HasColisage(Article? a) => CaseEngine.CanRebuild(a, id => Db.FindArticle(id));

    private readonly Dictionary<(Guid, CaseContent?, int?, DateTime, int), CaseEngine.CaseSheet?> _sheets = [];

    /// <summary>Colisage recalculé d'un article caisse (mis en cache tant que l'article n'est pas modifié).</summary>
    public CaseEngine.CaseSheet? ColisageSheet(Article box)
    {
        if (!HasColisage(box))
        {
            return null;
        }

        var key = (box.Id, box.CaseContent, box.QuantityPerCase, box.ModifiedAt, Db.Cases.Count);
        if (!_sheets.TryGetValue(key, out var sheet))
        {
            sheet = CaseEngine.Rebuild(box, id => Db.FindArticle(id), Db.Cases);
            _sheets[key] = sheet;
        }

        return sheet;
    }

    // ------------------------------------------------------------------ Impression

    /// <summary>Fiches imprimées de l'article affiché (palette, colisage, conditionnement), quel que soit l'espace.</summary>
    public PrintCenter Print { get; }

    /// <summary>Couleur d'un produit seul sur une fiche : distincte par défaut, ou celle de la fiche article (« Couleur d'origine »).</summary>
    public IReadOnlyDictionary<Guid, System.Windows.Media.Color> ColorsFor(Article a) => new Dictionary<Guid, System.Windows.Media.Color>
    {
        [a.Id] = Settings.Current.UseArticleColors ? ArticleColors.Parse(a.Color, ArticleColors.DistinctByIndex(0)) : ArticleColors.DistinctByIndex(0)
    };

    /// <summary>Couleurs distinctes des articles d'un colisage (caisse mixte : une par article).</summary>
    public IReadOnlyDictionary<Guid, System.Windows.Media.Color> ColorsFor(CaseEngine.CaseSheet sheet)
    {
        if (!sheet.IsMixed)
        {
            return ColorsFor(sheet.Content);
        }

        var colors = new Dictionary<Guid, System.Windows.Media.Color>();
        var k = 0;
        foreach (var id in sheet.ShownUnit.Items.GroupBy(p => p.ArticleId).OrderByDescending(g => g.Count()).Select(g => g.Key))
        {
            colors[id] = Settings.Current.UseArticleColors
                ? ArticleColors.Parse(Db.FindArticle(id)?.Color, ArticleColors.DistinctByIndex(k++))
                : ArticleColors.DistinctByIndex(k++);
        }

        return colors;
    }

    public int ClientCount => Db.Clients.Count;
    public int ArticleCount => Db.Articles.Count;
    public int PalletCount => Db.Pallets.Count;
    public int CaseCount => Db.Cases.Count;
    public int PackagingCount => Db.Packagings.Count;

    // ------------------------------------------------------------------ Persistance

    public void SaveDatabase()
    {
        try
        {
            _store.Save(Db);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Dialogs.ShowError($"Enregistrement impossible dans {_store.Path} : {ex.Message}");
        }

        OnPropertyChanged(nameof(ClientCount));
        OnPropertyChanged(nameof(ArticleCount));
        OnPropertyChanged(nameof(PalletCount));
        OnPropertyChanged(nameof(CaseCount));
        OnPropertyChanged(nameof(PackagingCount));
    }

    /// <summary>Conditionnements créés, modifiés ou supprimés hors de l'écran de création.</summary>
    public void NotifyPackagingsChanged() => OnPropertyChanged(nameof(PackagingCount));

    public void NotifyArticlesChanged()
    {
        Packagings.RefreshLists();
        Packagings.Refresh();
        Cases?.RefreshArticles();
        Clients?.Refresh();
        Articles?.RefreshLinks();
        OnPropertyChanged(nameof(ArticleCount));
    }

    /// <summary>Clients créés, renommés ou supprimés : arborescence et listes suivent.</summary>
    public void NotifyClientsChanged()
    {
        Articles.RebuildTree();
        if (Articles.SelectedArticle is { } selected)
        {
            Articles.Editor.Load(selected, isNew: false);
        }

        Clients.Refresh();
        Packagings.Picker.Refresh();
        Cases?.Picker.Refresh();
        OnPropertyChanged(nameof(ClientCount));
    }

    public void NotifyPalletsChanged()
    {
        Packagings.RefreshLists();
        Cases.RefreshPallets();
        OnPropertyChanged(nameof(PalletCount));
    }

    [RelayCommand]
    private void OpenDatabase()
    {
        var path = Dialogs.OpenFile("Ouvrir une base PalTunes", "Base PalTunes (*.json)|*.json|Tous les fichiers (*.*)|*.*");
        if (path == null)
        {
            return;
        }

        if (DatabaseStore.CheckUsable(path, mustExist: true) is { } problem)
        {
            Dialogs.ShowError(problem);
            return;
        }

        SwitchDatabase(path);
    }

    [RelayCommand]
    private void NewDatabase()
    {
        var path = Dialogs.SaveFile("Créer une base PalTunes (catalogues palettes et caisses inclus)", "Base PalTunes (*.json)|*.json", "base-paltunes.json");
        if (path == null)
        {
            return;
        }

        if (DatabaseStore.CheckUsable(path, mustExist: false) is { } problem)
        {
            Dialogs.ShowError(problem);
            return;
        }

        try
        {
            new DatabaseStore(path).Save(Database.CreateDefault(withDemo: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Dialogs.ShowError($"Création impossible : {ex.Message}");
            return;
        }

        SwitchDatabase(path);
    }

    /// <summary>
    /// Remise à zéro de la base courante : sauvegarde horodatée, puis base vide avec les catalogues par défaut
    /// (palettes, caisses). Le fichier reste le même, aucun nom à choisir.
    /// </summary>
    [RelayCommand]
    private void ResetDatabase()
    {
        if (!Dialogs.Confirm($"Réinitialiser la base « {DatabaseName} » ?\n\nClients, articles, conditionnements et palettes / caisses ajoutées seront supprimés ; " +
                             "les catalogues de palettes et de caisses par défaut sont remis.\nUne copie de sauvegarde est faite avant, dans le même dossier."))
        {
            return;
        }

        string? backup;
        try
        {
            backup = _store.Backup();
            Db = Database.CreateDefault(withDemo: false);
            _store.Save(Db);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Dialogs.ShowError($"Réinitialisation impossible : {ex.Message}");
            return;
        }

        ReloadAll();
        ShowToast($"Base réinitialisée" + (backup != null ? $" ; sauvegarde : {Path.GetFileName(backup)}." : "."), "Ok");
    }

    private void SwitchDatabase(string path)
    {
        var store = new DatabaseStore(path);
        Database db;
        try
        {
            db = store.Load(seedDemoIfMissing: false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            Dialogs.ShowError($"Base illisible : {ex.Message}");
            return;
        }

        _store.ChangePath(path);
        Db = db;
        Settings.Current.DatabasePath = path;
        Settings.Save();
        ReloadAll();
        ShowToast($"Base « {Path.GetFileName(path)} » ouverte : {db.Articles.Count} articles, {db.Packagings.Count} conditionnements.", "Ok");
    }

    /// <summary>Toutes les vues suivent la base courante (ouverture, création, remise à zéro).</summary>
    private void ReloadAll()
    {
        Articles.SelectedArticle = null;
        Articles.Editor.Load(new Article(), isNew: true);
        Articles.RebuildTree();
        Clients.Refresh();
        Pallets.Refresh();
        Packagings.RefreshLists();
        Packagings.SelectedPackaging = null;
        Packagings.Refresh();
        Cases.Reload();
        OnPropertyChanged(nameof(DatabasePath));
        OnPropertyChanged(nameof(DatabaseName));
        OnPropertyChanged(nameof(ArticleCount));
        OnPropertyChanged(nameof(PalletCount));
        OnPropertyChanged(nameof(PackagingCount));
        OnPropertyChanged(nameof(ClientCount));
    }

    [RelayCommand]
    private void ShowDatabaseFolder()
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(_store.Path));
        if (folder != null && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_store.Path}\"") { UseShellExecute = true });
        }
    }

    // ------------------------------------------------------------------ Import / export

    [ObservableProperty] private ImportReport? _importReport;

    [RelayCommand]
    private void ImportArticles()
    {
        var path = Dialogs.OpenFile("Importer des articles (CSV)", "Fichiers CSV / texte (*.csv;*.txt)|*.csv;*.txt|Tous les fichiers (*.*)|*.*");
        if (path == null)
        {
            return;
        }

        string text;
        try
        {
            text = ReadText(path);
        }
        catch (IOException ex)
        {
            Dialogs.ShowError($"Lecture impossible : {ex.Message}");
            return;
        }

        var report = ArticleCsv.Import(text, Db.Articles, v => Db.FindClient(v)?.Code ?? v);
        if (report.Created + report.Updated > 0)
        {
            var newClients = Db.MergeClients();
            SaveDatabase();
            Articles.RebuildTree();
            NotifyArticlesChanged();
            NotifyClientsChanged();
            if (newClients > 0)
            {
                report.Lines.Add(new ImportLine { Status = "Info", Message = $"{newClients} client(s) créé(s) dans la base clients à partir de la colonne CLIENT." });
            }
        }

        ImportReport = report;
        ShowToast($"Import {Path.GetFileName(path)} : {report.Summary}.", report.Errors > 0 ? "Warning" : "Ok");
    }

    /// <summary>UTF-8 si valide, sinon Windows-1252 (fichiers Excel « CSV (séparateur : point-virgule) »).</summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    [RelayCommand]
    private void CloseImportReport() => ImportReport = null;

    [RelayCommand]
    private void ImportClients()
    {
        var path = Dialogs.OpenFile("Importer des clients (CSV)", "Fichiers CSV / texte (*.csv;*.txt)|*.csv;*.txt|Tous les fichiers (*.*)|*.*");
        if (path == null)
        {
            return;
        }

        string text;
        try
        {
            text = ReadText(path);
        }
        catch (IOException ex)
        {
            Dialogs.ShowError($"Lecture impossible : {ex.Message}");
            return;
        }

        var report = ClientCsv.Import(text, Db);
        if (report.Created + report.Updated > 0)
        {
            SaveDatabase();
            NotifyClientsChanged();
        }

        ImportReport = report;
        ShowToast($"Import clients {Path.GetFileName(path)} : {report.Summary}.", report.Errors > 0 ? "Warning" : "Ok");
    }

    [RelayCommand]
    private void ExportClients() =>
        WriteCsv("Exporter la base clients", "clients_paltunes.csv", ClientCsv.Export(Db), $"{Db.Clients.Count} client(s) exporté(s)");

    [RelayCommand]
    private void ExportArticles() =>
        WriteCsv("Exporter la base articles", "articles_paltunes.csv", ArticleCsv.Export(Db.Articles.OrderBy(a => a.Code)), $"{Db.Articles.Count} article(s) exporté(s)");

    [RelayCommand]
    private void ExportTemplate() =>
        WriteCsv("Exporter le modèle CSV des articles", "modele_articles_paltunes.csv", ArticleCsv.Template(), "Modèle exporté (une ligne d'exemple par type)");

    /// <summary>Export des conditionnements mono-article (étude §12). Sans solution enregistrée : la recommandée est calculée.</summary>
    [RelayCommand]
    private void ExportPackagings()
    {
        var homogeneous = Db.Packagings.Where(p => p.Kind == PackagingKind.Homogene).OrderBy(p => p.Code).ToList();
        if (homogeneous.Count == 0)
        {
            ShowToast("Aucun conditionnement mono-article à exporter.", "Warning");
            return;
        }

        var computed = 0;
        var rows = new List<(Packaging, Article?)>();
        foreach (var p in homogeneous)
        {
            var copy = p;
            if (p.Solution == null)
            {
                copy = p.Clone();
                copy.Solution = PackagingCalculator.Compute(p, Db).Recommended;
                computed++;
            }

            rows.Add((copy, Db.FindArticle(p.ArticleId)));
        }

        var csv = PackagingSpec.ExportCsv(rows, out var skipped, null, Db.ClientLabel);
        var exported = rows.Count - skipped;
        WriteCsv("Exporter les conditionnements mono-article", "conditionnements_paltunes.csv", csv,
            $"{exported} conditionnement(s) mono-article exporté(s)" + (computed > 0 ? $", dont {computed} sans solution enregistrée (recommandée calculée)" : "") +
            (Db.Packagings.Count > homogeneous.Count ? " · les hétérogènes ne sont pas exportés (v0.0.1)" : ""));
    }

    private void WriteCsv(string title, string defaultName, string content, string done)
    {
        var path = Dialogs.SaveFile(title, "Fichier CSV (*.csv)|*.csv", defaultName);
        if (path == null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, content, new UTF8Encoding(true));
            ShowToast($"{done} : {Path.GetFileName(path)}.", "Ok");
        }
        catch (IOException ex)
        {
            Dialogs.ShowError($"Écriture impossible : {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ Aide, nouveautés, notifications

    public IReadOnlyList<ColumnDoc> ImportColumns => ArticleCsv.Columns;

    public IReadOnlyList<KindDoc> KindDocs { get; } = ArticleSchema.Kinds.Select(k => new KindDoc(
        ArticleSchema.KindLabel(k), ArticleSchema.KindDescription(k),
        string.Join(", ", ArticleSchema.Fields(k).Where(f => f.Required).Select(f => f.Label)) + ", Poids (kg)",
        string.Join(", ", ArticleSchema.Fields(k).Where(f => !f.Required).Select(f => f.Label)
            .Concat(k == ArticleKind.Caisse ? ["Quantité par caisse"] : [])))).ToList();

    [ObservableProperty] private bool _isFormatHelpOpen;
    [ObservableProperty] private bool _isReleaseNotesOpen;
    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private string _toastKind = "Info";

    [RelayCommand]
    private void ShowFormatHelp() => IsFormatHelpOpen = true;

    [RelayCommand]
    private void ShowReleaseNotes() => IsReleaseNotesOpen = true;

    [RelayCommand]
    private void CloseReleaseNotes()
    {
        IsReleaseNotesOpen = false;
        Settings.Current.LastSeenVersion = VersionNumber;
        Settings.Save();
    }

    [RelayCommand]
    private void Escape()
    {
        if (Search.IsOpen)
        {
            Search.IsOpen = false;
        }
        else if (IsShortcutsOpen)
        {
            IsShortcutsOpen = false;
        }
        else if (IsReleaseNotesOpen)
        {
            CloseReleaseNotes();
        }
        else if (IsFormatHelpOpen)
        {
            IsFormatHelpOpen = false;
        }
        else if (ImportReport != null)
        {
            ImportReport = null;
        }
        else
        {
            IsToastVisible = false;
        }
    }

    [RelayCommand]
    private void CloseToast() => IsToastVisible = false;

    [RelayCommand]
    private void Navigate(string section) => SelectedSection = section;

    /// <summary>Ctrl+S : enregistre la fiche de l'espace affiché.</summary>
    [RelayCommand]
    private void SaveCurrent()
    {
        switch (SelectedSection)
        {
            case "Articles":
                Articles.SaveCommand.Execute(null);
                break;
            case "Pallets":
                Pallets.SaveCommand.Execute(null);
                break;
            case "CaseTypes":
                Cases.CatalogSaveCommand.Execute(null);
                break;
            case "Clients":
                Clients.SaveCommand.Execute(null);
                break;
            default:
                Packagings.SaveCommand.Execute(null);
                break;
        }
    }

    public void ShowToast(string text, string kind = "Info")
    {
        ToastText = text;
        ToastKind = kind;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}

public sealed record KindDoc(string Label, string Description, string Required, string Optional);
