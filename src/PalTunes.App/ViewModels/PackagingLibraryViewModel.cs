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
/// Espace « Gestion des conditionnements » : deux listes, les conditionnements palette et les caisses de colisage, rangées
/// comme les articles (client, famille, sous-famille, type), recherche, fiche résumée avec aperçu 3D (solution enregistrée,
/// ou colisage recalculé, caisse ouverte). D'une palette de caisses, « Voir le colisage » ouvre l'espace Colisage réglé sur
/// son colisage. L'écran de création ne garde que les plus récents.
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
        nameof(PalletText), nameof(SolutionText), nameof(DatesText))]
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
        if (!ShowColisages)
        {
            Preview = BuildPreview(value);
        }

        OnPropertyChanged(nameof(HasColisageLink));
        OpenColisageCommand.NotifyCanExecuteChanged();
    }

    public void SelectNode(TreeNode? node)
    {
        if (node?.Packaging is { } p)
        {
            Selected = p;
        }
        else if (node?.Article is { } box)
        {
            SelectedBox = box;
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

    /// <summary>Élément de l'arborescence : conditionnement palette ou caisse de colisage.</summary>
    private sealed record Entry(Guid Id, Keys K, TreeNode Leaf, string SearchText);

    public void Rebuild()
    {
        var selectedId = ShowColisages ? SelectedBox?.Id : Selected?.Id;
        var expanded = new HashSet<string>(Flatten(Roots).Where(n => n.IsGroup && n.IsExpanded).Select(n => n.Header));
        var firstBuild = Roots.Count == 0;
        Roots.Clear();
        var query = SearchText.Trim();
        var colisages = Colisages().ToList();
        PalletCountText = $"Palettes ({_main.Db.Packagings.Count})";
        ColisageCountText = $"Colisages ({colisages.Count})";

        var all = ShowColisages
            ? colisages.Select(a => ColisageEntry(a, a.Id == selectedId)).ToList()
            : _main.Db.Packagings.Select(pk => PalletEntry(pk, pk.Id == selectedId)).ToList();
        var shown = all.Where(x => query.Length == 0 || x.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Leaf.Header, StringComparer.CurrentCultureIgnoreCase).ToList();
        var noun = ShowColisages ? "colisage(s)" : "conditionnement(s)";
        CountText = shown.Count == all.Count ? $"{all.Count} {noun}" : $"{shown.Count} / {all.Count} {noun}";

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
        if (ShowColisages)
        {
            SelectedBox = colisages.FirstOrDefault(a => a.Id == selectedId) ?? shown.Select(x => x.Leaf.Article).FirstOrDefault();
        }
        else
        {
            if (selectedId != null)
            {
                Selected = _main.Db.Packagings.FirstOrDefault(pk => pk.Id == selectedId);
            }

            Selected ??= shown.Select(x => x.Leaf.Packaging).FirstOrDefault();
        }
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
            .Concat(k.Articles.SelectMany(a => new[] { a.Code, a.Designation })));
        return new Entry(pk.Id, k, leaf, text);
    }

    // ------------------------------------------------------------------ Colisages

    /// <summary>Caisses de colisage : articles caisse dont le contenu (caisse créée au colisage) ou la quantité par caisse est connu.</summary>
    private IEnumerable<Article> Colisages() =>
        _main.Db.Articles.Where(a => a.Kind == ArticleKind.Caisse && (a.CaseContent != null || a.CaseQuantity != null));

    private Entry ColisageEntry(Article box, bool selected)
    {
        var db = _main.Db;
        var content = box.CaseContent is { } link ? db.FindArticle(link.ArticleId) : null;
        var kind = box.CaseContent switch
        {
            { CaseTypeCode: { } code } => db.Cases.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase))?.Name ?? code,
            not null => "Caisse spécifique",
            _ => "Contenu non renseigné"
        };
        var k = new Keys(Group(db.ClientLabel(box.Client)), Group(box.Family ?? content?.Family), Group(box.SubFamily ?? content?.SubFamily), kind, box,
            content == null ? [box] : [box, content]);
        var leaf = new TreeNode
        {
            Header = box.Code,
            Subtitle = box.Designation,
            Detail = $"{box.CaseQuantity?.ToString("#,0", Fr)} produit(s) par caisse",
            Glyph = ArticleSchema.KindGlyph(ArticleKind.Caisse),
            Color = box.Color ?? "#C9A26B",
            Article = box,
            IsSelected = selected
        };
        var text = string.Join(" ", new[] { box.Code, box.Designation, k.Client, k.Family, k.SubFamily, kind, box.Notes, content?.Code, content?.Designation });
        return new Entry(box.Id, k, leaf, text);
    }

    /// <summary>Liste affichée : conditionnements palette (par défaut) ou caisses de colisage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPallets), nameof(HasSelection), nameof(Title), nameof(Subtitle), nameof(KindLabel), nameof(ClientText),
        nameof(ContentText), nameof(PalletText), nameof(SolutionText), nameof(DatesText), nameof(PalletLabel), nameof(SolutionLabel),
        nameof(LinksText), nameof(HasColisageLink), nameof(PreviewHint))]
    private bool _showColisages;

    public bool ShowPallets => !ShowColisages;

    [ObservableProperty] private string _palletCountText = "Palettes";
    [ObservableProperty] private string _colisageCountText = "Colisages";

    [RelayCommand]
    private void SetList(string list) => ShowColisages = list == "Colisages";

    partial void OnShowColisagesChanged(bool value)
    {
        Preview = null;
        Rebuild();
        Preview = value ? BuildBoxPreview(SelectedBox) : BuildPreview(Selected);
        OpenColisageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Caisse de colisage sélectionnée (liste « Colisages »).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(Title), nameof(Subtitle), nameof(KindLabel), nameof(ClientText), nameof(ContentText),
        nameof(PalletText), nameof(SolutionText), nameof(DatesText), nameof(LinksText), nameof(HasColisageLink))]
    private Article? _selectedBox;

    partial void OnSelectedBoxChanged(Article? value)
    {
        if (ShowColisages)
        {
            Preview = BuildBoxPreview(value);
        }

        OpenColisageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Caisse dont on peut ouvrir le colisage : caisse sélectionnée, ou article caisse de la palette sélectionnée.</summary>
    private Article? ColisageBox
    {
        get
        {
            var box = ShowColisages ? SelectedBox : Selected is { Kind: PackagingKind.Homogene } p ? _main.Db.FindArticle(p.ArticleId) : null;
            return CaseEngine.CanRebuild(box, id => _main.Db.FindArticle(id)) ? box : null;
        }
    }

    /// <summary>Bouton « Voir le colisage » : visible si un colisage existe pour l'élément sélectionné.</summary>
    public bool HasColisageLink => ColisageBox != null;

    private Model3DGroup? BuildBoxPreview(Article? box)
    {
        if (box == null)
        {
            return null;
        }

        if (CaseEngine.Rebuild(box, id => _main.Db.FindArticle(id), _main.Db.Cases) is { Solution.FirstUnit: { } unit } sheet)
        {
            var color = _main.Settings.Current.UseArticleColors ? ArticleColors.Parse(sheet.Content.Color, ArticleColors.DistinctByIndex(0)) : ArticleColors.DistinctByIndex(0);
            return Scene3DBuilder.Build(sheet.Solution, unit, CaseEngine.CaseConstraints(sheet.Spec, sheet.Axis), _ => color, int.MaxValue,
                new Scene3DBuilder.CaseRender(sheet.Spec.WallThickness, sheet.Type?.Color ?? "#C9A26B", true)).Root;
        }

        return Scene3DBuilder.BuildArticle(box, ArticleColors.Parse(box.Color, Color.FromRgb(0xC9, 0xA2, 0x6B))).Root;
    }

    /// <summary>Conditionnements palette de la caisse de colisage sélectionnée.</summary>
    private List<Packaging> PalletsOf(Article box) =>
        _main.Db.Packagings.Where(pk => pk.Kind == PackagingKind.Homogene && pk.ArticleId == box.Id).OrderByDescending(pk => pk.ModifiedAt).ToList();

    public string PalletLabel => ShowColisages ? "Caisse" : "Palette";
    public string SolutionLabel => ShowColisages ? "Colisage" : "Solution enregistrée";
    public string PreviewHint => ShowColisages ? "Colisage recalculé, caisse ouverte · clic droit : rotation · molette : zoom" : "Solution enregistrée, première unité · clic droit : rotation · molette : zoom";

    /// <summary>Colisage : conditionnements palette de la caisse.</summary>
    public string LinksText
    {
        get
        {
            if (!ShowColisages || SelectedBox is not { } box)
            {
                return "";
            }

            var pallets = PalletsOf(box);
            return pallets.Count == 0
                ? "Aucun conditionnement palette : « Palettiser » le crée."
                : string.Join(Environment.NewLine, pallets.Select(pk => $"{pk.DisplayName} · " + (pk.Solution is { } ps
                    ? $"{ps.ItemsPerUnit.ToString("#,0", Fr)} caisses / {ps.Base.Label}" + (box.CaseQuantity is { } q ? $" → {((long)ps.ItemsPerUnit * q).ToString("#,0", Fr)} produits" : "")
                    : "pas de solution enregistrée")));
        }
    }

    /// <summary>Ouvre l'espace Colisage réglé sur ce colisage (produit, caisse, quantité par caisse, axe), calculé.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenColisage))]
    private void OpenColisage()
    {
        if (ColisageBox is { } box)
        {
            _main.Cases.OpenColisage(box);
        }
    }

    private bool CanOpenColisage() => ColisageBox != null;

    /// <summary>Fiche de l'article caisse (espace Articles).</summary>
    [RelayCommand]
    private void OpenBoxArticle()
    {
        if (SelectedBox is { } box)
        {
            _main.SelectedSection = "Articles";
            _main.Articles.SelectedArticle = box;
            _main.Articles.RebuildTree();
        }
    }

    /// <summary>Conditionnement palette de la caisse : le dernier existant, sinon un nouveau.</summary>
    [RelayCommand]
    private void Palletize()
    {
        if (SelectedBox is not { } box)
        {
            return;
        }

        if (PalletsOf(box).FirstOrDefault() is { } existing)
        {
            _main.Packagings.Open(existing);
        }
        else
        {
            _main.Packagings.CreateFor(box);
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

    public bool HasSelection => ShowColisages ? SelectedBox != null : Selected != null;
    public string Title => ShowColisages ? SelectedBox?.Code ?? "" : Selected?.Code ?? "";
    public string Subtitle => ShowColisages ? SelectedBox?.Designation ?? "" : Selected?.Name ?? "";
    public string KindLabel => ShowColisages ? SelectedBox?.CaseContent != null ? "Colisage" : "Colisage (quantité seule)" : Selected?.KindLabel ?? "";
    public string ClientText => ShowColisages
        ? SelectedBox == null ? "" : Group(_main.Db.ClientLabel(SelectedBox.Client))
        : Selected == null ? "" : KeysOf(Selected).Client;

    public string ContentText
    {
        get
        {
            if (ShowColisages)
            {
                if (SelectedBox is not { } box)
                {
                    return "";
                }

                var content = box.CaseContent is { } link ? _main.Db.FindArticle(link.ArticleId) : null;
                return content == null
                    ? $"{box.CaseQuantity?.ToString("#,0", Fr)} produit(s) par caisse (produit contenu non renseigné)"
                    : $"{box.CaseQuantity?.ToString("#,0", Fr)} × {content.DisplayName} · {content.DimensionsText}";
            }

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
            if (ShowColisages)
            {
                if (SelectedBox is not { } box)
                {
                    return "";
                }

                var link = box.CaseContent;
                var type = link?.CaseTypeCode is { } code ? _main.Db.Cases.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)) : null;
                return (type != null ? $"{type.Code} – {type.Name} · " : link != null ? "Caisse spécifique · " : "") +
                       (link != null ? $"intérieur {link.InnerLength:0} × {link.InnerWidth:0} × {link.InnerHeight:0} mm · " : "") +
                       $"extérieur {box.DimensionsText} mm";
            }

            if (Selected is not { } p)
            {
                return "";
            }

            var pallet = _main.Db.FindPallet(p.PalletId);
            return pallet == null ? "Palette non renseignée" : BaseInfo.From(pallet, p.PalletRotated, p.CountAlongLength, p.CountAlongWidth).Label;
        }
    }

    public string SolutionText
    {
        get
        {
            if (!ShowColisages)
            {
                return Selected == null ? "" : Summary(Selected);
            }

            if (SelectedBox is not { } box)
            {
                return "";
            }

            var sheet = CaseEngine.Rebuild(box, id => _main.Db.FindArticle(id), _main.Db.Cases);
            return sheet == null
                ? $"{box.CaseQuantity?.ToString("#,0", Fr)} produit(s) par caisse · {box.Weight.ToString(Formats.TotalWeight, Fr)} kg brut"
                : $"{sheet.Solution.ItemsPerUnit.ToString("#,0", Fr)} produits · {sheet.Solution.ItemsPerLayer.ToString("#,0", Fr)} par couche × {sheet.Solution.LayerCount} · " +
                  $"{box.Weight.ToString(Formats.TotalWeight, Fr)} kg brut";
        }
    }

    public string DatesText => ShowColisages
        ? SelectedBox == null ? "" : $"Créé le {SelectedBox.CreatedAt:dd/MM/yyyy} · modifié le {SelectedBox.ModifiedAt:dd/MM/yyyy HH:mm}"
        : Selected == null ? "" : $"Créé le {Selected.CreatedAt:dd/MM/yyyy} · modifié le {Selected.ModifiedAt:dd/MM/yyyy HH:mm}";

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
