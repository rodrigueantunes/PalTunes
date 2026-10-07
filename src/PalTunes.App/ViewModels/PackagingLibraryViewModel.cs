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

/// <summary>
/// Espace « Gestion des conditionnements » : tous les conditionnements créés, rangés comme les articles (client,
/// famille, sous-famille, type), recherche, fiche résumée avec aperçu 3D de la solution enregistrée. D'une palette de
/// caisses, « Voir le colisage » ouvre l'espace Colisage réglé sur le colisage de la caisse (choix si plusieurs caisses).
/// L'écran de création ne garde que les plus récents.
/// </summary>
public sealed partial class PackagingLibraryViewModel : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private const string NotSet = "(Non renseigné)";
    private const string HeterogeneousType = "Hétérogène (multi-articles)";
    private readonly MainViewModel _main;

    public PackagingLibraryViewModel(MainViewModel main)
    {
        _main = main;
        _grouping = Groupings.FirstOrDefault(g => g.Key == main.Settings.Current.PackagingTreeGrouping) ?? Groupings[0];
    }

    public IReadOnlyList<GroupingChoice> Groupings { get; } =
    [
        new("Client", "Client › Famille › Sous-famille"),
        new("Family", "Famille › Type"),
        new("Kind", "Type › Client"),
        new("List", "Liste")
    ];

    public ObservableCollection<TreeNode> Roots { get; } = [];

    [ObservableProperty] private GroupingChoice _grouping;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _countText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(Title), nameof(Subtitle), nameof(KindLabel), nameof(ClientText), nameof(ContentText),
        nameof(PalletText), nameof(SolutionText), nameof(DatesText), nameof(IsHomogeneousSelection))]
    private Packaging? _selected;

    [ObservableProperty] private Model3DGroup? _preview;

    partial void OnGroupingChanged(GroupingChoice value)
    {
        _main.Settings.Current.PackagingTreeGrouping = value.Key;
        _main.Settings.Save();
        Rebuild();
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnSelectedChanged(Packaging? value)
    {
        Preview = BuildPreview(value);
        Colisages.Clear();
        foreach (var link in ColisagesOf(value))
        {
            Colisages.Add(link);
        }

        OnPropertyChanged(nameof(HasColisageLink));
        OnPropertyChanged(nameof(ColisageButtonText));
        OnPropertyChanged(nameof(ColisageText));
    }

    public void SelectNode(TreeNode? node)
    {
        if (node?.Packaging is { } p)
        {
            Selected = p;
        }
    }

    // ------------------------------------------------------------------ Rangement

    private sealed record Keys(string Client, string Family, string SubFamily, string Kind, Article? Article, List<Article> Articles);

    private Keys KeysOf(Packaging p)
    {
        var db = _main.Db;
        if (p.Kind == PackagingKind.Homogene)
        {
            var a = db.FindArticle(p.ArticleId);
            return new Keys(Group(db.ClientLabel(a?.Client)), Group(a?.Family), Group(a?.SubFamily), a?.KindLabel ?? "Sans article", a,
                a == null ? [] : [a]);
        }

        var articles = p.Lines.Select(l => db.FindArticle(l.ArticleId)).Where(a => a != null).Select(a => a!).ToList();
        string Common(Func<Article, string?> f, string mixed)
        {
            var values = articles.Select(a => Group(f(a))).Distinct().ToList();
            return values.Count switch { 0 => NotSet, 1 => values[0], _ => mixed };
        }

        return new Keys(Common(a => db.ClientLabel(a.Client), "Multi-clients"), Common(a => a.Family, "Mixte"), Common(a => a.SubFamily, "Mixte"),
            HeterogeneousType, null, articles);
    }

    private static string Group(string? v) => string.IsNullOrWhiteSpace(v) ? NotSet : v.Trim();

    /// <summary>Élément de l'arborescence : un conditionnement, ses clés de rangement, sa feuille, son texte de recherche.</summary>
    private sealed record Entry(Guid Id, Keys K, TreeNode Leaf, string SearchText);

    public void Rebuild()
    {
        var selectedId = Selected?.Id;
        var expanded = new HashSet<string>(Flatten(Roots).Where(n => n.IsGroup && n.IsExpanded).Select(n => n.Header));
        var firstBuild = Roots.Count == 0;
        Roots.Clear();
        var query = SearchText.Trim();
        var all = _main.Db.Packagings.Select(pk => PalletEntry(pk, pk.Id == selectedId)).ToList();
        var shown = all.Where(x => query.Length == 0 || x.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Leaf.Header, StringComparer.CurrentCultureIgnoreCase).ToList();
        CountText = shown.Count == all.Count ? $"{all.Count} conditionnement(s)" : $"{shown.Count} / {all.Count} conditionnement(s)";

        Func<Keys, string>[] levels = Grouping.Key switch
        {
            "Client" => [k => k.Client, k => k.Family, k => k.SubFamily],
            "Family" => [k => k.Family, k => k.Kind],
            "Kind" => [k => k.Kind, k => k.Client],
            _ => []
        };
        string[] glyphs = Grouping.Key switch
        {
            "Client" => ["", "", ""],
            "Family" => ["", ""],
            "Kind" => ["", ""],
            _ => []
        };

        void Build(ObservableCollection<TreeNode> target, IEnumerable<Entry> items, int level)
        {
            if (level >= levels.Length)
            {
                foreach (var e in items)
                {
                    target.Add(e.Leaf);
                }

                return;
            }

            foreach (var g in items.GroupBy(x => levels[level](x.K))
                         .OrderBy(g => g.Key == NotSet ? 1 : 0).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var node = new TreeNode
                {
                    Header = g.Key,
                    Glyph = glyphs[level],
                    Count = g.Count(),
                    Detail = $"{g.Count()}",
                    IsExpanded = query.Length > 0 || expanded.Contains(g.Key) || (firstBuild && level == 0) || g.Any(x => x.Id == selectedId)
                };
                Build(node.Children, g, level + 1);
                target.Add(node);
            }
        }

        Build(Roots, shown, 0);
        if (selectedId != null)
        {
            Selected = _main.Db.Packagings.FirstOrDefault(pk => pk.Id == selectedId);
        }

        Selected ??= shown.Select(x => x.Leaf.Packaging).FirstOrDefault();
    }

    private Entry PalletEntry(Packaging pk, bool selected)
    {
        var k = KeysOf(pk);
        var leaf = new TreeNode
        {
            Header = pk.Code,
            Subtitle = pk.Name,
            Detail = Summary(pk),
            Glyph = pk.Kind == PackagingKind.Homogene && k.Article != null ? ArticleSchema.KindGlyph(k.Article.Kind) : "",
            Color = pk.Kind == PackagingKind.Homogene ? k.Article?.Color ?? "#5DADE2" : "#8E44AD",
            Packaging = pk,
            IsSelected = selected
        };
        var text = string.Join(" ", new[] { pk.Code, pk.Name, k.Client, k.Family, k.SubFamily, k.Kind, pk.Notes }
            .Concat(k.Articles.SelectMany(a => new[] { a.Code, a.Designation }))
            .Concat(k.Articles.SelectMany(a => a.CaseContent is { } link
                ? (link.Lines?.Select(l => l.ArticleId) ?? [link.ArticleId]).Select(id => _main.Db.FindArticle(id)?.Code)
                : [])));
        return new Entry(pk.Id, k, leaf, text);
    }

    // ------------------------------------------------------------------ Colisages des caisses de la palette

    /// <summary>Colisages accessibles depuis le conditionnement sélectionné (caisses créées au colisage, produit connu).</summary>
    public ObservableCollection<ColisageLink> Colisages { get; } = [];

    private List<ColisageLink> ColisagesOf(Packaging? p)
    {
        if (p == null)
        {
            return [];
        }

        var ids = p.Kind == PackagingKind.Homogene ? [p.ArticleId ?? Guid.Empty] : p.Lines.Select(l => l.ArticleId).Distinct().ToList();
        return ids.Select(id => _main.Db.FindArticle(id))
            .Where(a => CaseEngine.CanRebuild(a, id => _main.Db.FindArticle(id)))
            .Select(a => new ColisageLink(a!, a!.CaseContent!.IsMixed
                ? $"{a.Code} · caisse mixte : {string.Join(" + ", a.CaseContent.Lines!.Select(l => $"{l.Quantity.ToString("#,0", Fr)} × {_main.Db.FindArticle(l.ArticleId)?.Code}"))}"
                : $"{a.Code} · {a.CaseQuantity?.ToString("#,0", Fr)} × {_main.Db.FindArticle(a.CaseContent.ArticleId)?.Code}"))
            .ToList();
    }

    /// <summary>Bouton « Voir le colisage » : visible si un colisage existe pour la palette sélectionnée.</summary>
    public bool HasColisageLink => Colisages.Count > 0;

    public string ColisageButtonText => Colisages.Count > 1 ? $"Voir les colisages ({Colisages.Count})" : "Voir le colisage";

    /// <summary>Fiche : colisages des caisses de la palette.</summary>
    public string ColisageText => string.Join(Environment.NewLine, Colisages.Select(c => c.Label));

    /// <summary>Ouvre l'espace Colisage réglé sur le colisage de la caisse (la seule, ou celle choisie).</summary>
    [RelayCommand]
    private void OpenColisage(ColisageLink? link)
    {
        if ((link ?? Colisages.FirstOrDefault()) is { } target)
        {
            _main.OpenColisage(target.Box);
        }
    }

    public bool IsHomogeneousSelection => Selected is { Kind: PackagingKind.Homogene };

    /// <summary>Fiche de l'article du conditionnement homogène (espace Articles).</summary>
    [RelayCommand]
    private void OpenArticle()
    {
        if (Selected is { Kind: PackagingKind.Homogene } p && _main.Db.FindArticle(p.ArticleId) is { } a)
        {
            _main.ShowArticle(a);
        }
    }

    private static IEnumerable<TreeNode> Flatten(IEnumerable<TreeNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

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

    // ------------------------------------------------------------------ Fiche

    public bool HasSelection => Selected != null;
    public string Title => Selected?.Code ?? "";
    public string Subtitle => Selected?.Name ?? "";
    public string KindLabel => Selected?.KindLabel ?? "";
    public string ClientText => Selected == null ? "" : KeysOf(Selected).Client;

    public string ContentText
    {
        get
        {
            if (Selected is not { } p)
            {
                return "";
            }

            if (p.Kind == PackagingKind.Homogene)
            {
                var a = _main.Db.FindArticle(p.ArticleId);
                return a == null ? "Article introuvable" : $"{a.DisplayName} · {a.DimensionsText}" +
                                                           (p.TargetQuantity is { } q ? $" · quantité imposée {q.ToString("#,0", Fr)}" : "");
            }

            return string.Join(Environment.NewLine, p.Lines.Select(l =>
                $"{_main.Db.FindArticle(l.ArticleId)?.DisplayName ?? "Article introuvable"} × {l.Quantity.ToString("#,0", Fr)}"));
        }
    }

    public string PalletText
    {
        get
        {
            if (Selected is not { } p)
            {
                return "";
            }

            var pallet = _main.Db.FindPallet(p.PalletId);
            return pallet == null ? "Palette non renseignée" : BaseInfo.From(pallet, p.PalletRotated, p.CountAlongLength, p.CountAlongWidth).Label;
        }
    }

    public string SolutionText => Selected == null ? "" : Summary(Selected);

    public string DatesText => Selected == null ? "" : $"Créé le {Selected.CreatedAt:dd/MM/yyyy} · modifié le {Selected.ModifiedAt:dd/MM/yyyy HH:mm}";

    private static string Summary(Packaging p)
    {
        if (p.Solution is not { } s)
        {
            return "Aucune solution enregistrée";
        }

        var u = s.FirstUnit;
        return s.Kind == PackagingKind.Homogene
            ? $"{s.ItemsPerUnit.ToString("#,0", Fr)} produits · {s.ItemsPerLayer.ToString("#,0", Fr)} par couche × {s.LayerCount} · " +
              $"{(u == null ? "" : $"{u.Metrics.EnclosureLength:0} × {u.Metrics.EnclosureWidth:0} × {u.Metrics.EnclosureHeight:0} mm · ")}{s.Base.Label}"
            : $"{s.UnitCount} unité(s) · {s.TotalItems.ToString("#,0", Fr)} produits · {s.Base.Label}";
    }

    /// <summary>Mêmes couleurs que l'écran des conditionnements : distinctes par défaut, ou couleurs des fiches articles.</summary>
    public IReadOnlyDictionary<Guid, Color> ColorsOf(Packaging p)
    {
        var colors = new Dictionary<Guid, Color>();
        var ids = p.Kind == PackagingKind.Homogene ? [p.ArticleId ?? Guid.Empty] : p.Lines.Select(l => l.ArticleId).ToList();
        var k = 0;
        foreach (var id in ids.Distinct())
        {
            colors[id] = _main.Settings.Current.UseArticleColors
                ? ArticleColors.Parse(_main.Db.FindArticle(id)?.Color, ArticleColors.ByIndex(k++))
                : ArticleColors.DistinctByIndex(k++);
        }

        return colors;
    }

    private Model3DGroup? BuildPreview(Packaging? p)
    {
        if (p?.Solution is not { FirstUnit: { } unit } s)
        {
            return null;
        }

        var colors = ColorsOf(p);
        return Scene3DBuilder.Build(s, unit, p.Constraints, id => colors.TryGetValue(id, out var c) ? c : Colors.SteelBlue, int.MaxValue).Root;
    }

    // ------------------------------------------------------------------ Actions

    [RelayCommand]
    private void Open()
    {
        if (Selected is { } p)
        {
            _main.Packagings.Open(p);
        }
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is { } p)
        {
            _main.Packagings.Open(p);
            _main.Packagings.DuplicateCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { } p || !_main.Dialogs.Confirm($"Supprimer le conditionnement {p.DisplayName} ?"))
        {
            return;
        }

        _main.Db.Packagings.Remove(p);
        _main.SaveDatabase();
        Selected = null;
        Rebuild();
        _main.Packagings.Refresh();
        _main.NotifyPackagingsChanged();
        _main.ShowToast($"Conditionnement {p.Code} supprimé.", "Ok");
    }
}

/// <summary>Colisage accessible depuis une palette : article caisse et libellé (code, quantité × produit).</summary>
public sealed record ColisageLink(Article Box, string Label);
