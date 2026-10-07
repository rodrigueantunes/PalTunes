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

    /// <summary>Conditionnement (espace « Gestion des conditionnements »).</summary>
    public Packaging? Packaging { get; init; }

    /// <summary>Texte secondaire d'une feuille (désignation de l'article, nom du conditionnement).</summary>
    public string? Subtitle { get; init; }

    public ObservableCollection<TreeNode> Children { get; } = [];
    public int Count { get; set; }
    public string? Detail { get; init; }
    public string? Color { get; init; }
    public bool IsGroup => Article == null && Packaging == null;

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
        Editor = new ArticleEditor { FindArticle = id => _main.Db.FindArticle(id) };
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

    /// <summary>Poids unitaire impossible pour les dimensions saisies (erreur de saisie ou d'unité).</summary>
    [ObservableProperty] private string? _weightWarning;
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

        RefreshLinks();
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

    /// <summary>
    /// Clients proposés à la fiche article : « Aucun client », la base clients (« CODE - Nom »), plus les codes encore
    /// portés par des articles sans fiche client. La valeur retenue est le code.
    /// </summary>
    public IReadOnlyList<ClientChoice> Clients
    {
        get
        {
            var db = _main.Db;
            var list = new List<ClientChoice> { new("", "(Aucun client)") };
            list.AddRange(db.Clients.OrderBy(c => c.Code, StringComparer.CurrentCultureIgnoreCase).Select(c => new ClientChoice(c.Code, c.Label)));
            list.AddRange(Distinct(a => a.Client).Where(code => db.FindClient(code) == null).Select(code => new ClientChoice(code, code + " (client inconnu)")));
            if (Editor.Client is { Length: > 0 } current && list.All(c => c.Code != current))
            {
                list.Add(new ClientChoice(current, db.ClientLabel(current)));
            }

            return list;
        }
    }

    private string ClientGroup(Article a) => Group(_main.Db.ClientLabel(a.Client));
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
            "Client" => [ClientGroup, a => Group(a.Family), a => Group(a.SubFamily)],
            "Family" => [a => Group(a.Family), a => ArticleSchema.KindLabel(a.Kind)],
            "Kind" => [a => ArticleSchema.KindLabel(a.Kind), ClientGroup],
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
                        Subtitle = a.Designation,
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

    private bool Matches(Article a, string q)
    {
        var fields = new[] { a.Code, a.Designation, _main.Db.ClientLabel(a.Client), a.Family, a.SubFamily, a.CustomerRef, a.Ean, a.Notes, a.KindLabel, a.DimensionsText };
        return fields.Any(f => f?.Contains(q, StringComparison.CurrentCultureIgnoreCase) == true);
    }

    private void RefreshPreview()
    {
        var a = new Article();
        Editor.ApplyTo(a);
        var color = ArticleColors.Parse(a.Color, Color.FromRgb(0x5D, 0xAD, 0xE2));
        Preview = Scene3DBuilder.BuildArticle(a, color).Root;
        WeightWarning = ArticleSchema.Warnings(a);
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
        // Un code article est unique pour un client (le même code peut exister chez un autre client).
        if (_main.Db.Articles.Any(a => a.Id != draft.Id && string.Equals(a.Code.Trim(), draft.Code.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(a.Client?.Trim() ?? "", draft.Client?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Insert(0, string.IsNullOrWhiteSpace(draft.Client)
                ? $"Le code {draft.Code} existe déjà (sans client)."
                : $"Le code {draft.Code} existe déjà pour le client {_main.Db.ClientLabel(draft.Client)}.");
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
        RefreshLinks();
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

    // ------------------------------------------------------------------ Colisages et conditionnements de l'article

    /// <summary>
    /// Colisages de l'article affiché : la caisse elle-même (article caisse créé au colisage), ou les caisses créées pour
    /// ce produit, seul ou dans une caisse mixte.
    /// </summary>
    public ObservableCollection<ColisageLink> Colisages { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilteredColisages))] private string _colisageFilter = "";
    [ObservableProperty] private bool _isColisagePickerOpen;
    [ObservableProperty] private int _packagingCount;

    public bool HasColisages => Colisages.Count > 0;
    public string ColisageButtonText => Colisages.Count > 1 ? $"Voir les colisages ({Colisages.Count})" : "Voir le colisage";
    public string ColisagePickerTitle => SelectedArticle is { } a ? $"Colisages de {a.Code}" : "Colisages";

    public IReadOnlyList<ColisageLink> FilteredColisages
    {
        get
        {
            var q = ColisageFilter.Trim();
            return q.Length == 0 ? Colisages : Colisages.Where(c => c.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
    }

    public bool HasPackagings => PackagingCount > 0;
    public string PackagingsButtonText => PackagingCount > 1 ? $"Ses conditionnements ({PackagingCount})" : "Son conditionnement";

    partial void OnPackagingCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasPackagings));
        OnPropertyChanged(nameof(PackagingsButtonText));
    }

    /// <summary>Recalcule les colisages et le nombre de conditionnements de l'article affiché (boutons visibles seulement s'il y en a).</summary>
    public void RefreshLinks()
    {
        Colisages.Clear();
        IsColisagePickerOpen = false;
        var db = _main.Db;
        if (SelectedArticle is { } a && db.FindArticle(a.Id) != null)
        {
            Article? Find(Guid id) => db.FindArticle(id);
            string Describe(Article box)
            {
                var link = box.CaseContent!;
                var content = link.IsMixed
                    ? "caisse mixte : " + string.Join(" + ", link.Lines!.Select(l => $"{l.Quantity} × {Find(l.ArticleId)?.Code}"))
                    : $"{box.CaseQuantity} × {Find(link.ArticleId)?.Code}";
                return $"{box.Code} · {content}{(link.CaseTypeCode is { } c ? " · " + c : "")}";
            }

            var boxes = new List<Article>();
            if (CaseEngine.CanRebuild(a, Find))
            {
                boxes.Add(a);
            }

            boxes.AddRange(db.Articles.Where(b => b.Id != a.Id && b is { Kind: ArticleKind.Caisse, CaseContent: { } link } &&
                                                  (link.ArticleId == a.Id || link.Lines?.Any(l => l.ArticleId == a.Id) == true) &&
                                                  CaseEngine.CanRebuild(b, Find))
                .OrderBy(b => b.Code));
            foreach (var box in boxes)
            {
                Colisages.Add(new ColisageLink(box, Describe(box)));
            }

            // Conditionnements de l'article et des caisses qui le contiennent (comme le filtre de la gestion).
            var ids = boxes.Select(b => b.Id).Append(a.Id).ToHashSet();
            PackagingCount = db.Packagings.Count(p => (p.ArticleId is { } id && ids.Contains(id)) || p.Lines.Any(l => ids.Contains(l.ArticleId)));
        }
        else
        {
            PackagingCount = 0;
        }

        ColisageFilter = "";
        OnPropertyChanged(nameof(HasColisages));
        OnPropertyChanged(nameof(ColisageButtonText));
        OnPropertyChanged(nameof(ColisagePickerTitle));
        OnPropertyChanged(nameof(FilteredColisages));
    }

    /// <summary>Un seul colisage : ouvert directement ; plusieurs : petite liste filtrable.</summary>
    [RelayCommand]
    private void OpenColisage()
    {
        if (Colisages.Count == 1)
        {
            _main.OpenColisage(Colisages[0].Box);
        }
        else if (Colisages.Count > 1)
        {
            ColisageFilter = "";
            IsColisagePickerOpen = true;
        }
    }

    [RelayCommand]
    private void OpenColisageLink(ColisageLink? link)
    {
        if ((link ?? FilteredColisages.FirstOrDefault()) is { } target)
        {
            IsColisagePickerOpen = false;
            _main.OpenColisage(target.Box);
        }
    }

    /// <summary>Gestion des conditionnements filtrée sur l'article (palettes de l'article ou des caisses qui le contiennent).</summary>
    [RelayCommand]
    private void ShowPackagings()
    {
        if (SelectedArticle is { } a)
        {
            _main.ShowPackagingsFor(a.Code);
        }
    }

    /// <summary>Espace Colisage sur ce produit (mise en caisse).</summary>
    [RelayCommand]
    private void PackInCase()
    {
        if (SelectedArticle is { } a)
        {
            _main.Cases.OpenForProduct(a);
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
        nameof(ShowLength), nameof(ShowWidth), nameof(ShowHeight), nameof(ShowDiameter), nameof(ShowInnerDiameter), nameof(ShowFoldedLength), nameof(ShowFoldedWidth), nameof(ShowFoldedHeight),
        nameof(LengthLabel), nameof(WidthLabel), nameof(HeightLabel), nameof(DiameterLabel), nameof(InnerDiameterLabel), nameof(FoldedLengthLabel), nameof(FoldedWidthLabel), nameof(FoldedHeightLabel), nameof(Title), nameof(ShowQuantityPerCase), nameof(ShowTopShape), nameof(ShowCompression), nameof(NoHandleText))]
    private ArticleKind _kind;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private string _code = "";
    [ObservableProperty] private string? _designation;
    [ObservableProperty] private double _length;
    [ObservableProperty] private double _width;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CompressionText))] private double _height;
    [ObservableProperty] private double _diameter;
    [ObservableProperty] private double _innerDiameter;
    [ObservableProperty] private double _foldedLength;
    [ObservableProperty] private double _foldedWidth;
    [ObservableProperty] private double _foldedHeight;

    /// <summary>Caisse / carton : nombre de produits contenus (facultatif).</summary>
    [ObservableProperty] private int? _quantityPerCase;

    [ObservableProperty] private double _weight;
    [ObservableProperty] private OrientationRule _orientation;
    [ObservableProperty] private CoilAxis _coilAxis;
    [ObservableProperty] private double? _maxLoadOnTop;
    [ObservableProperty] private int? _maxLayers;
    [ObservableProperty] private bool _fragile;

    /// <summary>Bidon, seau, bouteille : forme du dessus (null = non renseignée).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTopShape), nameof(TopAngleText))] private TopShape? _topShape;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TopAngleValue), nameof(TopAngleText))] private double? _topAngle;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasHandle))] private HandleKind? _handle;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HandleRoundedChoice))] private bool? _handleRounded;
    /// <summary>Dimensions de la poignée (mm), au plus celles du produit.</summary>
    [ObservableProperty] private double? _handleLength;

    [ObservableProperty] private double? _handleWidth;
    [ObservableProperty] private double? _handleHeight;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HandleAngleLeftValue), nameof(HandleAngleLeftText))] private double? _handleAngleLeft;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HandleAngleRightValue), nameof(HandleAngleRightText))] private double? _handleAngleRight;

    /// <summary>Curseurs des côtés de la poignée arrondie (0 à 80°), comme l'angle au bord du dessus.</summary>
    public double HandleAngleLeftValue
    {
        get => HandleAngleLeft ?? 0;
        set => HandleAngleLeft = Math.Round(Math.Clamp(value, 0, ArticleSchema.MaxHandleSideAngle), 0);
    }

    public double HandleAngleRightValue
    {
        get => HandleAngleRight ?? 0;
        set => HandleAngleRight = Math.Round(Math.Clamp(value, 0, ArticleSchema.MaxHandleSideAngle), 0);
    }

    public string HandleAngleLeftText => $"Angle au bord gauche : {HandleAngleLeftValue:0}°";
    public string HandleAngleRightText => $"Angle au bord droit : {HandleAngleRightValue:0}°";

    /// <summary>Dimensions maxi de la poignée : celles du produit (diamètre pour un fût, un seau, une bouteille).</summary>
    private (double L, double W, double H) HandleMax
    {
        get
        {
            var a = new Article { Kind = Kind, Length = Length, Width = Width, Height = Height, Diameter = Diameter };
            return a.HandleReference;
        }
    }

    private static string Max(double v) => v > 0 ? $" ≤ {v:0.#}" : "";
    public string HandleLengthLabel => $"Longueur (mm{Max(HandleMax.L)})";
    public string HandleWidthLabel => $"Largeur (mm{Max(HandleMax.W)})";
    public string HandleHeightLabel => $"Hauteur (mm{Max(HandleMax.H)})";

    /// <summary>Poignée pleine, liée au corps (décochée par défaut).</summary>
    [ObservableProperty] private bool _handleSolid;

    /// <summary>Sac : tassement accepté à la mise en caisse (décoché par défaut).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CompressionText))] private bool _compressible;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CompressionText))] private double? _compressionPercent;
    [ObservableProperty] private string? _client;
    [ObservableProperty] private string? _family;
    [ObservableProperty] private string? _subFamily;
    [ObservableProperty] private string? _customerRef;
    [ObservableProperty] private string? _ean;
    [ObservableProperty] private string? _color;
    [ObservableProperty] private string? _notes;

    public IReadOnlyList<string> Swatches => ArticleColors.Swatches;

    /// <summary>Recherche d'un article de la base (contenu d'une caisse créée au colisage).</summary>
    public Func<Guid, Article?>? FindArticle { get; init; }

    /// <summary>Caisse créée au colisage : « Contient 24 × ART-001 – … · caisse CAR-01 » ; vide sinon.</summary>
    [ObservableProperty] private string? _caseContentText;

    public bool ShowQuantityPerCase => Kind == ArticleKind.Caisse;

    // ------------------------------------------------------------------ Forme du dessus (bidon, seau, bouteille)

    public bool ShowTopShape => TopStacking.Applies(Kind);
    public bool HasTopShape => TopShape != null;

    public sealed record Choice<T>(T Value, string Label) where T : struct;

    public IReadOnlyList<Choice<TopShape>> TopShapes { get; } =
    [
        new(Core.Models.TopShape.Droit, "Droit"),
        new(Core.Models.TopShape.Arrondi, "Arrondi (bombé)")
    ];

    public IReadOnlyList<Choice<HandleKind>> Handles { get; } =
    [
        new(HandleKind.Encastree, "Encastrée"),
        new(HandleKind.Saillante, "Saillante"),
        new(HandleKind.Rabattable, "Anse rabattable")
    ];

    public IReadOnlyList<Choice<bool>> HandleShapes { get; } = [new(false, "Droite (dessus plat)"), new(true, "Arrondie")];

    public bool HandleRoundedChoice
    {
        get => HandleRounded == true;
        set => HandleRounded = value;
    }

    /// <summary>Poignée présente (encastrée, saillante ou anse).</summary>
    public bool HasHandle => Handle is { } h && h != HandleKind.Aucune;

    public string NoHandleText => Handle == HandleKind.Aucune ? "Sans poignée." : "Poignée non décrite : proportions usuelles du type.";

    [RelayCommand]
    private void AddHandle()
    {
        Handle = Kind switch
        {
            ArticleKind.Seau => HandleKind.Rabattable,
            ArticleKind.Bidon => HandleKind.Encastree,
            _ => HandleKind.Saillante
        };
    }

    [RelayCommand]
    private void RemoveHandle()
    {
        Handle = HandleKind.Aucune;
        HandleRounded = null;
        HandleLength = null;
        HandleWidth = null;
        HandleHeight = null;
        HandleAngleLeft = null;
        HandleAngleRight = null;
        HandleSolid = false;
        OnPropertyChanged(nameof(NoHandleText));
    }

    [RelayCommand]
    private void ClearAllTop()
    {
        ClearTopShape();
        Handle = null;
        HandleRounded = null;
        HandleLength = null;
        HandleWidth = null;
        HandleHeight = null;
        HandleAngleLeft = null;
        HandleAngleRight = null;
        HandleSolid = false;
        OnPropertyChanged(nameof(NoHandleText));
    }

    // ------------------------------------------------------------------ Sac : tassement à la mise en caisse

    public bool ShowCompression => Kind == ArticleKind.Sac;

    partial void OnCompressibleChanged(bool value)
    {
        if (value && CompressionPercent == null && !_loading)
        {
            CompressionPercent = ArticleSchema.DefaultCompressionPercent;
        }
    }

    public string CompressionText
    {
        get
        {
            if (!Compressible)
            {
                return "Décoché : l'épaisseur saisie est conservée en caisse.";
            }

            var pct = Math.Clamp(CompressionPercent ?? ArticleSchema.DefaultCompressionPercent, 0, ArticleSchema.MaxCompressionPercent);
            return Height > 0
                ? $"En caisse : épaisseur {Height:0.#} → {Height * (1 - pct / 100):0.#} mm (−{pct:0.#} %), empreinte inchangée. Palettes : épaisseur saisie."
                : $"En caisse : épaisseur réduite de {pct:0.#} %, empreinte inchangée.";
        }
    }

    /// <summary>Curseur de l'angle (0 à 60°).</summary>
    public double TopAngleValue
    {
        get => TopAngle ?? 0;
        set => TopAngle = Math.Round(Math.Clamp(value, 0, 90), 0);
    }

    public string TopAngleText => TopShape == Core.Models.TopShape.Arrondi ? $"Angle au bord : {TopAngleValue:0}°" : $"Pente : {TopAngleValue:0}°";

    [RelayCommand]
    private void DefineTopShape()
    {
        var form = TopStacking.FormOf(new Article { Kind = Kind });
        TopShape = form.Shape;
        TopAngle = form.Angle;
    }

    [RelayCommand]
    private void ClearTopShape()
    {
        TopShape = null;
        TopAngle = null;
    }

    /// <summary>Verdict de gerbage selon la forme du dessus, et le raisonnement.</summary>
    public TopStackRule? TopRule
    {
        get
        {
            if (!ShowTopShape)
            {
                return null;
            }

            var a = new Article();
            ApplyTo(a);
            return TopStacking.For(a);
        }
    }

    public string TopVerdict => TopRule is { } r ? $"{char.ToUpper(r.ModeLabel[0])}{r.ModeLabel[1..]} — {r.Summary}" : "";
    public IReadOnlyList<string> TopSteps => TopRule?.Steps ?? [];
    public string TopVerdictColor => TopRule?.Mode switch { TopStackMode.Direct => "#1E8449", TopStackMode.Intercalaire => "#B9770E", TopStackMode.NonGerbable => "#C0392B", _ => "#7F8C8D" };
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
    public bool ShowFoldedLength => Spec(ArticleField.FoldedLength) != null;
    public bool ShowFoldedWidth => Spec(ArticleField.FoldedWidth) != null;
    public bool ShowFoldedHeight => Spec(ArticleField.FoldedHeight) != null;
    public string LengthLabel => Label(ArticleField.Length);
    public string WidthLabel => Label(ArticleField.Width);
    public string HeightLabel => Label(ArticleField.Height);
    public string DiameterLabel => Label(ArticleField.Diameter);
    public string InnerDiameterLabel => Label(ArticleField.InnerDiameter);
    public string FoldedLengthLabel => Label(ArticleField.FoldedLength);
    public string FoldedWidthLabel => Label(ArticleField.FoldedWidth);
    public string FoldedHeightLabel => Label(ArticleField.FoldedHeight);

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
        if (e.PropertyName is nameof(Length) or nameof(Width) or nameof(Height) or nameof(Diameter) or nameof(Kind))
        {
            OnPropertyChanged(nameof(HandleLengthLabel));
            OnPropertyChanged(nameof(HandleWidthLabel));
            OnPropertyChanged(nameof(HandleHeightLabel));
        }

        if (e.PropertyName is not (nameof(ProfileText) or nameof(IsDirty) or nameof(Errors) or nameof(IsNew) or nameof(Title) or
            nameof(TopVerdict) or nameof(TopSteps) or nameof(TopVerdictColor) or nameof(TopRule) or
            nameof(HandleLengthLabel) or nameof(HandleWidthLabel) or nameof(HandleHeightLabel)))
        {
            OnPropertyChanged(nameof(ProfileText));
            OnPropertyChanged(nameof(TopRule));
            OnPropertyChanged(nameof(TopVerdict));
            OnPropertyChanged(nameof(TopSteps));
            OnPropertyChanged(nameof(TopVerdictColor));
        }

        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(Errors) or nameof(IsNew) or nameof(ProfileText) or
            nameof(TopVerdict) or nameof(TopSteps) or nameof(TopVerdictColor) or nameof(TopRule)))
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
        FoldedLength = a.FoldedLength;
        FoldedWidth = a.FoldedWidth;
        FoldedHeight = a.FoldedHeight;
        QuantityPerCase = a.QuantityPerCase;
        CaseContentText = a is { Kind: ArticleKind.Caisse, CaseContent: { } link }
            ? (link.IsMixed
                ? $"Caisse mixte créée au colisage : {string.Join(" + ", link.Lines!.Select(l => $"{l.Quantity} × {FindArticle?.Invoke(l.ArticleId)?.Code ?? "produit supprimé"}"))}"
                : $"Créée au colisage : {(FindArticle?.Invoke(link.ArticleId)?.DisplayName ?? "produit supprimé de la base")}") +
              $" · caisse {link.CaseTypeCode ?? $"spécifique {link.InnerLength:0} × {link.InnerWidth:0} × {link.InnerHeight:0} mm (intérieur)"}"
            : null;
        Weight = a.Weight;
        Orientation = a.Orientation;
        CoilAxis = a.CoilAxis;
        MaxLoadOnTop = a.MaxLoadOnTop;
        MaxLayers = a.MaxLayers;
        Fragile = a.Fragile;
        TopShape = a.TopShape;
        TopAngle = a.TopAngle;
        Handle = a.Handle;
        HandleRounded = a.HandleRounded;
        HandleLength = a.HandleLength;
        HandleWidth = a.HandleWidth;
        HandleHeight = a.HandleHeight;
        HandleAngleLeft = a.HandleAngleLeft;
        HandleAngleRight = a.HandleAngleRight;
        HandleSolid = a.HandleSolid;
        Compressible = a.Compressible;
        CompressionPercent = a.CompressionPercent;
        Client = a.Client ?? "";
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
        a.FoldedLength = ShowFoldedLength ? FoldedLength : 0;
        a.FoldedWidth = ShowFoldedWidth ? FoldedWidth : 0;
        a.FoldedHeight = ShowFoldedHeight ? FoldedHeight : 0;
        a.QuantityPerCase = ShowQuantityPerCase ? QuantityPerCase : null;
        a.Weight = Weight;
        a.Orientation = Orientation;
        a.CoilAxis = CoilAxis;
        a.MaxLoadOnTop = MaxLoadOnTop;
        a.MaxLayers = MaxLayers;
        a.Fragile = Fragile;
        var top = ShowTopShape && TopShape != null;
        a.TopShape = top ? TopShape : null;
        a.TopAngle = top ? TopAngle : null;
        a.Handle = ShowTopShape ? Handle : null;
        var handle = ShowTopShape && HasHandle;
        a.HandleRounded = handle ? HandleRounded : null;
        a.HandleLength = handle ? HandleLength : null;
        a.HandleWidth = handle ? HandleWidth : null;
        a.HandleHeight = handle ? HandleHeight : null;
        a.HandleAngleLeft = handle && HandleRounded == true ? HandleAngleLeft : null;
        a.HandleAngleRight = handle && HandleRounded == true ? HandleAngleRight : null;
        a.HandleSolid = handle && HandleSolid;
        a.Compressible = ShowCompression && Compressible;
        a.CompressionPercent = ShowCompression && Compressible ? CompressionPercent : null;
        a.Client = Clean(Client);
        a.Family = Clean(Family);
        a.SubFamily = Clean(SubFamily);
        a.CustomerRef = Clean(CustomerRef);
        a.Ean = Clean(Ean);
        a.Color = Clean(Color);
        a.Notes = Clean(Notes);
    }
}
