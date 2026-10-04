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
        _store = new DatabaseStore(settings.Current.DatabasePath ?? DatabaseStore.DefaultPath);
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
        Cases = new CaseViewModel(this);
        _selectedSection = settings.Current.Section is "Clients" or "Articles" or "Pallets" or "Packagings" or "Cases" ? settings.Current.Section : "Packagings";
        _isReleaseNotesOpen = settings.Current.LastSeenVersion != VersionNumber;
    }

    private Database LoadDatabase(DatabaseStore store)
    {
        try
        {
            return store.Load();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
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
    public CaseViewModel Cases { get; }

    public string DatabasePath => _store.Path;
    public string DatabaseName => Path.GetFileName(_store.Path);

    public string AppVersion { get; } =
        "v" + (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               .Split('+')[0] ?? "0.0.1");

    public string VersionNumber => AppVersion.TrimStart('v');
    public IReadOnlyList<ReleaseNote> ReleaseNotesList => ReleaseNotes.All;

    [ObservableProperty] private string _selectedSection;

    partial void OnSelectedSectionChanged(string value)
    {
        Settings.Current.Section = value;
        Settings.Save();
    }

    public int ClientCount => Db.Clients.Count;
    public int ArticleCount => Db.Articles.Count;
    public int PalletCount => Db.Pallets.Count;
    public int PackagingCount => Db.Packagings.Count;

    // ------------------------------------------------------------------ Persistance

    public void SaveDatabase()
    {
        try
        {
            _store.Save(Db);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.ShowError($"Enregistrement impossible dans {_store.Path} : {ex.Message}");
        }

        OnPropertyChanged(nameof(ClientCount));
        OnPropertyChanged(nameof(ArticleCount));
        OnPropertyChanged(nameof(PalletCount));
        OnPropertyChanged(nameof(PackagingCount));
    }

    public void NotifyArticlesChanged()
    {
        Packagings.RefreshLists();
        Packagings.Refresh();
        Cases?.RefreshArticles();
        Clients?.Refresh();
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
        if (path != null)
        {
            SwitchDatabase(path);
        }
    }

    [RelayCommand]
    private void NewDatabase()
    {
        var path = Dialogs.SaveFile("Créer une base PalTunes (catalogue palettes inclus)", "Base PalTunes (*.json)|*.json", "paltunes-base.json");
        if (path == null)
        {
            return;
        }

        new DatabaseStore(path).Save(Database.CreateDefault(withDemo: false));
        SwitchDatabase(path);
    }

    private void SwitchDatabase(string path)
    {
        var store = new DatabaseStore(path);
        Database db;
        try
        {
            db = store.Load(seedDemoIfMissing: false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Dialogs.ShowError($"Base illisible : {ex.Message}");
            return;
        }

        _store.ChangePath(path);
        Db = db;
        Settings.Current.DatabasePath = path;
        Settings.Save();
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
        ShowToast($"Base « {Path.GetFileName(path)} » ouverte : {db.Articles.Count} articles, {db.Packagings.Count} conditionnements.", "Ok");
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

        var report = ArticleCsv.Import(text, Db.Articles);
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

        var csv = PackagingSpec.ExportCsv(rows, out var skipped);
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
        string.Join(", ", ArticleSchema.Fields(k).Where(f => !f.Required).Select(f => f.Label)))).ToList();

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
        if (IsReleaseNotesOpen)
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
