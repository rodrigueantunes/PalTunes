using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.App.Services;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.App.ViewModels;

/// <summary>Mode de mise en caisse : meilleure caisse du catalogue, caisse choisie, ou caisse spécifique saisie.</summary>
public enum CaseMode
{
    Best,
    Catalog,
    Custom
}

/// <summary>
/// Colisage : produits identiques dans une caisse. Catalogue de caisses (à l'image des palettes), seules les caisses
/// possibles pour l'article sont proposées, meilleure composition recommandée, caisse ouvrable en 3D.
/// </summary>
public sealed partial class CaseViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly MainViewModel _main;
    private bool _loading;

    public CaseViewModel(MainViewModel main)
    {
        _main = main;
        Picker = new ArticlePicker(() => _main.Db, () => [Article]);
        _axisChoice = AxisChoices[0];
        RefreshPallets();
        RefreshCatalog();
        RefreshArticles();
        _loading = true;
        Article = Articles.FirstOrDefault();
        Picker.Refresh();
        _loading = false;
        // Calcul différé à la première ouverture de l'espace Colisage : le démarrage reste immédiat quelle que soit la base.
    }

    private bool _computed;

    /// <summary>Premier affichage de l'espace Colisage : caisses possibles et solutions de l'article choisi.</summary>
    public void EnsureComputed()
    {
        if (!_computed)
        {
            RefreshPossibleCases();
            Compute();
        }
    }

    // ------------------------------------------------------------------ Mise en caisse

    public ObservableCollection<Article> Articles { get; } = [];

    /// <summary>Choix du client puis du produit à mettre en caisse.</summary>
    public ArticlePicker Picker { get; }

    public ObservableCollection<CaseType> PossibleCases { get; } = [];

    /// <summary>Caisses proposées au choix : les seules possibles, ou tout le catalogue si la caisse est forcée.</summary>
    public ObservableCollection<CaseType> CaseChoices { get; } = [];

    public ObservableCollection<PalletType> Pallets { get; } = [];
    public ObservableCollection<SolutionViewModel> Solutions { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ArticleWarning), nameof(ShowFixArticle), nameof(ShowAxis))] private Article? _article;

    /// <summary>Axe des tubes, bagues et bobines dans la caisse (comme en palettisation).</summary>
    public IReadOnlyList<AxisChoice> AxisChoices { get; } =
    [
        new(CoilAxis.Indifferent, "Le meilleur (axe horizontal ou vertical)"),
        new(CoilAxis.Vertical, "Forcer l'axe vertical (debout)"),
        new(CoilAxis.Horizontal, "Forcer l'axe horizontal (couché)")
    ];

    [ObservableProperty] private AxisChoice? _axisChoice;

    private CoilAxis? Axis => ShowAxis ? AxisChoice?.Value ?? CoilAxis.Indifferent : null;

    public bool ShowAxis => Article is { Kind: ArticleKind.Tube or ArticleKind.Bobine };

    partial void OnAxisChoiceChanged(AxisChoice? value)
    {
        if (!_loading && _computed)
        {
            RefreshPossibleCases();
            Compute();
        }
    }

    /// <summary>Poids unitaire impossible pour les dimensions (erreur de saisie ou d'unité).</summary>
    public string? ArticleWarning => Article == null ? null : ArticleSchema.Warnings(Article);

    /// <summary>Bouton « Corriger la fiche article » : poids suspect ou aucune solution.</summary>
    public bool ShowFixArticle => Article != null && (ArticleWarning != null || (_computed && !IsBusy && Solutions.Count == 0));

    [RelayCommand]
    private void OpenArticle()
    {
        if (Article is { } a)
        {
            _main.SelectedSection = "Articles";
            _main.Articles.SelectedArticle = a;
            _main.Articles.RebuildTree();
        }
    }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCatalogMode), nameof(IsCustomMode), nameof(IsPalletChoiceEnabled))] private CaseMode _mode = CaseMode.Best;
    [ObservableProperty] private CaseType? _selectedCase;
    [ObservableProperty] private CaseSpec _spec = new() { InnerLength = 600, InnerWidth = 400, InnerHeight = 300, WallThickness = 5, Tare = 0.6 };
    [ObservableProperty] private double _gap;

    /// <summary>Poids brut maxi d'une caisse manutentionnée à la main (0 = ignoré) pour « La meilleure ».</summary>
    [ObservableProperty] private double _manualLimit = CaseEngine.DefaultManualHandlingLimit;
    [ObservableProperty] private int? _targetQuantity;
    [ObservableProperty] private SolutionViewModel? _selectedSolution;
    [ObservableProperty] private Model3DGroup? _scene3D;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private string _possibleText = "";

    /// <summary>Palette de destination : la meilleure caisse est celle qui donne le plus de produits par palette.</summary>
    [ObservableProperty] private PalletType? _destinationPallet;

    /// <summary>Hauteur totale maxi de la palette de destination (palette comprise).</summary>
    [ObservableProperty] private double _palletMaxHeight = 1800;

    /// <summary>Débord palette autorisé (mm par côté) pour palettiser les caisses : 0 par défaut.</summary>
    [ObservableProperty] private double _palletOverhangLength;

    [ObservableProperty] private double _palletOverhangWidth;

    /// <summary>Forcer la caisse : tout le catalogue est proposé, la caisse choisie est imposée (décoché par défaut).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPalletChoiceEnabled))] private bool _forceCase;

    /// <summary>Caisse forcée : la palette de destination n'a plus d'importance (choix désactivé, pas de palettisation).</summary>
    public bool IsPalletChoiceEnabled => !(Mode == CaseMode.Catalog && ForceCase);

    private PalletType? ActivePallet => IsPalletChoiceEnabled ? DestinationPallet : null;

    /// <summary>Caisse ouverte : contenu visible en 3D, plan intérieur en 2D. Fermée : vue extérieure.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsClosed), nameof(OpenButtonText))] private bool _isOpen = true;

    public bool IsClosed => !IsOpen;
    public string OpenButtonText => IsOpen ? "Fermer la caisse" : "Ouvrir la caisse";
    public bool IsCatalogMode => Mode == CaseMode.Catalog;
    public bool IsCustomMode => Mode == CaseMode.Custom;

    /// <summary>Caisse de la solution affichée (null en mode spécifique).</summary>
    public CaseType? CurrentCase => SelectedSolution?.Solution.Base.IsCase == true ? _main.Db.FindCase(SelectedSolution.Solution.Base.PalletId) : null;

    /// <summary>Caractéristiques de la caisse de la solution affichée.</summary>
    public CaseSpec CurrentSpec => CurrentCase?.ToSpec(Gap) ?? WithGap(Spec);

    public Solution? CurrentSolution => SelectedSolution?.Solution;
    public LoadUnit? CurrentUnit => CurrentSolution?.FirstUnit;
    public PackagingConstraints Constraints => CaseEngine.CaseConstraints(CurrentSpec);
    public string CurrentCaseColor => CurrentCase?.Color ?? "#C9A26B";
    public IReadOnlyDictionary<Guid, Color> ColorMap => _main.Packagings.ColorMap;

    private CaseSpec WithGap(CaseSpec s)
    {
        s.Gap = Gap;
        return s;
    }

    public string CaseInfo
    {
        get
        {
            var c = IsCatalogMode ? SelectedCase : CurrentCase;
            return c == null
                ? ""
                : $"{c.MaterialLabel} · intérieur {c.InnerText} mm · extérieur {c.OuterText} mm · paroi {c.WallThickness:0.#} mm · " +
                  $"tare {c.Tare.ToString("0.###", Fr)} kg · charge maxi {(c.MaxWeight > 0 ? c.MaxWeight.ToString("0.#", Fr) + " kg" : "non limitée")}";
        }
    }

    public string Summary
    {
        get
        {
            if (CurrentSolution is not { } s || CurrentUnit is not { } u)
            {
                return "";
            }

            var spec = CurrentSpec;
            var weight = u.Items.Sum(p => p.Weight) + spec.Tare;
            var name = CurrentCase is { } c ? $"{c.Code} – {c.Name} : " : "Caisse spécifique : ";
            var fill = CurrentCase != null ? CaseEngine.InnerFill(s, _main.Db.Cases) : u.Metrics.FillRate;
            var pallet = s.DestinationPallet == null
                ? ""
                : s.CasesPerPallet > 0
                    ? $" · palette {s.DestinationPallet} : {s.CasesPerPallet} caisses = {s.ItemsPerPallet} produits par palette"
                    : $" · non palettisable sur {s.DestinationPallet}";
            return $"{name}{s.ItemsPerUnit} produits ({s.ItemsPerLayer} par couche × {s.LayerCount}) · volume intérieur rempli à {fill.ToString("0", Fr)} % · " +
                   $"caisse extérieure {spec.OuterLength:0} × {spec.OuterWidth:0} × {spec.OuterHeight:0} mm · {weight.ToString(Formats.TotalWeight, Fr)} kg{pallet}";
        }
    }

    public void RefreshArticles()
    {
        var current = Article?.Id;
        _loading = true;
        Articles.Clear();
        foreach (var a in _main.Db.Articles.OrderBy(a => a.Code, StringComparer.CurrentCultureIgnoreCase))
        {
            Articles.Add(a);
        }

        var article = Articles.FirstOrDefault(a => a.Id == current) ?? (Article != null && Articles.Contains(Article) ? Article : null);
        Picker.Sync(article);
        Article = article;
        Picker.Refresh();
        _loading = false;
    }

    partial void OnArticleChanged(Article? value)
    {
        if (_loading)
        {
            return;
        }

        RefreshPossibleCases();
        Compute();
    }

    partial void OnModeChanged(CaseMode value)
    {
        OnPropertyChanged(nameof(CaseInfo));
        if (!_loading)
        {
            Compute();
        }
    }

    partial void OnSelectedCaseChanged(CaseType? value)
    {
        OnPropertyChanged(nameof(CaseInfo));
        if (!_loading && Mode == CaseMode.Catalog)
        {
            Compute();
        }
    }

    partial void OnIsOpenChanged(bool value) => RebuildScene();

    partial void OnDestinationPalletChanged(PalletType? value)
    {
        if (!_loading)
        {
            Compute();
        }
    }

    partial void OnPalletMaxHeightChanged(double value)
    {
        if (!_loading)
        {
            Compute();
        }
    }

    partial void OnForceCaseChanged(bool value)
    {
        RefreshCaseChoices();
        if (!_loading && Mode == CaseMode.Catalog)
        {
            Compute();
        }
    }

    /// <summary>Palettes de destination ; EUR 1 (1200 × 800) par défaut.</summary>
    public void RefreshPallets()
    {
        var current = DestinationPallet?.Id;
        _loading = true;
        Pallets.Clear();
        foreach (var p in _main.Db.Pallets.OrderBy(p => p.Family).ThenBy(p => p.Code))
        {
            Pallets.Add(p);
        }

        DestinationPallet = Pallets.FirstOrDefault(p => p.Id == current) ?? Pallets.FirstOrDefault(p => p.Code == "EUR1") ?? Pallets.FirstOrDefault();
        _loading = false;
    }

    private PackagingConstraints PalletConstraints => new()
    {
        MaxTotalHeight = PalletMaxHeight > 0 ? PalletMaxHeight : 1800,
        OverhangLength = PalletOverhangLength,
        OverhangWidth = PalletOverhangWidth
    };

    partial void OnPalletOverhangLengthChanged(double value)
    {
        if (!_loading)
        {
            Compute();
        }
    }

    partial void OnPalletOverhangWidthChanged(double value)
    {
        if (!_loading)
        {
            Compute();
        }
    }

    private void RefreshCaseChoices()
    {
        var current = SelectedCase?.Id;
        _loading = true;
        CaseChoices.Clear();
        var source = ForceCase ? _main.Db.Cases.OrderBy(c => c.Family).ThenBy(c => c.InnerLength * c.InnerWidth * c.InnerHeight) : PossibleCases.AsEnumerable();
        foreach (var c in source)
        {
            CaseChoices.Add(c);
        }

        SelectedCase = CaseChoices.FirstOrDefault(c => c.Id == current) ?? CaseChoices.FirstOrDefault();
        _loading = false;
        OnPropertyChanged(nameof(CaseInfo));
    }

    /// <summary>Seules les caisses du catalogue capables de contenir l'article sont proposées.</summary>
    private void RefreshPossibleCases()
    {
        var current = SelectedCase?.Id;
        _loading = true;
        PossibleCases.Clear();
        if (Article != null)
        {
            foreach (var c in CaseEngine.PossibleCases(Article, _main.Db.Cases, Gap, Axis).OrderBy(c => c.InnerLength * c.InnerWidth * c.InnerHeight))
            {
                PossibleCases.Add(c);
            }
        }

        SelectedCase = PossibleCases.FirstOrDefault(c => c.Id == current) ?? PossibleCases.FirstOrDefault();
        _loading = false;
        PossibleText = Article == null
            ? ""
            : $"{PossibleCases.Count} caisse(s) possible(s) sur {_main.Db.Cases.Count} pour {Article.Code}";
        RefreshCaseChoices();
    }

    partial void OnSelectedSolutionChanged(SolutionViewModel? value)
    {
        OnPropertyChanged(nameof(CurrentSolution));
        OnPropertyChanged(nameof(CurrentUnit));
        OnPropertyChanged(nameof(CurrentCase));
        OnPropertyChanged(nameof(CurrentSpec));
        OnPropertyChanged(nameof(CurrentCaseColor));
        OnPropertyChanged(nameof(Constraints));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CaseInfo));
        OnPropertyChanged(nameof(ColorMap));
        RebuildScene();
    }

    private void RebuildScene()
    {
        if (CurrentSolution is { } s && CurrentUnit is { } u)
        {
            var map = ColorMap;
            var spec = CurrentSpec;
            var render = new Scene3DBuilder.CaseRender(spec.WallThickness, CurrentCase?.Color ?? "#C9A26B", IsOpen);
            Scene3D = Scene3DBuilder.Build(s, u, Constraints, id => map.TryGetValue(id, out var c) ? c : Colors.SteelBlue, int.MaxValue, render).Root;
        }
        else
        {
            Scene3D = null;
        }
    }

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Compute() => _ = ComputeAsync();

    private CancellationTokenSource? _cts;

    /// <summary>Calcul en cours (tâche de fond : la fenêtre reste utilisable).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowFixArticle))] private bool _isBusy;

    /// <summary>
    /// Colisage calculé en tâche de fond (petits produits : des dizaines de milliers par caisse) ; un nouveau calcul
    /// annule le précédent.
    /// </summary>
    private async Task ComputeAsync()
    {
        _computed = true;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Solutions.Clear();
        Message = null;
        if (Article is not { } article)
        {
            Message = "Choisissez l'article à mettre en caisse.";
            SelectedSolution = null;
            return;
        }

        if (Mode == CaseMode.Catalog && SelectedCase == null)
        {
            Message = ForceCase
                ? "Choisissez la caisse à imposer."
                : CaseEngine.Diagnose(article, _main.Db.Cases, Gap, Axis) +
                  " Vous pouvez aussi cocher « Forcer la caisse », utiliser une caisse spécifique ou ajouter une caisse au catalogue.";
            OnPropertyChanged(nameof(ShowFixArticle));
            SelectedSolution = null;
            return;
        }

        // Données du calcul figées avant la tâche de fond.
        var (mode, cases, gap, target, manual, pallet, constraints, axis) =
            (Mode, _main.Db.Cases.ToList(), Gap, TargetQuantity, ManualLimit, ActivePallet, PalletConstraints, Axis);
        var selected = SelectedCase;
        var spec = WithGap(Spec);
        var forced = ForceCase && selected != null && !PossibleCases.Contains(selected);
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => mode switch
            {
                CaseMode.Best => CaseEngine.Propose(article, cases, gap, target, manual, pallet, constraints, axis),
                CaseMode.Catalog => CaseEngine.Solve(article, selected!.ToSpec(gap), target, selected, pallet, constraints, axis),
                _ => CaseEngine.Solve(article, spec, target, null, pallet, constraints, axis)
            }, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            if (forced && result.Solutions.Count > 0)
            {
                result.Messages.Add($"Caisse {selected!.Code} forcée.");
            }

            foreach (var sol in result.Solutions)
            {
                Solutions.Add(new SolutionViewModel(sol, article));
            }

            Message = result.Messages.Count > 0 ? string.Join(Environment.NewLine, result.Messages) : null;
            SelectedSolution = Solutions.FirstOrDefault(x => x.Recommended) ?? Solutions.FirstOrDefault();
            OnPropertyChanged(nameof(ShowFixArticle));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private void CreateCaseArticle() => CreateCase();

    private Article? CreateCase()
    {
        if (Article == null || CurrentSolution == null)
        {
            _main.ShowToast("Calculez d'abord le colisage.", "Warning");
            return null;
        }

        var baseCode = "CAI-" + Article.Code + (CurrentCase is { } c ? "-" + c.Code : "");
        var code = baseCode;
        for (var i = 2; _main.Db.Articles.Any(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)); i++)
        {
            code = $"{baseCode}-{i}";
        }

        var box = CaseEngine.CreateCaseArticle(Article, CurrentSolution, CurrentSpec, code, CurrentCase, Axis);
        if (CurrentCase is { } type)
        {
            box.Designation = $"{type.Name} de {CurrentSolution.ItemsPerUnit} × {Article.Code}";
            box.Color = type.Color;
        }

        _main.Db.Articles.Add(box);
        _main.SaveDatabase();
        _main.Articles.RebuildTree();
        _main.NotifyArticlesChanged();
        RefreshArticles();
        _main.ShowToast($"Article {box.Code} créé ({box.DimensionsText}, {box.Weight.ToString(Formats.UnitWeight, Fr)} kg) : il peut maintenant être palettisé.", "Ok");
        return box;
    }

    // ------------------------------------------------------------------ Impression

    /// <summary>Colisage affiché, prêt pour la fiche.</summary>
    private CaseEngine.CaseSheet? CurrentSheet =>
        Article != null && CurrentSolution is { FirstUnit: not null } s ? new CaseEngine.CaseSheet(Article, CurrentCase, CurrentSpec, s, Axis) : null;

    public bool CanPrintCaseSheet => !IsBusy && CurrentSheet != null;

    /// <summary>Fiche palette des caisses : palette de destination choisie et caisse palettisable.</summary>
    public bool CanPrintPalletSheet => !IsBusy && Article != null && CurrentSolution is { CasesPerPallet: > 0 } && ActivePallet != null;

    public bool CanPrintPackagingSheet => CanPrintCaseSheet && CanPrintPalletSheet;

    public void PrintCaseSheet()
    {
        if (CurrentSheet is { } sheet)
        {
            PrintService.PrintCaseSheet(sheet, null, _main.Db, _main.ColorsFor(sheet.Content));
        }
    }

    /// <summary>Caisse du colisage affiché (article non enregistré) palettisée sur la palette de destination.</summary>
    private (Article Box, Packaging Packaging, Solution Solution)? PalletOfCases()
    {
        if (Article == null || CurrentSolution is not { CasesPerPallet: > 0 } s || ActivePallet is not { } pallet)
        {
            return null;
        }

        var box = CaseEngine.CreateCaseArticle(Article, s, CurrentSpec, "CAI-" + Article.Code + (CurrentCase is { } c ? "-" + c.Code : ""), CurrentCase, Axis);
        if (CurrentCase is { } type)
        {
            box.Designation = $"{type.Name} de {s.ItemsPerUnit} × {Article.Code}";
            box.Color = type.Color;
        }

        var constraints = PalletConstraints;
        var best = HomogeneousEngine.Solve(box, BaseInfo.From(pallet, false, 1, 1), constraints).Recommended;
        if (best?.FirstUnit == null)
        {
            _main.ShowToast($"Caisse non palettisable sur {pallet.Code} avec les contraintes saisies.", "Warning");
            return null;
        }

        var packaging = new Packaging
        {
            Code = box.Code, Name = $"Palette de {box.Designation}", Kind = PackagingKind.Homogene, ArticleId = box.Id, PalletId = pallet.Id,
            Constraints = constraints, Solution = best
        };
        return (box, packaging, best);
    }

    private IReadOnlyDictionary<Guid, Color> BoxColors(Article box) =>
        new Dictionary<Guid, Color> { [box.Id] = ArticleColors.Parse(box.Color, Color.FromRgb(0xC9, 0xA2, 0x6B)) };

    public void PrintPalletSheet()
    {
        if (PalletOfCases() is var (box, p, s))
        {
            PrintService.PrintSheet(p, s, s.FirstUnit!, _main.Db, BoxColors(box), box);
        }
    }

    public void PrintPackagingSheet()
    {
        if (CurrentSheet is { } sheet && PalletOfCases() is var (box, p, s))
        {
            PrintService.PrintPackagingSheet(sheet, box, p, s, s.FirstUnit!, _main.Db, _main.ColorsFor(sheet.Content), BoxColors(box));
        }
    }

    /// <summary>Crée l'article caisse puis ouvre un conditionnement homogène pour le palettiser.</summary>
    [RelayCommand]
    private void CreateAndPalletize()
    {
        if (CreateCase() is { } box)
        {
            _main.Packagings.CreateFor(box, ActivePallet, IsPalletChoiceEnabled ? PalletMaxHeight : null,
                IsPalletChoiceEnabled ? PalletOverhangLength : 0, IsPalletChoiceEnabled ? PalletOverhangWidth : 0);
        }
    }

    // ------------------------------------------------------------------ Catalogue des caisses

    public ObservableCollection<CaseType> CatalogCases { get; } = [];
    public IReadOnlyList<CaseMaterial> Materials { get; } = Enum.GetValues<CaseMaterial>();
    public IReadOnlyList<string> Swatches { get; } = ["#C9A26B", "#A9824E", "#E3CBA0", "#8E969D", "#5D6D7E", "#2E86C1", "#27AE60", "#C8A165", "#E74C3C", "#F4F6F7"];

    [ObservableProperty] private CaseType? _catalogSelected;
    [ObservableProperty] private CaseType _catalogDraft = new();
    [ObservableProperty] private Model3DGroup? _catalogPreview;

    partial void OnCatalogDraftChanged(CaseType value) => CatalogPreview = Scene3DBuilder.BuildCasePreview(value);
    [ObservableProperty] private IReadOnlyList<string> _catalogErrors = [];

    partial void OnCatalogSelectedChanged(CaseType? value)
    {
        if (value != null)
        {
            CatalogDraft = value.Clone();
            CatalogErrors = [];
        }
    }

    public void RefreshCatalog()
    {
        var selected = CatalogSelected?.Id;
        CatalogCases.Clear();
        foreach (var c in _main.Db.Cases.OrderBy(c => c.Family).ThenBy(c => c.InnerLength * c.InnerWidth * c.InnerHeight))
        {
            CatalogCases.Add(c);
        }

        CatalogSelected = CatalogCases.FirstOrDefault(c => c.Id == selected) ?? CatalogCases.FirstOrDefault();
    }

    /// <summary>Base changée (ouverture d'une autre base) : catalogue, articles et caisses possibles suivent.</summary>
    public void Reload()
    {
        RefreshPallets();
        RefreshCatalog();
        RefreshArticles();
        RefreshPossibleCases();
        Compute();
    }

    private void CatalogChanged()
    {
        _main.SaveDatabase();
        RefreshCatalog();
        RefreshPossibleCases();
        Compute();
    }

    [RelayCommand]
    private void CatalogNew()
    {
        CatalogSelected = null;
        CatalogDraft = new CaseType { Code = "", Name = "Nouvelle caisse", Family = "Carton standard", InnerLength = 400, InnerWidth = 300, InnerHeight = 200, WallThickness = 3, Tare = 0.3, MaxWeight = 15 };
        CatalogErrors = [];
    }

    [RelayCommand]
    private void CatalogDuplicate()
    {
        if (CatalogSelected == null)
        {
            return;
        }

        var copy = CatalogSelected.Clone();
        copy.Id = Guid.NewGuid();
        copy.Code += "-B";
        copy.IsBuiltIn = false;
        CatalogSelected = null;
        CatalogDraft = copy;
    }

    [RelayCommand]
    private void CatalogPickColor(string hex)
    {
        var copy = CatalogDraft.Clone();
        copy.Color = hex;
        CatalogDraft = copy;
    }

    [RelayCommand]
    private void CatalogSave()
    {
        var c = CatalogDraft.Clone();
        c.Code = (c.Code ?? "").Trim();
        var errors = c.Validate();
        if (_main.Db.Cases.Any(x => x.Id != c.Id && string.Equals(x.Code, c.Code, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Insert(0, $"Le code {c.Code} existe déjà.");
        }

        CatalogErrors = errors;
        if (errors.Count > 0)
        {
            _main.ShowToast("Caisse non enregistrée : " + errors[0], "Warning");
            return;
        }

        var index = _main.Db.Cases.FindIndex(x => x.Id == c.Id);
        if (index >= 0)
        {
            _main.Db.Cases[index] = c;
        }
        else
        {
            _main.Db.Cases.Add(c);
        }

        CatalogSelected = null;
        CatalogChanged();
        CatalogSelected = CatalogCases.FirstOrDefault(x => x.Id == c.Id);
        _main.ShowToast($"Caisse {c.Code} enregistrée (extérieur {c.OuterText} mm).", "Ok");
    }

    [RelayCommand]
    private void CatalogDelete()
    {
        if (CatalogSelected is not { } c || !_main.Dialogs.Confirm($"Supprimer la caisse {c.Code} du catalogue ?" +
                                                                    (c.IsBuiltIn ? " (elle pourra être restaurée)" : "")))
        {
            return;
        }

        _main.Db.Cases.Remove(c);
        CatalogSelected = null;
        CatalogChanged();
    }

    [RelayCommand]
    private void CatalogRestore()
    {
        var added = _main.Db.MergeCaseCatalog();
        CatalogChanged();
        _main.ShowToast(added > 0 ? $"{added} caisse(s) du catalogue restaurée(s)." : "Toutes les caisses du catalogue sont présentes.", "Ok");
    }
}
