using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.App.ViewModels;

/// <summary>Résultat de la recherche globale : nature, icône, titre, précision, et où il mène.</summary>
public sealed record SearchHit(string Kind, string Glyph, string Title, string Subtitle, Action Go);

/// <summary>
/// Recherche globale (Ctrl+K) : articles, colisages, conditionnements, clients, palettes, caisses du catalogue et
/// espaces, en une seule saisie. Tous les mots doivent figurer (code, désignation, client, famille, dimensions…) ;
/// un code exact passe en tête. Entrée ouvre le résultat dans son espace ; sans saisie, les éléments récents.
/// </summary>
public sealed partial class GlobalSearchViewModel(MainViewModel main) : ObservableObject
{
    private const int MaxHits = 40;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private SearchHit? _selected;
    [ObservableProperty] private string _status = "";

    public ObservableCollection<SearchHit> Results { get; } = [];

    partial void OnQueryChanged(string value) => Refresh();

    public void Open()
    {
        Query = "";
        Refresh();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Go(SearchHit? hit)
    {
        if ((hit ?? Selected ?? Results.FirstOrDefault()) is not { } target)
        {
            return;
        }

        IsOpen = false;
        target.Go();
    }

    /// <summary>Déplace la sélection (flèches haut / bas).</summary>
    public void Move(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var i = Selected == null ? -1 : Results.IndexOf(Selected);
        Selected = Results[Math.Clamp(i + delta, 0, Results.Count - 1)];
    }

    private static readonly (string Section, string Label, string Glyph, string Words)[] Sections =
    [
        ("Clients", "Clients", "", "clients client"),
        ("Articles", "Articles", "", "articles article produits"),
        ("Pallets", "Palettes", "", "palettes palette"),
        ("CaseTypes", "Caisses (catalogue)", "", "caisses catalogue cartons"),
        ("Packagings", "Conditionnements", "", "conditionnements palettisation calcul nouveau"),
        ("Cases", "Colisage (caisses)", "", "colisage caisse mise en caisse"),
        ("PackagingLibrary", "Gestion des conditionnements", "", "gestion conditionnements bibliotheque liste")
    ];

    private void Refresh()
    {
        Results.Clear();
        var db = main.Db;
        var words = Normalize(Query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<(int Rank, DateTime Date, SearchHit Hit)>();

        bool Match(string text) => words.All(text.Contains);
        int Rank(string code, string text)
        {
            var c = Normalize(code);
            var q = string.Join(' ', words);
            return c == q ? 0 : c.StartsWith(q, StringComparison.Ordinal) ? 1 : words.All(c.Contains) ? 2 : 3;
        }

        foreach (var (section, label, glyph, keys) in Sections)
        {
            if (words.Length > 0 && Match(Normalize(label + " " + keys)))
            {
                hits.Add((1, DateTime.MaxValue, new SearchHit("Espace", glyph, label, "Aller à l'espace", () => main.SelectedSection = section)));
            }
        }

        foreach (var a in db.Articles)
        {
            var text = Normalize($"{a.Code} {a.Designation} {a.Client} {db.ClientLabel(a.Client)} {a.Family} {a.SubFamily} {a.KindLabel} {a.DimensionsText} {a.CustomerRef} {a.Ean}");
            if (words.Length > 0 && !Match(text))
            {
                continue;
            }

            var article = a;
            hits.Add((Rank(a.Code, text), a.ModifiedAt, new SearchHit("Article", ArticleSchema.KindGlyph(a.Kind), a.DisplayName,
                $"{a.KindLabel} · {a.DimensionsText} mm · {a.Weight:0.###} kg{(string.IsNullOrWhiteSpace(a.Client) ? "" : " · " + db.ClientLabel(a.Client))}",
                () => main.ShowArticle(article))));
            if (main.HasColisage(a) && words.Length > 0)
            {
                hits.Add((Rank(a.Code, text) + 1, a.ModifiedAt, new SearchHit("Colisage", "", $"Colisage de {a.Code}",
                    $"{a.CaseQuantity} produits par caisse · ouvrir l'espace Colisage", () => main.OpenColisage(article))));
            }
        }

        foreach (var p in db.Packagings)
        {
            var codes = p.Kind == PackagingKind.Homogene
                ? db.FindArticle(p.ArticleId)?.Code
                : string.Join(" ", p.Lines.Select(l => db.FindArticle(l.ArticleId)?.Code));
            var text = Normalize($"{p.Code} {p.Name} {codes} {p.KindLabel}");
            if (words.Length > 0 && !Match(text))
            {
                continue;
            }

            var packaging = p;
            hits.Add((Rank(p.Code, text), p.ModifiedAt, new SearchHit("Conditionnement", "", p.DisplayName,
                $"{p.KindLabel} · {codes}{(p.Solution is { } s ? $" · {s.Title}" : "")}", () => main.ShowPackaging(packaging))));
        }

        if (words.Length > 0)
        {
            foreach (var c in db.Clients.Where(c => Match(Normalize($"{c.Code} {c.Name}"))))
            {
                var client = c;
                hits.Add((Rank(c.Code, Normalize(c.Name)), DateTime.MinValue, new SearchHit("Client", "", $"{c.Code} - {c.Name}", "Fiche client", () => main.ShowClient(client))));
            }

            foreach (var pt in db.Pallets.Where(pt => Match(Normalize($"{pt.Code} {pt.Name} {pt.Length} {pt.Width}"))))
            {
                var pallet = pt;
                hits.Add((Rank(pt.Code, Normalize(pt.Name)), DateTime.MinValue, new SearchHit("Palette", "", $"{pt.Code} – {pt.Name}",
                    $"{pt.Length:0} × {pt.Width:0} mm", () => main.ShowPallet(pallet))));
            }

            foreach (var ct in db.Cases.Where(ct => Match(Normalize($"{ct.Code} {ct.Name} {ct.Family}"))))
            {
                var type = ct;
                hits.Add((Rank(ct.Code, Normalize(ct.Name)), DateTime.MinValue, new SearchHit("Caisse", "", $"{ct.Code} – {ct.Name}",
                    $"Intérieur {ct.InnerLength:0} × {ct.InnerWidth:0} × {ct.InnerHeight:0} mm", () => main.ShowCaseType(type))));
            }
        }

        var ordered = words.Length == 0
            ? hits.OrderByDescending(h => h.Date).Take(12).ToList()
            : hits.OrderBy(h => h.Rank).ThenByDescending(h => h.Date).Take(MaxHits).ToList();
        foreach (var h in ordered)
        {
            Results.Add(h.Hit);
        }

        Selected = Results.FirstOrDefault();
        Status = words.Length == 0
            ? "Récemment modifiés · tapez un code, une désignation, un client, une dimension…"
            : hits.Count == 0 ? "Aucun résultat." : hits.Count > MaxHits ? $"{MaxHits} premiers résultats sur {hits.Count} : précisez la recherche." : $"{hits.Count} résultat(s)";
    }

    /// <summary>Minuscules, sans accents ni séparateurs : « Étiquette » trouve « etiquette ».</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var decomposed = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return sb.ToString();
    }
}
