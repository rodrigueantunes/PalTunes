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
/// Fiche palette : solution affichée ou enregistrée, sinon calculée sur la palette du dernier conditionnement de
/// l'article (à défaut la palette de destination du colisage). Fiche de colisage : caisse créée au colisage (contenu
/// connu), ou produit qui tient dans au moins une caisse du catalogue (la meilleure). Fiche de conditionnement : colisage
/// puis palette de la caisse.
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
        public (Packaging Packaging, Solution Solution)? Pallet;
        public string PalletWhy = "";
        public CaseEngine.CaseSheet? Case;
        public string CaseWhy = "";

        /// <summary>Caisse du colisage palettisée (produit) ; null pour un article caisse (sa palette est <see cref="Pallet"/>).</summary>
        public (Article Box, Packaging Packaging, Solution Solution)? CasePallet;

        public string PackagingWhy = "";
        public bool PackagingOk;
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
        "Cases" => (true, _main.Cases.Article),
        _ => (false, null)
    };

    /// <summary>Palette du dernier conditionnement homogène de l'article (enregistré), sinon palette de destination du colisage.</summary>
    private Packaging Template(Article a)
    {
        var last = _main.Db.Packagings.Where(p => p.Kind == PackagingKind.Homogene && p.ArticleId == a.Id).MaxBy(p => p.ModifiedAt);
        if (last != null)
        {
            return last;
        }

        var pallet = DestinationPallet();
        return new Packaging
        {
            Code = a.Code, Name = a.Designation, Kind = PackagingKind.Homogene, ArticleId = a.Id, PalletId = pallet?.Id,
            Constraints = _main.Cases.DestinationConstraints
        };
    }

    private PalletType? DestinationPallet() =>
        _main.Cases.DestinationPallet ?? _main.Db.Pallets.FirstOrDefault(p => p.Code == "EUR1") ?? _main.Db.Pallets.FirstOrDefault();

    private string Key(Article a)
    {
        var db = _main.Db;
        var packagings = db.Packagings.Where(p => p.ArticleId == a.Id).Select(p => p.ModifiedAt.Ticks).DefaultIfEmpty().Max();
        var content = a.CaseContent is { } link ? db.FindArticle(link.ArticleId)?.ModifiedAt.Ticks ?? -1 : 0;
        return $"{a.Id}|{a.ModifiedAt.Ticks}|{packagings}|{content}|{db.Cases.Count}|{db.Pallets.Count}|{DestinationPallet()?.Id}|" +
               $"{_main.Cases.DestinationConstraints.MaxTotalHeight}|{_main.Cases.DestinationConstraints.OverhangLength}|{_main.Cases.DestinationConstraints.OverhangWidth}";
    }

    /// <summary>Calcul du plan (tâche de fond) : mêmes moteurs que les écrans, aucune modification de la base.</summary>
    private Plan Build(Article a, Packaging template, PalletType? destination, PackagingConstraints destinationConstraints, List<CaseType> cases)
    {
        var db = _main.Db;
        var plan = new Plan();

        // Palette de l'article.
        if (ArticleSchema.Validate(a) is [var error, ..])
        {
            plan.PalletWhy = $"Fiche article incomplète : {error}";
        }
        else if (template.Solution is { FirstUnit: not null } saved && db.Packagings.Contains(template))
        {
            plan.Pallet = (template, saved);
        }
        else
        {
            var r = PackagingCalculator.Compute(template, db);
            if (r.Recommended is { FirstUnit: not null } s)
            {
                plan.Pallet = (template, s);
            }
            else
            {
                plan.PalletWhy = r.Messages.LastOrDefault() ?? "Aucune palettisation possible.";
            }
        }

        // Colisage.
        if (a.Kind == ArticleKind.Caisse && a.CaseContent != null)
        {
            plan.Case = CaseEngine.Rebuild(a, id => db.FindArticle(id), cases);
            plan.CaseWhy = CaseEngine.CanRebuild(a, id => db.FindArticle(id))
                ? "Le produit ne tient plus dans la caisse (fiche produit ou caisse modifiée)."
                : "Le produit contenu n'existe plus dans la base.";
        }
        else if (a.Kind == ArticleKind.Caisse && a.CaseQuantity != null)
        {
            plan.CaseWhy = "Caisse importée ou créée avant la 0.1.3 : son produit contenu n'est pas connu.";
        }
        else if (ArticleSchema.Validate(a).Count == 0)
        {
            var r = CaseEngine.Propose(a, cases, 0, null, _main.Cases.ManualLimit, destination, destinationConstraints);
            if (r.Recommended is { FirstUnit: not null } s)
            {
                var type = cases.FirstOrDefault(c => c.Id == s.Base.PalletId);
                plan.Case = new CaseEngine.CaseSheet(a, type, type?.ToSpec() ?? new CaseSpec(), s, null);
                if (destination != null && s.CasesPerPallet > 0)
                {
                    var (box, palletSolution) = CaseEngine.PalletOfCases(a, s, plan.Case.Spec, type, null, destination, destinationConstraints,
                        "CAI-" + a.Code + (type != null ? "-" + type.Code : ""));
                    if (palletSolution != null)
                    {
                        plan.CasePallet = (box, CasePackaging(box, destination, destinationConstraints, palletSolution), palletSolution);
                    }
                }
            }
            else
            {
                plan.CaseWhy = "L'article ne tient dans aucune caisse du catalogue.";
            }
        }
        else
        {
            plan.CaseWhy = "Fiche article incomplète.";
        }

        // Conditionnement = colisage + palette de la caisse.
        if (plan.Case == null)
        {
            plan.PackagingWhy = plan.CaseWhy;
        }
        else if (a.CaseContent != null)
        {
            plan.PackagingOk = plan.Pallet != null;
            plan.PackagingWhy = plan.PalletWhy;
        }
        else
        {
            plan.PackagingOk = plan.CasePallet != null;
            plan.PackagingWhy = $"Caisse non palettisable sur {destination?.Code ?? "la palette de destination"}.";
        }

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
                var template = Template(target);
                var destination = DestinationPallet();
                var constraints = _main.Cases.DestinationConstraints;
                var cases = _main.Db.Cases.ToList();
                Task.Run(() =>
                {
                    try
                    {
                        return Build(target, template, destination, constraints, cases);
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
        var none = Article == null ? "Aucun article affiché." : null;
        PalletTip = "Ctrl+P · " + (CanPalletSheet() ? "Fiche palette de l'article (spécification, plans, plan de palettisation)."
            : none ?? (IsChecking ? checking : _plan.PalletWhy));
        CaseTip = CanCaseSheet() ? "Fiche de colisage : le produit dans sa caisse (quantité par caisse, poids brut, plans, vue 3D)."
            : none ?? (IsChecking ? checking : _plan.CaseWhy);
        PackagingTip = CanPackagingSheet() ? "Fiche de colisage puis fiche palette de la caisse, en un seul document."
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

    private bool CanPalletSheet() => ShownPackaging || LibraryPackaging != null || (!IsChecking && _plan.Pallet != null);

    private bool CanCaseSheet() => ShownColisage || (!IsChecking && _plan.Case != null);

    private bool CanPackagingSheet() => ShownColisage ? _main.Cases.CanPrintPackagingSheet || (!IsChecking && _plan.PackagingOk) : !IsChecking && _plan.PackagingOk;

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
        else if (_plan.Pallet is var (packaging, s) && Article is { } a)
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
            PrintService.PrintCaseSheet(sheet, Article?.CaseContent != null ? Article.Code : null, _main.Db, _main.ColorsFor(sheet.Content));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPackagingSheet))]
    private void PackagingSheet()
    {
        if (ShownColisage && _main.Cases.CanPrintPackagingSheet)
        {
            _main.Cases.PrintPackagingSheet();
            return;
        }

        if (_plan.Case is not { } sheet || Article is not { } a)
        {
            return;
        }

        if (a.CaseContent != null && _plan.Pallet is var (packaging, s))
        {
            PrintService.PrintPackagingSheet(sheet, a, packaging, s, s.FirstUnit!, _main.Db, _main.ColorsFor(sheet.Content), _main.ColorsFor(a));
        }
        else if (_plan.CasePallet is var (box, casePackaging, caseSolution))
        {
            PrintService.PrintPackagingSheet(sheet, box, casePackaging, caseSolution, caseSolution.FirstUnit!, _main.Db, _main.ColorsFor(sheet.Content),
                new Dictionary<Guid, Color> { [box.Id] = ArticleColors.Parse(box.Color, Color.FromRgb(0xC9, 0xA2, 0x6B)) });
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
