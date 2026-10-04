using System.Text.Json;
using System.Text.Json.Serialization;
using PalTunes.Core.Catalog;
using PalTunes.Core.Models;

namespace PalTunes.Core.Storage;

/// <summary>Base PalTunes : articles, palettes, conditionnements (un fichier JSON, partageable).</summary>
public sealed class Database
{
    public int SchemaVersion { get; set; } = 3;
    public List<Client> Clients { get; set; } = [];
    public List<Article> Articles { get; set; } = [];
    public List<PalletType> Pallets { get; set; } = [];
    public List<Packaging> Packagings { get; set; } = [];
    public List<CaseType> Cases { get; set; } = [];

    public CaseType? FindCase(Guid? id) => id == null ? null : Cases.FirstOrDefault(c => c.Id == id);

    /// <summary>Ajoute les caisses du catalogue absentes, sans toucher aux caisses modifiées.</summary>
    public int MergeCaseCatalog()
    {
        var added = 0;
        foreach (var c in CaseCatalog.Defaults())
        {
            if (Cases.All(x => x.Id != c.Id && !string.Equals(x.Code, c.Code, StringComparison.OrdinalIgnoreCase)))
            {
                Cases.Add(c);
                added++;
            }
        }

        return added;
    }

    public Article? FindArticle(Guid? id) => id == null ? null : Articles.FirstOrDefault(a => a.Id == id);
    public PalletType? FindPallet(Guid? id) => id == null ? null : Pallets.FirstOrDefault(p => p.Id == id);

    public Client? FindClient(string? name) => string.IsNullOrWhiteSpace(name)
        ? null
        : Clients.FirstOrDefault(c => string.Equals(c.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Crée les clients cités par des articles mais absents de la base (import CSV, anciennes bases).</summary>
    public int MergeClients()
    {
        var added = 0;
        foreach (var name in Articles.Select(a => a.Client?.Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            if (FindClient(name) != null)
            {
                continue;
            }

            var code = Client.CodeFromName(name!);
            for (var i = 2; Clients.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)); i++)
            {
                code = Client.CodeFromName(name!) + i;
            }

            Clients.Add(new Client { Code = code, Name = name! });
            added++;
        }

        return added;
    }

    /// <summary>Répercute le renommage d'un client sur ses articles. Renvoie le nombre d'articles modifiés.</summary>
    public int RenameClient(string oldName, string newName)
    {
        var count = 0;
        foreach (var a in Articles.Where(a => string.Equals(a.Client?.Trim(), oldName.Trim(), StringComparison.CurrentCultureIgnoreCase)))
        {
            a.Client = newName;
            count++;
        }

        return count;
    }

    public int ArticleCountOf(Client c) =>
        Articles.Count(a => string.Equals(a.Client?.Trim(), c.Name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Ajoute les palettes du catalogue absentes (nouvelles versions), sans toucher aux palettes modifiées.</summary>
    public int MergeCatalog()
    {
        var added = 0;
        foreach (var p in PalletCatalog.Defaults())
        {
            if (Pallets.All(x => x.Id != p.Id && !string.Equals(x.Code, p.Code, StringComparison.OrdinalIgnoreCase)))
            {
                Pallets.Add(p);
                added++;
            }
        }

        return added;
    }

    public static Database CreateDefault(bool withDemo)
    {
        var db = new Database();
        db.Pallets.AddRange(PalletCatalog.Defaults());
        db.Cases.AddRange(CaseCatalog.Defaults());
        if (withDemo)
        {
            DemoData.Fill(db);
        }

        db.MergeClients();

        return db;
    }
}

/// <summary>Lecture / écriture de la base (écriture atomique : fichier temporaire puis remplacement).</summary>
public sealed class DatabaseStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public DatabaseStore(string path) => Path = path;

    public string Path { get; private set; }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PalTunes", "paltunes-base.json");

    public Database Load(bool seedDemoIfMissing = true)
    {
        if (!File.Exists(Path))
        {
            var db = Database.CreateDefault(seedDemoIfMissing);
            Save(db);
            return db;
        }

        var loaded = JsonSerializer.Deserialize<Database>(File.ReadAllText(Path), Json) ?? new Database();
        if (loaded.MergeCatalog() + loaded.MergeClients() + loaded.MergeCaseCatalog() > 0)
        {
            Save(loaded);
        }

        return loaded;
    }

    public void Save(Database db)
    {
        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(db, Json));
        File.Move(temp, Path, overwrite: true);
    }

    public void ChangePath(string path) => Path = path;
}
