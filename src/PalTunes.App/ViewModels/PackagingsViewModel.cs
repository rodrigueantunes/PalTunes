using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.App.Services;
using PalTunes.Core.Engine;
using PalTunes.Core.Export;
using PalTunes.Core.Models;

namespace PalTunes.App.ViewModels;

public sealed partial class LineViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProfileText), nameof(SubtotalText), nameof(Subtotal))] private Article? _article;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubtotalText), nameof(Subtotal))] private int _quantity = 1;

    /// <summary>Couleur de l'article dans les vues (légende de la ligne).</summary>
    [ObservableProperty] private Brush? _viewBrush;

    /// <summary>Caisse créée au colisage : son colisage peut être rouvert depuis la ligne.</summary>
    public bool HasColisage => Article is { Kind: ArticleKind.Caisse, CaseContent: not null };

    partial void OnArticleChanged(Article? value) => OnPropertyChanged(nameof(HasColisage));

    public double Subtotal => (Article?.Weight ?? 0) * Math.Max(0, Quantity);

    public string SubtotalText => Article == null ? "" : $"{Subtotal.ToString(Formats.TotalWeight, Fr)} kg";

    /// <summary>Profil de gerbage déduit (étude hétérogène §3) : zone conseillée et capacité.</summary>
    public string ProfileText
    {
        get
        {
            if (Article == null)
            {
                return "";
            }

            // Tube ou bobine sans axe imposé : posé debout dans les couches, c'est ce profil qui compte (couché, seuls des
            // exemplaires identiques peuvent aller dessus).
            var free = Article.Kind is ArticleKind.Tube or ArticleKind.Bobine && Article.CoilAxis == CoilAxis.Indifferent;
            var p = free ? StackingProfile.For(Article, CoilAxis.Vertical) : StackingProfile.For(Article);
            var capacity = p.Capacity < 10 ? p.Capacity.ToString("0.###", Fr) : p.Capacity.ToString("0.#", Fr);
            return $"{p.ZoneLabel} · porte {capacity} kg{(p.CapacityEntered ? "" : " (déduit)")}{(free ? " debout ; couché : identiques seulement" : "")}";
        }
    }
}

public sealed record AxisChoice(CoilAxis? Value, string Label);

public sealed record UnitChoice(LoadUnit Unit, string Label);

/// <summary>Carte d'une solution dans le bandeau.</summary>
public sealed class SolutionViewModel(Solution s, Article? article)
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public Solution Solution { get; } = s;
    public string Title => Solution.Title;
    public bool Recommended => Solution.Recommended;
    public bool Compliant => Solution.IsCompliant;
    public bool HasWarnings => Solution.Warnings.Count > 0;
    public string BaseLabel => Solution.Base.Label;

    /// <summary>Colisage : résultat sur la palette de destination.</summary>
    public string? PalletLine => Solution.DestinationPallet == null
        ? null
        : Solution.CasesPerPallet <= 0
            ? $"Non palettisable sur {Solution.DestinationPallet}"
            : Solution.Kind == PackagingKind.Heterogene
                ? $"{Solution.CasesPerPallet.ToString("#,0", Fr)} caisses / {Solution.DestinationPallet} → {Solution.PalletCount} palette(s)"
                : $"{Solution.CasesPerPallet.ToString("#,0", Fr)} caisses / {Solution.DestinationPallet} → {Solution.ItemsPerPallet.ToString("#,0", Fr)} produits / palette";

    public string Headline => Solution.Kind == PackagingKind.Homogene
        ? $"{Solution.ItemsPerUnit.ToString("#,0", Fr)} {Noun(article?.Kind, Solution.ItemsPerUnit)}"
        : Solution.Base.IsCase
            ? $"{Solution.UnitCount} caisse(s) · {Solution.TotalItems.ToString("#,0", Fr)} produits"
            : $"{Solution.UnitCount} unité(s) · {Solution.TotalItems.ToString("#,0", Fr)} produits";

    public string Details
    {
        get
        {
            var m = Solution.FirstUnit?.Metrics;
            if (m == null)
            {
                return "";
            }

            return Solution.Kind == PackagingKind.Homogene
                ? $"{Solution.ItemsPerLayer}/couche × {Solution.LayerCount} · {Solution.PatternLabel} · {m.EnclosureHeight:0} mm · {m.FillRate.ToString("0", Fr)} %"
                : $"Score {Solution.Score * 100:0}/100 · remplissage {Solution.Units.Average(u => u.Metrics.FillRate).ToString("0", Fr)} %";
        }
    }

    public string Orientation => Solution.Kind == PackagingKind.Homogene ? Solution.OrientationText : Solution.Description;

    private static string Noun(ArticleKind? kind, int count)
    {
        var word = kind switch
        {
            ArticleKind.Bobine => "bobine",
            ArticleKind.Tube => "tube",
            ArticleKind.Plaque => "plaque",
            ArticleKind.Sac => "sac",
            ArticleKind.Fut => "fût",
            _ => "produit"
        };
        return count > 1 ? word + "s" : word;
    }
}

