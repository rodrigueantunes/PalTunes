using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.Core.Models;

namespace PalTunes.App.ViewModels;

/// <summary>Ligne de la liste des clients (avec le nombre d'articles rattachés).</summary>
public sealed record ClientRow(Client Client, int ArticleCount)
{
    public string Name => Client.Name;
    public string Code => Client.Code;
    public string Location => Client.Location;
    public override string ToString() => Client.Name;
}

public sealed record PalletChoice(Guid? Id, string Label);

/// <summary>Base clients : liste, fiche (coordonnées, exigences de conditionnement), articles du client.</summary>
public sealed partial class ClientsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ClientsViewModel(MainViewModel main)
    {
        _main = main;
        Refresh();
        SelectedRow = Rows.FirstOrDefault();
    }

    public ObservableCollection<ClientRow> Rows { get; } = [];
    public ObservableCollection<Article> ClientArticles { get; } = [];
    public ObservableCollection<PalletChoice> PalletChoices { get; } = [];

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private ClientRow? _selectedRow;
    [ObservableProperty] private Client _draft = new();
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private IReadOnlyList<string> _errors = [];
    [ObservableProperty] private PalletChoice? _defaultPallet;
    [ObservableProperty] private string _countText = "";

    partial void OnFilterChanged(string value) => Refresh();

    private bool _quiet;

    partial void OnSelectedRowChanged(ClientRow? value)
    {
        if (!_quiet && value != null)
        {
            Load(value.Client.Clone(), isNew: false);
        }
    }

    partial void OnDefaultPalletChanged(PalletChoice? value) => Draft.DefaultPalletId = value?.Id;

    public void Refresh()
    {
        var selected = SelectedRow?.Client.Id;
        PalletChoices.Clear();
        PalletChoices.Add(new PalletChoice(null, "(standard : EUR 1)"));
        foreach (var p in _main.Db.Pallets.OrderBy(p => p.Code))
        {
            PalletChoices.Add(new PalletChoice(p.Id, $"{p.Code} – {p.Name}"));
        }

        Rows.Clear();
        var query = Filter.Trim();
        foreach (var c in _main.Db.Clients
                     .Where(c => query.Length == 0 || $"{c.Code} {c.Name} {c.City} {c.Country} {c.Contact} {c.Email}".Contains(query, StringComparison.CurrentCultureIgnoreCase))
                     .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Rows.Add(new ClientRow(c, _main.Db.ArticleCountOf(c)));
        }

        CountText = Rows.Count == _main.Db.Clients.Count ? $"{Rows.Count} client(s)" : $"{Rows.Count} / {_main.Db.Clients.Count} client(s)";
        if (selected != null && Rows.FirstOrDefault(r => r.Client.Id == selected) is { } row)
        {
            _quiet = true;
            SelectedRow = row;
            _quiet = false;
            RefreshArticles();
        }
    }

    private void Load(Client c, bool isNew)
    {
        Draft = c;
        IsNew = isNew;
        Errors = [];
        DefaultPallet = PalletChoices.FirstOrDefault(p => p.Id == c.DefaultPalletId) ?? PalletChoices[0];
        RefreshArticles();
    }

    private void RefreshArticles()
    {
        ClientArticles.Clear();
        var name = SelectedRow?.Client.Name;
        if (IsNew || name == null)
        {
            return;
        }

        foreach (var a in _main.Db.Articles
                     .Where(a => string.Equals(a.Client?.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase))
                     .OrderBy(a => a.Code))
        {
            ClientArticles.Add(a);
        }
    }

    [RelayCommand]
    private void New()
    {
        _quiet = true;
        SelectedRow = null;
        _quiet = false;
        Load(new Client(), isNew: true);
    }

    [RelayCommand]
    private void Save()
    {
        var c = Draft.Clone();
        c.Code = (c.Code ?? "").Trim();
        c.Name = (c.Name ?? "").Trim();
        if (c.Code.Length == 0 && c.Name.Length > 0)
        {
            c.Code = Client.CodeFromName(c.Name);
        }

        var errors = c.Validate();
        if (_main.Db.Clients.Any(x => x.Id != c.Id && string.Equals(x.Code, c.Code, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Insert(0, $"Le code {c.Code} existe déjà.");
        }

        if (_main.Db.Clients.Any(x => x.Id != c.Id && string.Equals(x.Name.Trim(), c.Name, StringComparison.CurrentCultureIgnoreCase)))
        {
            errors.Insert(0, $"Le nom « {c.Name} » est déjà utilisé.");
        }

        Errors = errors;
        if (errors.Count > 0)
        {
            _main.ShowToast("Client non enregistré : " + errors[0], "Warning");
            return;
        }

        c.ModifiedAt = DateTime.Now;
        var index = _main.Db.Clients.FindIndex(x => x.Id == c.Id);
        var renamed = 0;
        if (index >= 0)
        {
            var old = _main.Db.Clients[index];
            if (!string.Equals(old.Name, c.Name, StringComparison.Ordinal))
            {
                renamed = _main.Db.RenameClient(old.Name, c.Name);
            }

            _main.Db.Clients[index] = c;
        }
        else
        {
            _main.Db.Clients.Add(c);
        }

        _main.SaveDatabase();
        IsNew = false;
        Refresh();
        SelectedRow = Rows.FirstOrDefault(r => r.Client.Id == c.Id);
        _main.NotifyClientsChanged();
        _main.ShowToast($"Client {c.Name} enregistré" + (renamed > 0 ? $" ({renamed} article(s) mis à jour)." : "."), "Ok");
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedRow?.Client is not { } c)
        {
            return;
        }

        var count = _main.Db.ArticleCountOf(c);
        if (!_main.Dialogs.Confirm(count > 0
                ? $"Le client {c.Name} a {count} article(s). Le supprimer ? Ses articles seront conservés, sans client."
                : $"Supprimer le client {c.Name} ?"))
        {
            return;
        }

        foreach (var a in _main.Db.Articles.Where(a => string.Equals(a.Client?.Trim(), c.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            a.Client = null;
        }

        _main.Db.Clients.Remove(c);
        _main.SaveDatabase();
        Refresh();
        SelectedRow = Rows.FirstOrDefault();
        _main.NotifyClientsChanged();
    }

    [RelayCommand]
    private void OpenArticle(Article? a)
    {
        if (a != null)
        {
            _main.SelectedSection = "Articles";
            _main.Articles.SelectedArticle = a;
            _main.Articles.RebuildTree();
        }
    }

    [RelayCommand]
    private void NewArticle()
    {
        if (SelectedRow?.Client is { } c)
        {
            _main.SelectedSection = "Articles";
            _main.Articles.NewForClient(c.Name);
        }
    }
}
