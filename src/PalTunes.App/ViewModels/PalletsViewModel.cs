using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.App.Services;
using PalTunes.Core.Catalog;
using PalTunes.Core.Models;

namespace PalTunes.App.ViewModels;

public sealed partial class PalletsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public PalletsViewModel(MainViewModel main)
    {
        _main = main;
        Refresh();
        SelectedPallet = Pallets.FirstOrDefault();
    }

    public ObservableCollection<PalletType> Pallets { get; } = [];
    public IReadOnlyList<PalletMaterial> Materials { get; } = Enum.GetValues<PalletMaterial>();
    public IReadOnlyList<PalletConstruction> Constructions { get; } = Enum.GetValues<PalletConstruction>();
    public IReadOnlyList<string> Families => _main.Db.Pallets.Select(p => p.Family).Distinct().OrderBy(f => f).ToList();
    public IReadOnlyList<string> Swatches { get; } = [PalletCatalog.Wood, PalletCatalog.WoodDark, PalletCatalog.PlasticGrey, "#5D6D7E", "#2E86C1", "#3498DB", "#27AE60", PalletCatalog.Cardboard, "#E74C3C", "#9AA5AD"];

    [ObservableProperty] private PalletType? _selectedPallet;
    [ObservableProperty] private PalletType _draft = new();
    [ObservableProperty] private Model3DGroup? _preview;
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private IReadOnlyList<string> _errors = [];

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnSelectedPalletChanged(PalletType? value)
    {
        if (value != null)
        {
            Draft = value.Clone();
            IsNew = false;
            Errors = [];
            RefreshPreview();
        }
    }

    public void Refresh()
    {
        var selected = SelectedPallet?.Id;
        Pallets.Clear();
        foreach (var p in _main.Db.Pallets
                     .Where(p => Filter.Length == 0 || $"{p.Code} {p.Name} {p.Family} {p.MaterialLabel} {p.DimensionsText}".Contains(Filter, StringComparison.CurrentCultureIgnoreCase))
                     .OrderBy(p => FamilyOrder(p.Family)).ThenBy(p => p.Family).ThenBy(p => p.Code))
        {
            Pallets.Add(p);
        }

        if (selected != null)
        {
            SelectedPallet = Pallets.FirstOrDefault(p => p.Id == selected);
        }
    }

    private static int FamilyOrder(string f) => f switch
    {
        "Europe" => 0,
        "Plastique" => 1,
        "Demi / quart" => 2,
        "ISO" => 3,
        "Chimie CP" => 4,
        _ => 5
    };

    [RelayCommand]
    public void RefreshPreview() => Preview = Scene3DBuilder.BuildPallet(Draft).Root;

    [RelayCommand]
    private void PickColor(string hex)
    {
        Draft.Color = hex;
        OnPropertyChanged(nameof(Draft));
        var copy = Draft.Clone();
        Draft = copy;
        RefreshPreview();
    }

    [RelayCommand]
    private void New()
    {
        SelectedPallet = null;
        Draft = new PalletType { Code = "", Name = "Nouvelle palette", Family = "Autre", Length = 1200, Width = 800, Height = 144, Tare = 25, DynamicLoad = 1000 };
        IsNew = true;
        Errors = [];
        RefreshPreview();
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedPallet == null)
        {
            return;
        }

        var copy = SelectedPallet.Clone();
        copy.Id = Guid.NewGuid();
        copy.Code += "-B";
        copy.IsBuiltIn = false;
        SelectedPallet = null;
        Draft = copy;
        IsNew = true;
        RefreshPreview();
    }

    [RelayCommand]
    private void Save()
    {
        var errors = Draft.Validate();
        if (_main.Db.Pallets.Any(p => p.Id != Draft.Id && string.Equals(p.Code, Draft.Code, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Insert(0, $"Le code {Draft.Code} existe déjà.");
        }

        Errors = errors;
        if (errors.Count > 0)
        {
            _main.ShowToast("Palette non enregistrée : " + errors[0], "Warning");
            return;
        }

        var saved = Draft.Clone();
        var index = _main.Db.Pallets.FindIndex(p => p.Id == saved.Id);
        if (index >= 0)
        {
            _main.Db.Pallets[index] = saved;
        }
        else
        {
            _main.Db.Pallets.Add(saved);
        }

        _main.SaveDatabase();
        IsNew = false;
        Refresh();
        SelectedPallet = Pallets.FirstOrDefault(p => p.Id == saved.Id);
        _main.NotifyPalletsChanged();
        _main.ShowToast($"Palette {saved.Code} enregistrée.", "Ok");
    }

    [RelayCommand]
    private void Delete()
    {
        var p = SelectedPallet;
        if (p == null)
        {
            return;
        }

        var used = _main.Db.Packagings.Count(x => x.PalletId == p.Id);
        if (!_main.Dialogs.Confirm(used > 0
                ? $"La palette {p.Code} est utilisée par {used} conditionnement(s). La supprimer quand même ?"
                : $"Supprimer la palette {p.Code} ?" + (p.IsBuiltIn ? " (elle pourra être restaurée par « Restaurer le catalogue »)" : "")))
        {
            return;
        }

        _main.Db.Pallets.Remove(p);
        _main.SaveDatabase();
        SelectedPallet = null;
        Refresh();
        SelectedPallet = Pallets.FirstOrDefault();
        _main.NotifyPalletsChanged();
    }

    [RelayCommand]
    private void RestoreCatalog()
    {
        var added = _main.Db.MergeCatalog();
        _main.SaveDatabase();
        Refresh();
        _main.NotifyPalletsChanged();
        _main.ShowToast(added > 0 ? $"{added} palette(s) du catalogue restaurée(s)." : "Toutes les palettes du catalogue sont présentes.", "Ok");
    }
}