/// <summary>
/// Conditionnement en cours de création ou de modification, non enregistré : gardé en mémoire pendant la session
/// (liste « En cours ») pour passer d'un conditionnement à l'autre sans perdre la saisie.
/// </summary>
public sealed partial class WorkItem(Guid id) : ObservableObject
{
    public Guid Id { get; } = id;

    /// <summary>État de l'éditeur au dernier changement (solution retenue comprise).</summary>
    public Packaging Snapshot { get; private set; } = new();

    public bool IsNew { get; private set; }

    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _state = "";

    /// <summary>Conditionnement affiché dans l'éditeur.</summary>
    [ObservableProperty] private bool _isCurrent;

    public void Update(Packaging snapshot, bool isNew, string subtitle)
    {
        Snapshot = snapshot;
        IsNew = isNew;
        Code = string.IsNullOrWhiteSpace(snapshot.Code) ? "(sans code)" : snapshot.Code;
        Subtitle = subtitle;
        State = isNew ? "Nouveau" : "Modifié";
    }
}

public sealed partial class PackagingsViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly MainViewModel _main;
    private SceneResult? _scene;
    private GeometryModel3D? _hovered;
    private CancellationTokenSource? _cts;
    private bool _loading;

    public PackagingsViewModel(MainViewModel main)
    {
        _main = main;
        Picker = new ArticlePicker(() => _main.Db, () => Lines.Select(l => l.Article).Append(Article));
        Lines.CollectionChanged += (_, e) =>
        {
            foreach (LineViewModel l in e.NewItems ?? Array.Empty<LineViewModel>())
            {
                l.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName == nameof(LineViewModel.ViewBrush))
                    {
                        return;
                    }

                    OnPropertyChanged(nameof(LinesSummary));
                    MarkDirty();
                    if (a.PropertyName == nameof(LineViewModel.Article))
                    {
                        BuildColorMap();
                    }
                };
            }

            OnPropertyChanged(nameof(LinesSummary));
            BuildColorMap();
        };
        _useArticleColors = main.Settings.Current.UseArticleColors;
        RefreshLists();
        Refresh();
    }

    // ------------------------------------------------------------------ Listes

    public ObservableCollection<Packaging> Packagings { get; } = [];
    public ObservableCollection<Article> Articles { get; } = [];
    public ObservableCollection<PalletType> Pallets { get; } = [];
    public ObservableCollection<LineViewModel> Lines { get; } = [];

    /// <summary>Choix du client puis de l'article (homogène et lignes hétérogènes).</summary>
    public ArticlePicker Picker { get; }

    /// <summary>Total de la commande hétérogène et rappel de la charge admissible de la base.</summary>
    public string LinesSummary
    {
        get
        {
            var weight = Lines.Sum(l => l.Subtotal);
            var count = Lines.Where(l => l.Article != null).Sum(l => Math.Max(0, l.Quantity));
            var capacity = Pallet == null ? 0 : BaseInfo.From(Pallet, PalletRotated, CountAlongLength, CountAlongWidth).DynamicCapacity;
            var max = Draft.Constraints.MaxLoadWeight > 0 ? Draft.Constraints.MaxLoadWeight : capacity;
            return $"{count.ToString("#,0", Fr)} produit(s) · {weight.ToString(Formats.TotalWeight, Fr)} kg" +
                   (max > 0 ? $" · charge admissible par unité {max.ToString("#,0", Fr)} kg" + (weight > max ? $" → au moins {Math.Ceiling(weight / max):0} unités" : "") : "");
        }
    }
    public ObservableCollection<SolutionViewModel> Solutions { get; } = [];
    public ObservableCollection<UnitChoice> Units { get; } = [];

    public IReadOnlyList<AxisChoice> AxisChoices { get; } =
    [
        new(null, "Automatique (le meilleur)"),
        new(CoilAxis.Vertical, "Forcer l'axe vertical"),
        new(CoilAxis.Horizontal, "Forcer l'axe horizontal")
    ];

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private Packaging? _selectedPackaging;

    partial void OnFilterChanged(string value) => Refresh();

    /// <summary>Conditionnements récents gardés dans l'écran de création (les autres : espace « Gestion des conditionnements »).</summary>
    public const int RecentCount = 10;

    /// <summary>« Récents » ou, pendant une recherche, le nombre de résultats (recherche sur tous les conditionnements).</summary>
    [ObservableProperty] private string _listTitle = "Récents";

    /// <summary>Section « Récents » dépliée (par défaut).</summary>
    [ObservableProperty] private bool _isRecentOpen = true;

    // ------------------------------------------------------------------ En cours (non enregistrés)

    /// <summary>Conditionnements créés ou modifiés, non enregistrés (le plus récent en tête).</summary>
    public ObservableCollection<WorkItem> InProgress { get; } = [];

    [ObservableProperty] private WorkItem? _selectedWork;

    /// <summary>Section « En cours » dépliée (par défaut).</summary>
    [ObservableProperty] private bool _isInProgressOpen = true;

    public string InProgressTitle => $"En cours ({InProgress.Count})";
    public bool HasInProgress => InProgress.Count > 0;

    private void NotifyInProgress()
    {
        OnPropertyChanged(nameof(InProgressTitle));
        OnPropertyChanged(nameof(HasInProgress));
    }

    /// <summary>Recopie l'éditeur dans « En cours » tant qu'il n'est pas enregistré.</summary>
    private void TrackCurrent()
    {
        if (_loading || !IsDirty)
        {
            return;
        }

        var p = BuildPackaging();
        p.Solution = SelectedSolution?.Solution ?? Draft.Solution;
        var item = InProgress.FirstOrDefault(w => w.Id == p.Id);
        if (item == null)
        {
            item = new WorkItem(p.Id);
            InProgress.Insert(0, item);
            NotifyInProgress();
        }

        var what = p.Kind == PackagingKind.Homogene
            ? Article?.DisplayName ?? "Article à choisir"
            : $"Hétérogène · {p.Lines.Count} ligne(s)";
        item.Update(p, IsEditingNew, what);
        foreach (var w in InProgress)
        {
            w.IsCurrent = w.Id == p.Id;
        }

        _loading = true;
        SelectedWork = item;
        _loading = false;
    }

    /// <summary>Avant d'afficher un autre conditionnement : la saisie non enregistrée reste dans « En cours ».</summary>
    private void StashCurrent()
    {
        TrackCurrent();
        foreach (var w in InProgress)
        {
            w.IsCurrent = false;
        }

        _loading = true;
        SelectedWork = null;
        _loading = false;
    }

    partial void OnIsDirtyChanged(bool value) => TrackCurrent();

    partial void OnSelectedWorkChanged(WorkItem? value)
    {
        if (_loading || value == null)
        {
            return;
        }

        StashCurrent();
        _loading = true;
        SelectedPackaging = Packagings.FirstOrDefault(x => x.Id == value.Id);
        _loading = false;
        LoadDraft(value.Snapshot, isNew: value.IsNew);
        IsDirty = true;
        _ = ComputeAsync(preferStored: true);
    }

    /// <summary>Abandonne une saisie non enregistrée (le conditionnement enregistré, s'il existe, est réaffiché).</summary>
    [RelayCommand]
    private void Discard(WorkItem? item)
    {
        if (item == null)
        {
            return;
        }

        var current = item.IsCurrent || item.Id == Draft.Id;
        InProgress.Remove(item);
        NotifyInProgress();
        if (!current)
        {
            return;
        }

        IsDirty = false;
        _loading = true;
        SelectedWork = null;
        _loading = false;
        if (_main.Db.Packagings.FirstOrDefault(x => x.Id == item.Id) is { } saved)
        {
            LoadDraft(saved, isNew: false);
            _ = ComputeAsync(preferStored: true);
        }
        else if (InProgress.FirstOrDefault() is { } next)
        {
            SelectedWork = next;
        }
        else if (Packagings.FirstOrDefault() is { } recent)
        {
            _loading = true;
            SelectedPackaging = null;
            _loading = false;
            SelectedPackaging = recent;
        }
    }

    public void Refresh()
    {
        var selected = SelectedPackaging?.Id;
        Packagings.Clear();
        var matches = _main.Db.Packagings
            .Where(p => Filter.Length == 0 || $"{p.Code} {p.Name} {_main.Db.FindArticle(p.ArticleId)?.Code} {_main.Db.ClientLabel(_main.Db.FindArticle(p.ArticleId)?.Client)}".Contains(Filter, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        var shown = Filter.Length == 0
            ? matches.OrderByDescending(p => p.ModifiedAt).Take(RecentCount).ToList()
            : matches.OrderBy(p => p.Code, StringComparer.CurrentCultureIgnoreCase).ToList();

        // Conditionnement ouvert depuis la gestion : toujours visible, même s'il n'est pas récent.
        if (selected != null && shown.All(p => p.Id != selected) && _main.Db.Packagings.FirstOrDefault(p => p.Id == selected) is { } open)
        {
            shown.Insert(0, open);
        }

        foreach (var p in shown)
        {
            Packagings.Add(p);
        }

        ListTitle = Filter.Length == 0 ? "Récents" : $"Résultats ({matches.Count})";

        if (selected != null && Packagings.FirstOrDefault(p => p.Id == selected) is { } again)
        {
            _loading = true;
            SelectedPackaging = again;
            _loading = false;
        }
        else if (SelectedPackaging == null && !IsEditingNew && Packagings.Count > 0)
        {
            SelectedPackaging = Packagings[0];
        }
    }

    /// <summary>Ouvre un conditionnement dans l'écran de création (depuis l'espace « Gestion des conditionnements »).</summary>
    public void Open(Packaging p)
    {
        _main.SelectedSection = "Packagings";
        StashCurrent();
        Filter = "";
        _loading = true;
        SelectedPackaging = null;
        _loading = false;
        if (Packagings.All(x => x.Id != p.Id))
        {
            Packagings.Insert(0, p);
        }

        SelectedPackaging = Packagings.First(x => x.Id == p.Id);
    }

    public void RefreshLists()
    {
        var article = Article;
        var pallet = Pallet;
        Articles.Clear();
        foreach (var a in _main.Db.Articles.OrderBy(a => a.Code, StringComparer.CurrentCultureIgnoreCase))
        {
            Articles.Add(a);
        }

        Pallets.Clear();
        foreach (var p in _main.Db.Pallets.OrderBy(p => p.Family).ThenBy(p => p.Code))
        {
            Pallets.Add(p);
        }

        _loading = true;
        // Les articles relus de la base entrent dans la liste avant d'être choisis (la sélection n'est jamais perdue).
        var newArticle = article == null ? null : Articles.FirstOrDefault(a => a.Id == article.Id);
        var newLines = Lines.Select(l => l.Article == null ? null : Articles.FirstOrDefault(a => a.Id == l.Article.Id)).ToList();
        Picker.Sync([newArticle, .. newLines]);
        Article = newArticle;
        Pallet = pallet == null ? null : Pallets.FirstOrDefault(p => p.Id == pallet.Id);
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].Article = newLines[i];
        }

        Picker.Refresh();
        _loading = false;
        BuildColorMap();
    }

    // ------------------------------------------------------------------ Éditeur

    [ObservableProperty] private Packaging _draft = new();
    [ObservableProperty] private bool _isEditingNew;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsHomogeneous), nameof(IsHeterogeneous))] private PackagingKind _kind;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BaseText), nameof(ShowAxis), nameof(ArticleWarning), nameof(ArticleHasColisage))] private Article? _article;

    /// <summary>Poids unitaire impossible pour les dimensions (erreur de saisie ou d'unité).</summary>
    public string? ArticleWarning => Article == null ? null : ArticleSchema.Warnings(Article);

    /// <summary>Article caisse créé au colisage (produit connu) : « Voir le colisage ».</summary>
    public bool ArticleHasColisage => _main.HasColisage(Article);

    [RelayCommand]
    private void OpenArticleColisage()
    {
        if (Article is { } a && _main.HasColisage(a))
        {
            _main.OpenColisage(a);
        }
    }

    [RelayCommand]
    private void OpenLineColisage(LineViewModel? line)
    {
        if (line?.Article is { } a && _main.HasColisage(a))
        {
            _main.OpenColisage(a);
        }
    }

    [RelayCommand]
    private void OpenArticleSheet()
    {
        if (Article is { } a)
        {
            _main.ShowArticle(a);
        }
    }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BaseText))] private PalletType? _pallet;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BaseText))] private bool _palletRotated;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BaseText))] private int _countAlongLength = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(BaseText))] private int _countAlongWidth = 1;
    [ObservableProperty] private AxisChoice? _axis;
    [ObservableProperty] private bool _corners;
    [ObservableProperty] private bool _isDirty;

    public bool IsHomogeneous => Kind == PackagingKind.Homogene;
    public bool IsHeterogeneous => Kind == PackagingKind.Heterogene;

    public bool ShowAxis => IsHeterogeneous || Article is { Kind: ArticleKind.Tube or ArticleKind.Bobine };

    public string BaseText
    {
        get
        {
            if (Pallet == null)
            {
                return "Aucune palette";
            }

            var b = BaseInfo.From(Pallet, PalletRotated, CountAlongLength, CountAlongWidth);
            return b.PhysicalCount == 1
                ? $"Base {b.Length:0} × {b.Width:0} × {b.PalletHeight:0} mm (1 palette)"
                : $"Base {b.Length:0} × {b.Width:0} mm : {b.PhysicalCount} palettes physiques ({b.CountAlongLength} × {b.CountAlongWidth})";
        }
    }

    partial void OnKindChanged(PackagingKind value)
    {
        MarkDirty();
        BuildColorMap();
    }
    partial void OnArticleChanged(Article? value)
    {
        MarkDirty();
        BuildColorMap();
    }

    /// <summary>« Couleur d'origine » : couleurs des fiches articles ; décochée (par défaut), couleurs bien distinctes.</summary>
    [ObservableProperty] private bool _useArticleColors;

    partial void OnUseArticleColorsChanged(bool value)
    {
        _main.Settings.Current.UseArticleColors = value;
        _main.Settings.Save();
        BuildColorMap();
        RebuildScene();
    }
    partial void OnPalletChanged(PalletType? value) => MarkDirty();
    partial void OnPalletRotatedChanged(bool value) => MarkDirty();
    partial void OnCountAlongLengthChanged(int value) => MarkDirty();
    partial void OnCountAlongWidthChanged(int value) => MarkDirty();
    partial void OnAxisChanged(AxisChoice? value) => MarkDirty();
    partial void OnCornersChanged(bool value) => MarkDirty();

    public void MarkDirty()
    {
        if (!_loading)
        {
            IsDirty = true;
            TrackCurrent();
        }
    }

    partial void OnSelectedPackagingChanged(Packaging? value)
    {
        if (_loading || value == null)
        {
            return;
        }

        StashCurrent();
        if (InProgress.FirstOrDefault(w => w.Id == value.Id) is { } pending)
        {
            SelectedWork = pending; // modifications non enregistrées de ce conditionnement
            return;
        }

        LoadDraft(value, isNew: false);
        _ = ComputeAsync(preferStored: true);
    }

    private void LoadDraft(Packaging p, bool isNew)
    {
        _loading = true;
        Draft = p.Clone();
        IsEditingNew = isNew;
        Kind = p.Kind;
        var article = Articles.FirstOrDefault(a => a.Id == p.ArticleId);
        var lineArticles = p.Lines.Select(l => Articles.FirstOrDefault(a => a.Id == l.ArticleId)).ToList();
        Picker.Sync([article, .. lineArticles]);
        Article = article;
        Pallet = Pallets.FirstOrDefault(x => x.Id == p.PalletId) ?? Pallets.FirstOrDefault(x => x.Code == "EUR1");
        PalletRotated = p.PalletRotated;
        CountAlongLength = Math.Max(1, p.CountAlongLength);
        CountAlongWidth = Math.Max(1, p.CountAlongWidth);
        Axis = AxisChoices.First(a => a.Value == p.Constraints.ForcedAxis);
        Corners = p.Constraints.Corners;
        Lines.Clear();
        for (var i = 0; i < p.Lines.Count; i++)
        {
            Lines.Add(new LineViewModel { Article = lineArticles[i], Quantity = p.Lines[i].Quantity });
        }

        Picker.Sync();
        _loading = false;
        IsDirty = isNew;
        BuildColorMap();
        ClearSolutions();
    }

    /// <summary>Conditionnement tel qu'affiché dans l'éditeur (copie, pour l'impression).</summary>
    public Packaging CurrentPackaging() => BuildPackaging();

    /// <summary>Recopie l'éditeur dans un conditionnement (le brouillon, ou une copie pour le calcul).</summary>
    private Packaging BuildPackaging()
    {
        var p = Draft.Clone();
        p.Kind = Kind;
        p.ArticleId = Article?.Id;
        p.PalletId = Pallet?.Id;
        p.PalletRotated = PalletRotated;
        p.CountAlongLength = Math.Clamp(CountAlongLength, 1, 6);
        p.CountAlongWidth = Math.Clamp(CountAlongWidth, 1, 6);
        p.Constraints.ForcedAxis = Axis?.Value;
        p.Constraints.Corners = Corners;
        p.Lines = Lines.Where(l => l.Article != null && l.Quantity > 0)
            .Select(l => new PackagingLine { ArticleId = l.Article!.Id, Quantity = l.Quantity }).ToList();
        p.Code = (p.Code ?? "").Trim();
        return p;
    }

    [RelayCommand]
    private void New(PackagingKind kind)
    {
        StashCurrent();
        _loading = true;
        SelectedPackaging = null;
        _loading = false;
        var p = new Packaging
        {
            Kind = kind,
            Code = UniqueCode(kind == PackagingKind.Homogene ? "CDT" : "MIX"),
            PalletId = Pallets.FirstOrDefault(x => x.Code == "EUR1")?.Id
        };
        LoadDraft(p, isNew: true);
        if (kind == PackagingKind.Heterogene)
        {
            Lines.Add(new LineViewModel());
        }
    }

    /// <summary>Nouveau conditionnement homogène pour un article (depuis la fiche article).</summary>
    public void CreateFor(Article a, PalletType? pallet = null, double? maxTotalHeight = null, double overhangLength = 0, double overhangWidth = 0)
    {
        _main.SelectedSection = "Packagings";
        StashCurrent();
        _loading = true;
        SelectedPackaging = null;
        _loading = false;
        var p = new Packaging
        {
            Kind = PackagingKind.Homogene,
            Code = UniqueCode("CDT-" + a.Code),
            Name = a.Designation,
            ArticleId = a.Id,
            PalletId = Pallets.FirstOrDefault(x => x.Code == "EUR1")?.Id
        };

        // Exigences du client de l'article : palette imposée, hauteur maxi, gerbage.
        if (_main.Db.FindClient(a.Client) is { } client)
        {
            if (client.DefaultPalletId is { } clientPallet && Pallets.Any(x => x.Id == clientPallet))
            {
                p.PalletId = clientPallet;
            }

            if (client.MaxTotalHeight is { } height)
            {
                p.Constraints.MaxTotalHeight = height;
            }

            if (client.MaxStackLevels is { } levels)
            {
                p.Constraints.MaxStackLevels = levels;
            }
        }

        // Palette et hauteur choisies en amont (colisage) : elles priment.
        if (pallet != null)
        {
            p.PalletId = pallet.Id;
        }

        if (maxTotalHeight is > 0)
        {
            p.Constraints.MaxTotalHeight = maxTotalHeight.Value;
        }

        p.Constraints.OverhangLength = overhangLength;
        p.Constraints.OverhangWidth = overhangWidth;

        LoadDraft(p, isNew: true);
        _ = ComputeAsync(preferStored: false);
    }

    private string UniqueCode(string prefix)
    {
        for (var i = 1; ; i++)
        {
            var code = prefix.Contains('-') && i == 1 ? prefix : $"{prefix}-{i:000}";
            if (_main.Db.Packagings.All(p => !string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                return code;
            }
        }
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedPackaging == null)
        {
            return;
        }

        var copy = BuildPackaging();
        copy.Id = Guid.NewGuid();
        copy.Code = UniqueCode(copy.Code + "-COPIE");
        copy.Solution = SelectedSolution?.Solution;
        StashCurrent();
        _loading = true;
        SelectedPackaging = null;
        _loading = false;
        LoadDraft(copy, isNew: true);
        _ = ComputeAsync(preferStored: true);
    }

    [RelayCommand]
    private void Delete()
    {
        var p = SelectedPackaging;
        if (p == null || !_main.Dialogs.Confirm($"Supprimer le conditionnement {p.Code} ?"))
        {
            return;
        }

        _main.Db.Packagings.Remove(p);
        _main.SaveDatabase();
        if (InProgress.FirstOrDefault(w => w.Id == p.Id) is { } gone)
        {
            InProgress.Remove(gone);
            NotifyInProgress();
        }

        SelectedPackaging = null;
        ClearSolutions();
        Refresh();
    }

    [RelayCommand]
    private void Save()
    {
        var p = BuildPackaging();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(p.Code))
        {
            errors.Add("Le code du conditionnement est obligatoire.");
        }
        else if (_main.Db.Packagings.Any(x => x.Id != p.Id && string.Equals(x.Code, p.Code, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Le code {p.Code} existe déjà.");
        }

        if (p.PalletId == null)
        {
            errors.Add("Choisissez une palette.");
        }

        if (errors.Count > 0)
        {
            _main.ShowToast(errors[0], "Warning");
            return;
        }

        // La solution retenue est figée ; si elle vient de l'assistant (autre base), la base du conditionnement suit.
        if (SelectedSolution?.Solution is { } s)
        {
            p.Solution = s;
            if (s.Base.PalletId != p.PalletId || s.Base.Rotated != p.PalletRotated || s.Base.CountAlongLength != p.CountAlongLength || s.Base.CountAlongWidth != p.CountAlongWidth)
            {
                p.PalletId = s.Base.PalletId;
                p.PalletRotated = s.Base.Rotated;
                p.CountAlongLength = s.Base.CountAlongLength;
                p.CountAlongWidth = s.Base.CountAlongWidth;
            }
        }

        p.ModifiedAt = DateTime.Now;
        var index = _main.Db.Packagings.FindIndex(x => x.Id == p.Id);
        if (index >= 0)
        {
            _main.Db.Packagings[index] = p;
        }
        else
        {
            _main.Db.Packagings.Add(p);
        }

        _main.SaveDatabase();
        if (InProgress.FirstOrDefault(w => w.Id == p.Id) is { } done)
        {
            InProgress.Remove(done);
            NotifyInProgress();
        }

        IsEditingNew = false;
        _loading = true;
        Draft = p.Clone();
        Pallet = Pallets.FirstOrDefault(x => x.Id == p.PalletId);
        PalletRotated = p.PalletRotated;
        CountAlongLength = p.CountAlongLength;
        CountAlongWidth = p.CountAlongWidth;
        _loading = false;
        IsDirty = false;
        Refresh();
        _loading = true;
        SelectedPackaging = Packagings.FirstOrDefault(x => x.Id == p.Id);
        _loading = false;
        _main.ShowToast($"Conditionnement {p.Code} enregistré" + (p.Solution != null ? $" avec la solution « {p.Solution.Title} »." : "."), "Ok");
    }

    [RelayCommand]
    private void AddLine() => Lines.Add(new LineViewModel { Article = Picker.Articles.FirstOrDefault() });

    [RelayCommand]
    private void RemoveLine(LineViewModel? line)
    {
        if (line != null)
        {
            Lines.Remove(line);
        }
    }

    // ------------------------------------------------------------------ Calcul

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private SolutionViewModel? _selectedSolution;
    [ObservableProperty] private UnitChoice? _selectedUnit;
    [ObservableProperty] private bool _showingProposals;

    [RelayCommand]
    private Task Compute() => ComputeAsync(preferStored: false);

    [RelayCommand]
    private async Task Propose()
    {
        if (!IsHomogeneous)
        {
            _main.ShowToast("L'assistant compare les palettes pour un conditionnement homogène.", "Warning");
            return;
        }

        await RunAsync(p => PackagingCalculator.Propose(p, _main.Db), proposals: true, preferStored: false);
    }

    private Task ComputeAsync(bool preferStored) => RunAsync(p => PackagingCalculator.Compute(p, _main.Db), proposals: false, preferStored);

    private async Task RunAsync(Func<Packaging, EngineResult> run, bool proposals, bool preferStored)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var p = BuildPackaging();
        IsBusy = true;
        Message = null;
        try
        {
            var result = await Task.Run(() => run(p), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            ShowingProposals = proposals;
            Solutions.Clear();
            var article = _main.Db.FindArticle(p.ArticleId);
            foreach (var s in result.Solutions)
            {
                Solutions.Add(new SolutionViewModel(s, article));
            }

            Message = result.Messages.Count > 0 ? string.Join(Environment.NewLine, result.Messages) : null;
            var stored = preferStored ? Draft.Solution : null;
            SelectedSolution = (stored == null ? null : Solutions.FirstOrDefault(s => SameSolution(s.Solution, stored)))
                               ?? Solutions.FirstOrDefault(s => s.Recommended) ?? Solutions.FirstOrDefault();
            if (stored != null && SelectedSolution != null && !SameSolution(SelectedSolution.Solution, stored))
            {
                Message = "La solution enregistrée ne correspond plus aux données actuelles (article, palette ou contraintes modifiés) : la recommandée est affichée.";
            }
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

    private static bool SameSolution(Solution a, Solution b) =>
        a.Base.PalletId == b.Base.PalletId && a.Base.Length.Equals(b.Base.Length) && a.Base.Width.Equals(b.Base.Width) &&
        a.Pattern == b.Pattern && a.ItemsPerUnit == b.ItemsPerUnit && a.LayerCount == b.LayerCount &&
        a.OrientationText == b.OrientationText && a.Strategy == b.Strategy && a.UnitCount == b.UnitCount;

    private void ClearSolutions()
    {
        Solutions.Clear();
        SelectedSolution = null;
        Message = null;
    }

    partial void OnSelectedSolutionChanged(SolutionViewModel? value)
    {
        Units.Clear();
        if (value != null)
        {
            foreach (var u in value.Solution.Units)
            {
                Units.Add(new UnitChoice(u, value.Solution.Units.Count > 1 ? $"Unité {u.Index} · {u.Items.Count} produits" : $"{u.Items.Count} produits"));
            }
        }

        SelectedUnit = Units.FirstOrDefault();
        OnPropertyChanged(nameof(Recommendation));
        OnPropertyChanged(nameof(Warnings));
        OnPropertyChanged(nameof(Violations));
        OnPropertyChanged(nameof(HasMultipleUnits));
        OnPropertyChanged(nameof(CurrentSolution));
    }

    partial void OnSelectedUnitChanged(UnitChoice? value)
    {
        MaxLayers = value?.Unit.Layers.Count ?? 0;
        VisibleLayers = MaxLayers;
        PlanLayer = 1;
        OnPropertyChanged(nameof(CurrentUnit));
        OnPropertyChanged(nameof(SpecRows));
        OnPropertyChanged(nameof(LayerRows));
        OnPropertyChanged(nameof(PoseRows));
        OnPropertyChanged(nameof(Constraints));
        OnPropertyChanged(nameof(CalculationSections));
        RebuildScene();
    }

    public Solution? CurrentSolution => SelectedSolution?.Solution;
    public LoadUnit? CurrentUnit => SelectedUnit?.Unit;
    public PackagingConstraints Constraints => BuildPackaging().Constraints;

    /// <summary>Onglet « Détails du calcul » : étapes et chiffres de la solution et de l'unité affichées.</summary>
    public IReadOnlyList<DetailSection> CalculationSections => CurrentSolution is { } s && CurrentUnit is { } u
        ? CalculationDetails.Pallet(s, u, Constraints, IsHomogeneous ? Article : null, id => _main.Db.FindArticle(id), _main.ColisageSheet)
        : [];
    public bool HasMultipleUnits => Units.Count > 1;
    public string? Recommendation => SelectedSolution?.Solution.Recommendation ?? (SelectedSolution != null ? "Alternative : " + SelectedSolution.Solution.Description : null);
    public IReadOnlyList<string> Warnings => SelectedSolution?.Solution.Warnings ?? [];
    public IReadOnlyList<string> Violations => SelectedSolution?.Solution.Violations ?? [];

    public IReadOnlyList<SpecRow> SpecRows
    {
        get
        {
            if (CurrentSolution is not { } s)
            {
                return [];
            }

            if (CurrentUnit is { } unit && !ReferenceEquals(unit, s.FirstUnit))
            {
                // Hétérogène multi-unités : spécification de l'unité affichée.
                var view = new Solution
                {
                    Kind = s.Kind, Base = s.Base, Units = [unit], ItemsPerUnit = unit.Items.Count, LayerCount = unit.Layers.Count,
                    StackLevels = s.StackLevels, StackLimitReason = s.StackLimitReason, Pattern = s.Pattern, OrientationText = s.OrientationText
                };
                return PackagingSpec.Rows(view, Constraints, Article);
            }

            return PackagingSpec.Rows(s, Constraints, Article);
        }
    }

    public IReadOnlyList<PoseRow> PoseRows
    {
        get
        {
            if (CurrentUnit is not { } unit)
            {
                return [];
            }

            // Article et zone conseillée calculés une fois par article (des dizaines de milliers de lignes possibles).
            var info = new Dictionary<Guid, (string Code, string Designation, string Zone)>();
            (string Code, string Designation, string Zone) Info(Guid id)
            {
                if (!info.TryGetValue(id, out var v))
                {
                    var a = _main.Db.FindArticle(id);
                    v = (a?.Code ?? "", a?.Designation ?? "", a == null ? "" : StackingProfile.For(a).ZoneLabel);
                    info[id] = v;
                }

                return v;
            }

            return unit.Items.OrderBy(p => p.Sequence).Select(p =>
            {
                var (code, designation, zone) = Info(p.ArticleId);
                return new PoseRow(p.Sequence, code, designation, p.Layer, Math.Round(p.X), Math.Round(p.Y), Math.Round(p.Z),
                    $"{p.DX:0} × {p.DY:0} × {p.DZ:0}", p.Weight, zone);
            }).ToList();
        }
    }

    public IReadOnlyList<LayerRow> LayerRows =>
        CurrentUnit?.Layers.Select(l => new LayerRow(l.Index, l.Pattern, l.Count, l.Z, l.Height,
            l.ArticleId is { } id ? _main.Db.FindArticle(id)?.Code ?? "" : "Mixte", l.SlipSheetBelow)).Reverse().ToList() ?? [];

    // ------------------------------------------------------------------ Vues 3D / 2D

    [ObservableProperty] private Model3DGroup? _scene3D;
    [ObservableProperty] private string? _hoverText;
    [ObservableProperty] private int _maxLayers;
    [ObservableProperty] private int _visibleLayers;
    [ObservableProperty] private int _planLayer = 1;
    [ObservableProperty] private IReadOnlyDictionary<Guid, Color> _colorMap = new Dictionary<Guid, Color>();

    public event Action? SceneReplaced;

    partial void OnVisibleLayersChanged(int value) => RebuildScene();

    [RelayCommand]
    private void LayerUp() => PlanLayer = Math.Min(MaxLayers, PlanLayer + 1);

    [RelayCommand]
    private void LayerDown() => PlanLayer = Math.Max(1, PlanLayer - 1);

    /// <summary>
    /// Couleurs des articles dans les vues : par défaut bien distinctes, attribuées dans l'ordre des lignes du
    /// conditionnement affiché ; « Couleur d'origine » : couleurs des fiches articles.
    /// </summary>
    public void BuildColorMap()
    {
        var map = new Dictionary<Guid, Color>();
        if (UseArticleColors)
        {
            var i = 0;
            foreach (var a in _main.Db.Articles)
            {
                map[a.Id] = ArticleColors.Parse(a.Color, ArticleColors.ByIndex(i++));
            }
        }
        else
        {
            var k = 0;
            var order = Kind == PackagingKind.Homogene ? [Article] : Lines.Select(l => l.Article);
            foreach (var a in order)
            {
                if (a != null && !map.ContainsKey(a.Id))
                {
                    map[a.Id] = ArticleColors.DistinctByIndex(k++);
                }
            }

            foreach (var a in _main.Db.Articles)
            {
                map.TryAdd(a.Id, ArticleColors.DistinctByIndex(k++));
            }
        }

        ColorMap = map;
        foreach (var line in Lines)
        {
            line.ViewBrush = line.Article != null && map.TryGetValue(line.Article.Id, out var c) ? new SolidColorBrush(c) : null;
        }
    }

    private void RebuildScene()
    {
        _hovered = null;
        HoverText = null;
        if (CurrentSolution is not { } s || CurrentUnit is not { } u)
        {
            _scene = null;
            Scene3D = null;
            return;
        }

        var map = ColorMap;
        _scene = Scene3DBuilder.Build(s, u, Constraints, id => map.TryGetValue(id, out var c) ? c : Colors.SteelBlue, VisibleLayers);
        Scene3D = _scene.Root;
        SceneSize = (_scene.SizeX, _scene.SizeY, _scene.SizeZ);
        SceneReplaced?.Invoke();
    }

    public (double X, double Y, double Z) SceneSize { get; private set; } = (1.2, 0.8, 1.5);

    public void HoverFromModel(GeometryModel3D? model)
    {
        if (ReferenceEquals(model, _hovered))
        {
            return;
        }

        if (_hovered != null && _scene != null && _scene.ByModel.TryGetValue(_hovered, out var previous))
        {
            var color = ColorMap.TryGetValue(previous.ArticleId, out var c) ? c : Colors.SteelBlue;
            _hovered.Material = Scene3DBuilder.Solid(Scene3DBuilder.ItemColor(previous, color));
            _hovered.BackMaterial = _hovered.Material;
        }

        _hovered = null;
        if (model == null || _scene == null || !_scene.ByModel.TryGetValue(model, out var p))
        {
            HoverText = null;
            return;
        }

        _hovered = model;
        model.Material = Scene3DBuilder.Hover;
        model.BackMaterial = Scene3DBuilder.Hover;
        var a = _main.Db.FindArticle(p.ArticleId);
        HoverText = $"{a?.DisplayName}\n{a?.KindLabel} · {a?.DimensionsText}\n" +
                    $"Couche {p.Layer} · pose n° {p.Sequence}\n" +
                    $"Position X {p.X:0} · Y {p.Y:0} · Z {p.Z:0} mm\n" +
                    $"Enveloppe {p.DX:0} × {p.DY:0} × {p.DZ:0} mm · {p.Weight.ToString(Formats.UnitWeight, Fr)} kg";
    }

    // ------------------------------------------------------------------ Export / impression

    [RelayCommand]
    private void Print()
    {
        if (CurrentSolution is not { } s)
        {
            _main.ShowToast("Calculez puis choisissez une solution à imprimer.", "Warning");
            return;
        }

        var p = BuildPackaging();
        PrintService.PrintSheet(p, s, CurrentUnit ?? s.FirstUnit!, _main.Db, ColorMap);
    }

    /// <summary>Fiche palette : une solution est affichée.</summary>
    public bool CanPrintPalletSheet => CurrentSolution != null;
}

public sealed record LayerRow(int Index, string Pattern, int Count, double Z, double Height, string Article, bool SlipSheet);

/// <summary>Ligne de l'ordre de pose à suivre par le préparateur.</summary>
public sealed record PoseRow(int Sequence, string Article, string Designation, int Layer, double X, double Y, double Z, string Size, double Weight, string Zone);
