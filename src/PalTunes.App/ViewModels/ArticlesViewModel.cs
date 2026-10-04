using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.App.Services;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.App.ViewModels;

/// <summary>Nœud de l'arborescence articles : groupe (client, famille…) ou article.</summary>
public sealed partial class TreeNode : ObservableObject
{
    public string Header { get; init; } = "";
    public string Glyph { get; init; } = "";
    public Article? Article { get; init; }
    public ObservableCollection<TreeNode> Children { get; } = [];
    public int Count { get; set; }
    public string? Detail { get; init; }
    public string? Color { get; init; }
    public bool IsGroup => Article == null;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;
}

public sealed record GroupingChoice(string Key, string Label);

public sealed partial class ArticlesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ArticlesViewModel(MainViewModel main)
    {
        _main = main;
        _grouping = Groupings.FirstOrDefault(g => g.Key == main.Settings.Current.TreeGrouping) ?? Groupings[0];
        Editor = new ArticleEditor();
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(ArticleEditor.Errors) or nameof(ArticleEditor.IsDirty)))
            {
                RefreshPreview();
            }
        };
        SelectedArticle = main.Db.Articles.OrderBy(a => a.Client).ThenBy(a => a.Code).FirstOrDefault();
        if (SelectedArticle == null)
        {
            Editor.Load(new Article(), isNew: true);
        }

        RebuildTree();
    }

    public IReadOnlyList<GroupingChoice> Groupings { get; } =
    [
        new("Client", "Client › Famille › Sous-famille"),
        new("Family", "Famille › Type"),
        new("Kind", "Type › Client"),
        new("Flat", "Liste (sans regroupement)")
    ];

    public IReadOnlyList<ArticleKind> Kinds => ArticleSchema.Kinds;

    public ObservableCollection<TreeNode> Roots { get; } = [];
    public ArticleEditor Editor { get; }

    [ObservableProperty] private GroupingChoice _grouping;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private Article? _selectedArticle;
    [ObservableProperty] private Model3DGroup? _preview;
    [ObservableProperty] private string _countText = "";

    partial void OnGroupingChanged(GroupingChoice value)
    {
        _main.Settings.Current.TreeGrouping = value.Key;
        _main.Settings.Save();
        RebuildTree();
    }

    partial void OnSearchTextChanged(string value) => RebuildTree();

    partial void OnSelectedArticleChanged(Article? value)
    {
        if (value != null)
        {
            Editor.Load(value, isNew: false);
        }
    }

    public void SelectNode(TreeNode? node)
    {
        if (node?.Article is { } a)
        {
            SelectedArticle = a;
        }
    }

    private static string Group(string? v) => string.IsNullOrWhiteSpace(v) ? "(Non renseigné)" : v.Trim();

    private IReadOnlyList<string> Distinct(Func<Article, string?> f) =>
        _main.Db.Articles.Select(f).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Clients proposés : base clients, plus les noms encore portés par des articles.</summary>
    public IReadOnlyList<string> Clients => _main.Db.Clients.Select(c => c.Name).Concat(Distinct(a => a.Client))
        .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase).ToList();
    public IReadOnlyList<string> Families => Distinct(a => a.Family);
    public IReadOnlyList<string> SubFamilies => Distinct(a => a.SubFamily);

    public void RebuildTree()
    {
        OnPropertyChanged(nameof(Clients));
        OnPropertyChanged(nameof(Families));
        OnPropertyChanged(nameof(SubFamilies));
        var selectedId = SelectedArticle?.Id;
        var expanded = new HashSet<string>(Flatten(Roots).Where(n => n.IsGroup && n.IsExpanded).Select(n => n.Header));
        var firstBuild = Roots.Count == 0;
        Roots.Clear();
        var query = SearchText.Trim();
        var articles = _main.Db.Articles
            .Where(a => query.Length == 0 || Matches(a, query))
            .OrderBy(a => a.Code, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        CountText = articles.Count == _main.Db.Articles.Count
            ? $"{articles.Count} article(s)"
            : $"{articles.Count} / {_main.Db.Articles.Count} article(s)";

        Func<Article, string>[] levels = Grouping.Key switch
        {
            "Client" => [a => Group(a.Client), a => Group(a.Family), a => Group(a.SubFamily)],
            "Family" => [a => Group(a.Family), a => ArticleSchema.KindLabel(a.Kind)],
            "Kind" => [a => ArticleSchema.KindLabel(a.Kind), a => Group(a.Client)],
            _ => []
        };
        string[] glyphs = Grouping.Key switch
        {
            "Client" => ["", "", ""],
            "Family" => ["", ""],
            "Kind" => ["", ""],
            _ => []
        };

        void Build(ObservableCollection<TreeNode> target, IEnumerable<Article> items, int level, string path)
        {
            if (level >= levels.Length)
            {
                foreach (var a in items)
                {
                    target.Add(new TreeNode
                    {
                        Header = a.Code,
                        Detail = $"{a.Designation}{(string.IsNullOrWhiteSpace(a.Designation) ? "" : " · ")}{a.DimensionsText}",
                        Glyph = ArticleSchema.KindGlyph(a.Kind),
                        Article = a,
                        Color = a.Color,
                        IsSelected = a.Id == selectedId
                    });
                }

                return;
            }

            foreach (var g in items.GroupBy(levels[level]).OrderBy(g => g.Key == "(Non renseigné)" ? 1 : 0).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var key = path + "/" + g.Key;
                var node = new TreeNode
                {
                    Header = g.Key,
                    Glyph = glyphs[level],
                    Count = g.Count(),
                    Detail = $"{g.Count()}",
                    IsExpanded = query.Length > 0 || expanded.Contains(g.Key) || (firstBuild && level == 0) || g.Any(a => a.Id == selectedId)
                };
                Build(node.Children, g, level + 1, key);
                target.Add(node);
            }
        }

        Build(Roots, articles, 0, "");
    }

    private static IEnumerable<TreeNode> Flatten(IEnumerable<TreeNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private static bool Matches(Article a, string q)
    {
        var fields = new[] { a.Code, a.Designation, a.Client, a.Family, a.SubFamily, a.CustomerRef, a.Ean, a.Notes, a.KindLabel, a.DimensionsText };
        return fields.Any(f => f?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true);
    }

    private void RefreshPreview()
    {
        var a = new Article();
        Editor.ApplyTo(a);
        var color = ArticleColors.Parse(a.Color, Color.FromRgb(0x5D, 0xAD, 0xE2));
        Preview = Scene3DBuilder.BuildArticle(a, color).Root;
    }

    [RelayCommand]
    private void New(ArticleKind kind)
    {
        var a = new Article
        {
            Kind = kind,
            CoilAxis = ArticleSchema.DefaultAxis(kind),
            Client = SelectedArticle?.Client,
            Family = SelectedArticle?.Family,
            SubFamily = SelectedArticle?.SubFamily
        };
        SelectedArticle = null;
        Editor.Load(a, isNew: true);
    }

    /// <summary>Nouvel article rattaché à un client (depuis la fiche client).</summary>
    public void NewForClient(string client)
    {
        SelectedArticle = null;
        Editor.Load(new Article { Kind = ArticleKind.Caisse, Client = client }, isNew: true);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedArticle == null)
        {
            return;
        }

        var copy = SelectedArticle.Clone();
        copy.Id = Guid.NewGuid();
        copy.Code = UniqueCode(SelectedArticle.Code + "-COPIE");
        copy.CreatedAt = copy.ModifiedAt = DateTime.Now;
        SelectedArticle = null;
        Editor.Load(copy, isNew: true);
    }

    private string UniqueCode(string code)
    {
        var candidate = code;
        for (var i = 2; _main.Db.Articles.Any(a => string.Equals(a.Code, candidate, StringComparison.OrdinalIgnoreCase)); i++)
        {
            candidate = $"{code}{i}";
        }

        return candidate;
    }

    [RelayCommand]
    private void Delete()
    {
        var a = SelectedArticle;
        if (a == null)
        {
            return;
        }

        var used = _main.Db.Packagings.Count(p => p.ArticleId == a.Id || p.Lines.Any(l => l.ArticleId == a.Id));
        var message = used > 0
            ? $"L'article {a.Code} est utilisé par {used} conditionnement(s). Le supprimer quand même ?"
            : $"Supprimer l'article {a.Code} ?";
        if (!_main.Dialogs.Confirm(message))
        {
            return;
        }

        _main.Db.Articles.Remove(a);
        SelectedArticle = null;
        Editor.Load(new Article(), isNew: true);
        _main.SaveDatabase();
        RebuildTree();
        _main.NotifyArticlesChanged();
        _main.ShowToast($"Article {a.Code} supprimé.");
    }

    [RelayCommand]
    private void Save()
    {
        var draft = Editor.Original?.Clone() ?? new Article();
        Editor.ApplyTo(draft);
        var errors = ArticleSchema.Validate(draft);
        if (_main.Db.Articles.Any(a => a.Id != draft.Id && string.Equals(a.Code.Trim(), draft.Code.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            errors.Insert(0, $"Le code {draft.Code} existe déjà.");
        }

        Editor.Errors = errors;
        if (errors.Count > 0)
        {
            _main.ShowToast("Article non enregistré : " + errors[0], "Warning");
            return;
        }

        draft.ModifiedAt = DateTime.Now;
        var index = _main.Db.Articles.FindIndex(a => a.Id == draft.Id);
        if (index >= 0)
        {
            _main.Db.Articles[index] = draft;
        }
        else
        {
            _main.Db.Articles.Add(draft);
        }

        var newClients = _main.Db.MergeClients();
        _main.SaveDatabase();
        SelectedArticle = draft;
        RebuildTree();
        _main.NotifyArticlesChanged();
        if (newClients > 0)
        {
            _main.NotifyClientsChanged();
        }

        _main.ShowToast($"Article {draft.Code} enregistré.", "Ok");
    }

    [RelayCommand]
    private void Cancel()
    {
        if (SelectedArticle != null)
        {
            Editor.Load(SelectedArticle, isNew: false);
        }
        else if (Editor.Original != null)
        {
            Editor.Load(Editor.Original, isNew: true);
        }
    }

    [RelayCommand]
    private void ExpandAll() => SetExpanded(true);

    [RelayCommand]
    private void CollapseAll() => SetExpanded(false);

    private void SetExpanded(bool value)
    {
        foreach (var n in Flatten(Roots).Where(n => n.IsGroup))
        {
            n.IsExpanded = value;
        }
    }

    /// <summary>Crée un conditionnement homogène pour l'article affiché et ouvre la section Conditionnements.</summary>
    [RelayCommand]
    private void Palletize()
    {
        if (SelectedArticle is { } a)
        {
            _main.Packagings.CreateFor(a);
        }
    }
}

/// <summary>Formulaire d'un article : champs affichés et libellés selon le type (données minimales, étude §2).</summary>
public sealed partial class ArticleEditor : ObservableObject
{
    public Article? Original { get; private set; }

    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private IReadOnlyList<string> _errors = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Fields), nameof(KindDescription), nameof(UsesOrientation), nameof(UsesCoilAxis),
        nameof(ShowLength), nameof(ShowWidth), nameof(ShowHeight), nameof(ShowDiameter), nameof(ShowInnerDiameter),
        nameof(LengthLabel), nameof(WidthLabel), nameof(HeightLabel), nameof(DiameterLabel), nameof(InnerDiameterLabel), nameof(Title))]
    private ArticleKind _kind;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private string _code = "";
    [ObservableProperty] private string? _designation;
    [ObservableProperty] private double _length;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private double _diameter;
    [ObservableProperty] private double _innerDiameter;
    [ObservableProperty] private double _weight;
    [ObservableProperty] private OrientationRule _orientation;
    [ObservableProperty] private CoilAxis _coilAxis;
    [ObservableProperty] private double? _maxLoadOnTop;
    [ObservableProperty] private int? _maxLayers;
    [ObservableProperty] private bool _fragile;
    [ObservableProperty] private string? _client;
    [ObservableProperty] private string? _family;
    [ObservableProperty] private string? _subFamily;
    [ObservableProperty] private string? _customerRef;
    [ObservableProperty] private string? _ean;
    [ObservableProperty] private string? _color;
    [ObservableProperty] private string? _notes;

    public IReadOnlyList<string> Swatches => ArticleColors.Swatches;
    public IReadOnlyList<OrientationRule> Orientations { get; } = Enum.GetValues<OrientationRule>();
    public IReadOnlyList<CoilAxis> Axes { get; } = Enum.GetValues<CoilAxis>();

    /// <summary>Profil de gerbage déduit des données saisies (étude hétérogène §3), en lecture seule.</summary>
    public string ProfileText
    {
        get
        {
            var a = new Article();
            ApplyTo(a);
            if (ArticleSchema.Validate(a).Any(e => !e.StartsWith("Le code", StringComparison.Ordinal)))
            {
                return "Complétez les données de calcul pour voir le profil.";
            }

            var p = StackingProfile.For(a);
            return $"Classe : {p.ClassLabel} · zone conseillée : {p.ZoneLabel} · densité {p.Density.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} kg/dm³\n" +
                   $"Peut porter : {p.CapacityText}";
        }
    }

    public string Title => IsNew ? (string.IsNullOrWhiteSpace(Code) ? $"Nouvel article · {ArticleSchema.KindLabel(Kind)}" : $"Nouvel article {Code}") : Code;
    public IReadOnlyList<FieldSpec> Fields => ArticleSchema.Fields(Kind);
    public string KindDescription => ArticleSchema.KindDescription(Kind);
    public bool UsesOrientation => ArticleSchema.UsesOrientation(Kind);
    public bool UsesCoilAxis => ArticleSchema.UsesCoilAxis(Kind);

    private FieldSpec? Spec(ArticleField f) => Fields.FirstOrDefault(s => s.Field == f);
    private string Label(ArticleField f) => Spec(f) is { } s ? s.Label + (s.Required ? " *" : "") : "";

    public bool ShowLength => Spec(ArticleField.Length) != null;
    public bool ShowWidth => Spec(ArticleField.Width) != null;
    public bool ShowHeight => Spec(ArticleField.Height) != null;
    public bool ShowDiameter => Spec(ArticleField.Diameter) != null;
    public bool ShowInnerDiameter => Spec(ArticleField.InnerDiameter) != null;
    public string LengthLabel => Label(ArticleField.Length);
    public string WidthLabel => Label(ArticleField.Width);
    public string HeightLabel => Label(ArticleField.Height);
    public string DiameterLabel => Label(ArticleField.Diameter);
    public string InnerDiameterLabel => Label(ArticleField.InnerDiameter);

    private bool _loading;

    partial void OnKindChanged(ArticleKind oldValue, ArticleKind newValue)
    {
        if (!_loading && ArticleSchema.UsesCoilAxis(newValue) && !ArticleSchema.UsesCoilAxis(oldValue))
        {
            CoilAxis = ArticleSchema.DefaultAxis(newValue);
        }
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(ProfileText) or nameof(IsDirty) or nameof(Errors) or nameof(IsNew) or nameof(Title)))
        {
            OnPropertyChanged(nameof(ProfileText));
        }

        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(Errors) or nameof(IsNew) or nameof(ProfileText)))
        {
            // Modifié = différent de l'article d'origine (les listes éditables réécrivent leur texte sans rien changer).
            var current = Original?.Clone() ?? new Article();
            ApplyTo(current);
            IsDirty = IsNew || Original == null || Snapshot(current) != Snapshot(Original);
        }
    }

    [RelayCommand]
    private void PickColor(string hex) => Color = hex;

    public void Load(Article a, bool isNew)
    {
        _loading = true;
        Original = a;
        IsNew = isNew;
        Kind = a.Kind;
        Code = a.Code;
        Designation = a.Designation;
        Length = a.Length;
        Width = a.Width;
        Height = a.Height;
        Diameter = a.Diameter;
        InnerDiameter = a.InnerDiameter;
        Weight = a.Weight;
        Orientation = a.Orientation;
        CoilAxis = a.CoilAxis;
        MaxLoadOnTop = a.MaxLoadOnTop;
        MaxLayers = a.MaxLayers;
        Fragile = a.Fragile;
        Client = a.Client;
        Family = a.Family;
        SubFamily = a.SubFamily;
        CustomerRef = a.CustomerRef;
        Ean = a.Ean;
        Color = a.Color;
        Notes = a.Notes;
        Errors = [];
        _loading = false;
        IsDirty = false;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Length));
    }

    private static string Snapshot(Article a)
    {
        var copy = a.Clone();
        copy.CreatedAt = copy.ModifiedAt = default;
        copy.Designation = Clean(copy.Designation);
        copy.Client = Clean(copy.Client);
        copy.Family = Clean(copy.Family);
        copy.SubFamily = Clean(copy.SubFamily);
        copy.CustomerRef = Clean(copy.CustomerRef);
        copy.Ean = Clean(copy.Ean);
        copy.Color = Clean(copy.Color);
        copy.Notes = Clean(copy.Notes);
        return System.Text.Json.JsonSerializer.Serialize(copy);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public void ApplyTo(Article a)
    {
        a.Kind = Kind;
        a.Code = (Code ?? "").Trim();
        a.Designation = Clean(Designation);
        // Seules les dimensions du type sont conservées (les autres sont remises à zéro).
        a.Length = ShowLength ? Length : 0;
        a.Width = ShowWidth ? Width : 0;
        a.Height = ShowHeight ? Height : 0;
        a.Diameter = ShowDiameter ? Diameter : 0;
        a.InnerDiameter = ShowInnerDiameter ? InnerDiameter : 0;
        a.Weight = Weight;
        a.Orientation = Orientation;
        a.CoilAxis = CoilAxis;
        a.MaxLoadOnTop = MaxLoadOnTop;
        a.MaxLayers = MaxLayers;
        a.Fragile = Fragile;
        a.Client = Clean(Client);
        a.Family = Clean(Family);
        a.SubFamily = Clean(SubFamily);
        a.CustomerRef = Clean(CustomerRef);
        a.Ean = Clean(Ean);
        a.Color = Clean(Color);
        a.Notes = Clean(Notes);
    }
}
