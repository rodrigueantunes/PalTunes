using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.App.ViewModels;

/// <summary>Client proposé dans une liste : <see cref="Code"/> est la valeur portée par l'article.</summary>
public sealed record ClientChoice(string? Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Choix d'un article précédé du choix du client : la liste des articles ne montre que ceux du client retenu
/// (« Tous les clients » par défaut). Les articles déjà choisis restent toujours dans la liste, pour que la sélection
/// ne soit jamais perdue en changeant de client.
/// </summary>
public sealed partial class ArticlePicker : ObservableObject
{
    public static readonly ClientChoice AllClients = new(null, "Tous les clients");
    public static readonly ClientChoice NoClient = new("", "(Sans client)");

    private readonly Func<Database> _db;
    private readonly Func<IEnumerable<Article?>> _selected;
    private bool _refreshing;

    public ArticlePicker(Func<Database> db, Func<IEnumerable<Article?>> selected)
    {
        _db = db;
        _selected = selected;
        _client = AllClients;
    }

    public ObservableCollection<ClientChoice> Clients { get; } = [];

    /// <summary>Articles du client retenu (plus les articles déjà choisis), triés par code.</summary>
    public ObservableCollection<Article> Articles { get; } = [];

    /// <summary>Client retenu (la liste déroulante peut le remettre à null pendant un rechargement).</summary>
    [ObservableProperty] private ClientChoice? _client;

    partial void OnClientChanged(ClientChoice? value)
    {
        if (!_refreshing && value != null)
        {
            Sync();
        }
    }

    /// <summary>Recharge les clients et les articles depuis la base.</summary>
    public void Refresh()
    {
        var db = _db();
        var choices = new List<ClientChoice> { AllClients };
        choices.AddRange(db.Clients.OrderBy(c => c.Code, StringComparer.CurrentCultureIgnoreCase).Select(c => new ClientChoice(c.Code, c.Label)));
        if (db.Articles.Any(a => db.FindClient(a.Client) == null))
        {
            choices.Add(NoClient);
        }

        var code = Client?.Code;
        _refreshing = true;
        Clients.Clear();
        foreach (var c in choices)
        {
            Clients.Add(c);
        }

        Client = Clients.FirstOrDefault(c => c.Code == code) ?? AllClients;
        _refreshing = false;
        Sync();
    }

    /// <summary>Met la liste des articles à jour sans retirer un article choisi (pas de perte de sélection).</summary>
    public void Sync(params Article?[] keep)
    {
        var db = _db();
        var kept = _selected().Concat(keep).Where(a => a != null).Select(a => a!).ToHashSet(ReferenceEqualityComparer.Instance);
        var target = Client?.Code;
        var client = target is { Length: > 0 } ? db.FindClient(target) : null;
        var desired = db.Articles
            .Where(a => target == null || (target.Length == 0 ? db.FindClient(a.Client) == null : client != null && db.IsClientOf(a, client)))
            .Concat(kept.Cast<Article>())
            .Distinct(ReferenceEqualityComparer.Instance).Cast<Article>()
            .OrderBy(a => a.Code, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id)
            .ToList();

        // Retraits puis insertions (les deux listes sont triées de la même façon) : un article choisi n'est jamais retiré.
        var wanted = desired.ToHashSet(ReferenceEqualityComparer.Instance);
        for (var i = Articles.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Articles[i]))
            {
                Articles.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            if (i < Articles.Count && ReferenceEquals(Articles[i], desired[i]))
            {
                continue;
            }

            var j = i + 1;
            while (j < Articles.Count && !ReferenceEquals(Articles[j], desired[i]))
            {
                j++;
            }

            if (j < Articles.Count)
            {
                Articles.Move(j, i); // code modifié : l'article change de rang
            }
            else
            {
                Articles.Insert(i, desired[i]);
            }
        }
    }
}
