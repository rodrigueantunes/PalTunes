using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalTunes.App.Services;
using PalTunes.Core.Engine;
using PalTunes.Core.Models;

namespace PalTunes.App.ViewModels;

/// <summary>
/// Fiches imprimées de l'article affiché, quel que soit l'espace : fiche palette, fiche de colisage, fiche de
/// conditionnement (colisage puis palette). Chaque bouton est actif si et seulement si la fiche est possible pour
/// l'article ; ce qui manque est calculé en tâche de fond dès que l'article change, puis réutilisé à l'impression.
/// </summary>
/// <remarks>
/// Article des fiches : article sélectionné (Articles), article du conditionnement (Conditionnements, Gestion des
/// conditionnements), produit à mettre en caisse (Colisage) ; dans les autres espaces, le dernier article affiché.
/// Fiche palette : un conditionnement existe (affiché, sélectionné ou enregistré). Fiche de colisage : un colisage existe
/// (affiché, caisse créée au colisage, ou au moins une quantité par caisse : fiche résumée). Fiche de conditionnement :
/// les deux (palette de la caisse, puis colisage).
/// </remarks>
public sealed partial class PrintCenter : ObservableObject
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly MainViewModel _main;

    public PrintCenter(MainViewModel main) => _main = main;

    /// <summary>Article des fiches (null : aucun article affiché).</summary>
    [ObservableProperty] private Article? _article;

    /// <summary>« Fiches de CODE – désignation » sous l'en-tête de la navigation.</summary>
    [ObservableProperty] private string _articleText = "Aucun article";

    [ObservableProperty] private string _palletTip = "";
    [ObservableProperty] private string _caseTip = "";
    [ObservableProperty] private string _packagingTip = "";

    /// <summary>Vérification en cours (tâche de fond) : boutons grisés le temps du calcul.</summary>
    [ObservableProperty] private bool _isChecking;

    // ------------------------------------------------------------------ Plan d'impression de l'article

    /// <summary>Ce qui est imprimable pour un article : palette, colisage, conditionnement, avec la raison sinon.</summary>
    private sealed class Plan
    {
        /// <summary>Conditionnement enregistré de l'article, avec sa solution.</summary>
        public Packaging? Pallet;

        public string PalletWhy = "";

        /// <summary>Colisage recalculé (caisse créée au colisage : produit et caisse connus).</summary>
        public CaseEngine.CaseSheet? Case;

        /// <summary>Colisage minimal : caisse dont seule la quantité par caisse est connue.</summary>
        public Article? CaseSummary;

        public string CaseWhy = "";
        public string PackagingWhy = "";

        public bool HasCase => Case != null || CaseSummary != null;
    }

    private Plan _plan = new();
    private string? _key;
    private int _version;

    /// <summary>Article de l'espace affiché ; null pour un espace sans article (on garde alors le dernier).</summary>
    private (bool HasContext, Article? Article) ContextArticle() => _main.SelectedSection switch
    {
        "Articles" => (true, _main.Articles.SelectedArticle),
        "Packagings" => (true, _main.Packagings.IsHomogeneous ? _main.Packagings.Article : null),
        "PackagingLibrary" => (true, _main.PackagingLibrary.Selected is { Kind: PackagingKind.Homogene } p ? _main.Db.FindArticle(p.ArticleId) : null),
        "Cases" => (true, _main.Cases.IsMixed ? null : _main.Cases.Article),
        _ => (false, null)
    };

    /// <summary>Dernier conditionnement homogène enregistré de l'article, avec sa solution ; null s'il n'y en a pas.</summary>
    private Packaging? SavedPackaging(Article a) =>
        _main.Db.Packagings.Where(p => p.Kind == PackagingKind.Homogene && p.ArticleId == a.Id && p.Solution is { FirstUnit: not null }).MaxBy(p => p.ModifiedAt);

    private string Key(Article a)
    {
        var db = _main.Db;
        var packagings = db.Packagings.Where(p => p.ArticleId == a.Id).Select(p => p.ModifiedAt.Ticks).DefaultIfEmpty().Max();
        var content = a.CaseContent is { } link ? db.FindArticle(link.ArticleId)?.ModifiedAt.Ticks ?? -1 : 0;
        return $"{a.Id}|{a.ModifiedAt.Ticks}|{packagings}|{db.Packagings.Count}|{content}|{db.Cases.Count}";
    }

    /// <summary>
    /// Plan d'impression (tâche de fond pour le recalcul du colisage) : la fiche palette demande un conditionnement
    /// enregistré, la fiche de colisage un colisage (caisse créée au colisage, ou au moins une quantité par caisse), la
    /// fiche de conditionnement les deux. Rien n'est calculé à la place de l'utilisateur.
    /// </summary>
    private Plan Build(Article a, Packaging? saved, List<CaseType> cases)
    {
        var db = _main.Db;
        var plan = new Plan { Pallet = saved };
        plan.PalletWhy = "Aucun conditionnement enregistré pour cet article : créez-le dans l'espace Conditionnements.";

        if (a.Kind == ArticleKind.Caisse && a.CaseContent != null)
        {
            plan.Case = CaseEngine.Rebuild(a, id => db.FindArticle(id), cases);
            if (plan.Case == null)
            {
                // Le produit contenu n'existe plus ou ne tient plus : la quantité par caisse reste imprimable.
                plan.CaseSummary = a.CaseQuantity != null ? a : null;
                plan.CaseWhy = "Colisage impossible à recalculer (produit contenu supprimé ou modifié).";
            }
        }
        else if (a.Kind == ArticleKind.Caisse && a.CaseQuantity != null)
        {
            plan.CaseSummary = a;
        }
        else
        {
            plan.CaseWhy = a.Kind == ArticleKind.Caisse
                ? "Pas de colisage : renseignez la quantité par caisse ou créez la caisse dans l'espace Colisage."
                : "Pas de colisage pour cet article : calculez-le et créez sa caisse dans l'espace Colisage.";
        }

        plan.PackagingWhy = !plan.HasCase ? plan.CaseWhy : plan.PalletWhy;
        return plan;
    }

    public static Packaging CasePackaging(Article box, PalletType pallet, PackagingConstraints constraints, Solution solution) => new()
    {
        Code = box.Code, Name = $"Palette de {box.Designation}", Kind = PackagingKind.Homogene, ArticleId = box.Id, PalletId = pallet.Id,
        Constraints = constraints, Solution = solution
    };

    /// <summary>Met à jour l'article des fiches et, s'il a changé, relance la vérification en tâche de fond.</summary>
    public void Refresh()
    {
        var (hasContext, article) = ContextArticle();
        if (hasContext)
        {
            Article = article;
        }

        ArticleText = Article is { } a ? $"Fiches de {a.DisplayName}" : "Aucun article affiché";
        var key = Article is { } current ? Key(current) : "";
        if (key != _key)
        {
            _key = key;
            _plan = new Plan();
            var version = ++_version;
            if (Article is { } target)
            {
                IsChecking = true;
                var saved = SavedPackaging(target);
                var cases = _main.Db.Cases.ToList();
                Task.Run(() =>
                {
                    try
                    {
                        return Build(target, saved, cases);
                    }
                    catch (Exception ex)
                    {
                        return new Plan { PalletWhy = ex.Message, CaseWhy = ex.Message, PackagingWhy = ex.Message };
                    }
                }).ContinueWith(t =>
                {
                    if (version != _version)
                    {
                        return;
                    }

                    _plan = t.Result;
                    IsChecking = false;
                    UpdateCommands();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                IsChecking = false;
            }
        }

        UpdateCommands();
    }

    private void UpdateCommands()
    {
        PalletSheetCommand.NotifyCanExecuteChanged();
        CaseSheetCommand.NotifyCanExecuteChanged();
        PackagingSheetCommand.NotifyCanExecuteChanged();
        const string checking = "Vérification en cours…";
        var none = Article == null && !ShownPackaging && LibraryPackaging == null ? "Aucun article affiché." : null;
        PalletTip = "Ctrl+P · " + (CanPalletSheet() ? "Fiche palette du conditionnement (spécification, schémas, plan de palettisation)."
            : none ?? (IsChecking ? checking : _plan.PalletWhy));
        CaseTip = CanCaseSheet()
            ? _plan.Case == null && _plan.CaseSummary != null && !ShownColisage
                ? "Fiche de colisage résumée : seule la quantité par caisse est connue (pas de plans)."
                : "Fiche de colisage : le produit dans sa caisse (quantité par caisse, poids brut, schémas, vue 3D)."
            : none ?? (IsChecking ? checking : _plan.CaseWhy);
        PackagingTip = CanPackagingSheet() ? "Fiche palette puis fiche de colisage, en un seul document."
            : none ?? (IsChecking ? checking : _plan.PackagingWhy);
    }

    // ------------------------------------------------------------------ Contexte affiché (prioritaire pour le contenu)

    /// <summary>Conditionnement affiché avec sa solution (écran Conditionnements, y compris hétérogène).</summary>
    private bool ShownPackaging => _main.SelectedSection == "Packagings" && _main.Packagings.CanPrintPalletSheet &&
                                   (!_main.Packagings.IsHomogeneous || ReferenceEquals(_main.Packagings.Article, Article));

    /// <summary>Conditionnement sélectionné dans la gestion, avec sa solution enregistrée.</summary>
    private Packaging? LibraryPackaging => _main.SelectedSection == "PackagingLibrary" && _main.PackagingLibrary.Selected is { Solution.FirstUnit: not null } p ? p : null;

    /// <summary>Colisage affiché pour cet article (écran Colisage).</summary>
    private bool ShownColisage => _main.SelectedSection == "Cases" && ReferenceEquals(_main.Cases.Article, Article) && _main.Cases.CanPrintCaseSheet;

    // ------------------------------------------------------------------ Commandes

    /// <summary>Fiche palette : un conditionnement existe (affiché, sélectionné ou enregistré pour l'article).</summary>
    private bool CanPalletSheet() => ShownPackaging || LibraryPackaging != null || (!IsChecking && _plan.Pallet != null);

    /// <summary>Fiche de colisage : un colisage existe (affiché, caisse créée au colisage, ou quantité par caisse).</summary>
    private bool CanCaseSheet() => ShownColisage || (!IsChecking && _plan.HasCase);

    /// <summary>Fiche de conditionnement : fiche de colisage possible et conditionnement de la caisse (ou caisses palettisées du colisage affiché).</summary>
    private bool CanPackagingSheet() => ShownColisage
        ? _main.Cases.CanPrintPackagingSheet
        : !IsChecking && _plan.HasCase && (_plan.Pallet != null || ShownPackaging || LibraryPackaging != null);

    [RelayCommand(CanExecute = nameof(CanPalletSheet))]
    private void PalletSheet()
    {
        if (ShownPackaging)
        {
            _main.Packagings.PrintCommand.Execute(null);
        }
        else if (LibraryPackaging is { } p)
        {
            PrintService.PrintSheet(p, p.Solution!, p.Solution!.FirstUnit!, _main.Db, _main.PackagingLibrary.ColorsOf(p));
        }
        else if (_plan.Pallet is { Solution: { } s } packaging && Article is { } a)
        {
            PrintService.PrintSheet(packaging, s, s.FirstUnit!, _main.Db, _main.ColorsFor(a), a);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCaseSheet))]
    private void CaseSheet()
    {
        if (ShownColisage)
        {
            _main.Cases.PrintCaseSheet();
        }
        else if (_plan.Case is { } sheet)
        {
            PrintService.PrintCaseSheet(sheet, Article?.Code, _main.Db, _main.ColorsFor(sheet.Content));
        }
        else if (_plan.CaseSummary is { } box)
        {
            PrintService.PrintCaseSummarySheet(box, _plan.Pallet, _main.Db);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPackagingSheet))]
    private void PackagingSheet()
    {
        if (ShownColisage)
        {
            _main.Cases.PrintPackagingSheet();
            return;
        }

        if (Article is not { } a)
        {
            return;
        }

        // Conditionnement de la caisse : celui affiché ou sélectionné s'il porte sur cet article, sinon le dernier enregistré.
        var packaging = LibraryPackaging is { ArticleId: var id } lib && id == a.Id ? lib : _plan.Pallet;
        if (ShownPackaging && _main.Packagings.CurrentSolution is { } shown)
        {
            var current = _main.Packagings.CurrentPackaging();
            current.Solution = shown;
            packaging = current;
        }

        if (packaging?.Solution is not { } s)
        {
            return;
        }

        if (_plan.Case is { } sheet)
        {
            PrintService.PrintPackagingSheet(sheet, a, packaging, s, s.FirstUnit!, _main.Db, _main.ColorsFor(sheet.Content), _main.ColorsFor(a));
        }
        else if (_plan.CaseSummary is { } box)
        {
            PrintService.PrintPackagingSummarySheet(box, packaging, s, s.FirstUnit!, _main.Db, _main.ColorsFor(a));
        }
    }

    /// <summary>Écoute les espaces qui portent un article.</summary>
    public void Attach(params INotifyPropertyChanged[] sources)
    {
        foreach (var source in sources)
        {
            source.PropertyChanged += (_, _) => Refresh();
        }
    }
}
