using System.Text.Json;
using System.Text.Json.Serialization;
using PalTunes.Core.Catalog;
using PalTunes.Core.Models;

namespace PalTunes.Core.Storage;

/// <summary>Base PalTunes : articles, palettes, conditionnements (un fichier JSON, partageable).</summary>
public sealed class Database
{
    public int SchemaVersion { get; set; } = 7;
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

    // Index des articles (identifiant → position) : publié d'un bloc, lisible depuis un calcul en tâche de fond.
    private (List<Article> List, int Count, Dictionary<Guid, int> Positions)? _articleIndex;

    public Article? FindArticle(Guid? id)
    {
        if (id == null)
        {
            return null;
        }

        var index = _articleIndex;
        if (index is not { } ix || !ReferenceEquals(ix.List, Articles) || ix.Count != Articles.Count)
        {
            index = RebuildArticleIndex();
        }

        // Position vérifiée (un article remplacé à la même place reste trouvé ; sinon index reconstruit une fois).
        if (index!.Value.Positions.TryGetValue(id.Value, out var i) && i < Articles.Count && Articles[i].Id == id)
        {
            return Articles[i];
        }

        index = RebuildArticleIndex();
        return index.Value.Positions.TryGetValue(id.Value, out i) && i < Articles.Count ? Articles[i] : null;
    }

    private (List<Article>, int, Dictionary<Guid, int>) RebuildArticleIndex()
    {
        var list = Articles;
        var positions = new Dictionary<Guid, int>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            positions.TryAdd(list[i].Id, i);
        }

        var index = (list, list.Count, positions);
        _articleIndex = index;
        return index;
    }
    public PalletType? FindPallet(Guid? id) => id == null ? null : Pallets.FirstOrDefault(p => p.Id == id);

    // Index des clients (code, nom) : les bases importées comptent des milliers de clients et d'articles.
    private Dictionary<string, Client>? _byCode;
    private Dictionary<string, Client>? _byName;
    private List<Client>? _indexedList;
    private int _indexedCount = -1;

    /// <summary>À appeler après avoir remplacé ou modifié un client de la liste (code ou nom).</summary>
    public void InvalidateClientIndex() => _byCode = null;

    private void EnsureClientIndex()
    {
        if (_byCode != null && ReferenceEquals(_indexedList, Clients) && _indexedCount == Clients.Count)
        {
            return;
        }

        _byCode = new Dictionary<string, Client>(StringComparer.OrdinalIgnoreCase);
        _byName = new Dictionary<string, Client>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var c in Clients)
        {
            _byCode.TryAdd(c.Code?.Trim() ?? "", c);
            _byName.TryAdd(c.Name?.Trim() ?? "", c);
        }

        _indexedList = Clients;
        _indexedCount = Clients.Count;
    }

    /// <summary>Client référencé par un article : par son code, à défaut par son nom (anciennes bases, saisie libre).</summary>
    public Client? FindClient(string? codeOrName)
    {
        if (string.IsNullOrWhiteSpace(codeOrName))
        {
            return null;
        }

        var key = codeOrName.Trim();
        EnsureClientIndex();
        if (_byCode!.TryGetValue(key, out var c) && string.Equals(c.Code?.Trim(), key, StringComparison.OrdinalIgnoreCase))
        {
            return c;
        }

        if (_byName!.TryGetValue(key, out c) && string.Equals(c.Name?.Trim(), key, StringComparison.CurrentCultureIgnoreCase))
        {
            return c;
        }

        // Index périmé (client modifié sur place) : reconstruit puis relu une fois.
        if (c != null)
        {
            InvalidateClientIndex();
            EnsureClientIndex();
            return _byCode!.TryGetValue(key, out c) ? c : _byName!.TryGetValue(key, out c) ? c : null;
        }

        return null;
    }

    /// <summary>Nombre d'articles de chaque client (une seule passe sur les articles).</summary>
    public Dictionary<Guid, int> ArticleCountsByClient()
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var a in Articles)
        {
            if (FindClient(a.Client) is { } c)
            {
                counts[c.Id] = counts.GetValueOrDefault(c.Id) + 1;
            }
        }

        return counts;
    }

    /// <summary>Libellé d'un client référencé par un article : « CODE - Nom » (la valeur brute si le client est inconnu).</summary>
    public string ClientLabel(string? code) => FindClient(code) is { } c ? c.Label : code?.Trim() ?? "";

    public bool IsClientOf(Article a, Client c) => FindClient(a.Client)?.Id == c.Id;

    public IEnumerable<Article> ArticlesOf(Client c) => Articles.Where(a => IsClientOf(a, c));

    /// <summary>
    /// Les articles référencent leur client par son code (v0.0.6) : un nom (anciennes bases, import) est remplacé par le
    /// code du client correspondant. Renvoie le nombre d'articles modifiés.
    /// </summary>
    public int NormalizeClientReferences()
    {
        var count = 0;
        foreach (var a in Articles)
        {
            if (string.IsNullOrWhiteSpace(a.Client))
            {
                if (a.Client != null)
                {
                    a.Client = null;
                    count++;
                }

                continue;
            }

            if (FindClient(a.Client) is { } c && !string.Equals(a.Client, c.Code, StringComparison.Ordinal))
            {
                a.Client = c.Code;
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Crée les clients cités par des articles mais absents de la base (import CSV) : la valeur citée devient le code,
    /// et le nom en attendant qu'il soit renseigné. Les références par nom sont ensuite converties en codes.
    /// </summary>
    public int MergeClients()
    {
        var added = 0;
        foreach (var code in Articles.Select(a => a.Client?.Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (FindClient(code) != null)
            {
                continue;
            }

            Clients.Add(new Client { Code = code!, Name = code! });
            added++;
        }

        NormalizeClientReferences();
        return added;
    }

    /// <summary>
    /// Caisses créées au colisage avant la 0.1.3, sans quantité par caisse ni contenu : tout est repris de la fiche créée
    /// à l'époque — désignation « Carton 800 × 600 × 400 de 550 × BAG0000001 » (quantité, produit), code
    /// « CAI-produit-caisse » (caisse du catalogue), notes « Intérieur 786 × 586 × 386 mm, paroi 7 mm » et position
    /// (couché, debout). Caisse mixte « … mixte : 4 × A + 2 × B » : quantité totale. Renvoie le nombre de caisses complétées.
    /// </summary>
    public int CompleteLegacyCaseArticles()
    {
        var count = 0;
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        foreach (var box in Articles.Where(a => a.Kind == ArticleKind.Caisse && a.CaseContent == null && !string.IsNullOrWhiteSpace(a.Designation)).ToList())
        {
            var designation = box.Designation!.Trim();
            var mixed = System.Text.RegularExpressions.Regex.Match(designation, @"^(?<name>.*?)\s*\bmixte\s*:\s*(?<content>.+)$");
            if (mixed.Success)
            {
                var parts = System.Text.RegularExpressions.Regex.Matches(mixed.Groups["content"].Value, @"(?<q>\d+)\s*×\s*(?<code>[^\s+]+)")
                    .Select(m => (Quantity: int.Parse(m.Groups["q"].Value), Code: m.Groups["code"].Value)).ToList();
                var total = parts.Sum(x => x.Quantity);
                if (box.QuantityPerCase == null && total > 0)
                {
                    box.QuantityPerCase = total;
                    count++;
                }

                // Contenu détaillé (0.1.9) : articles retrouvés par leur code, caisse par le code « CAI-MIX-caisse » ou son nom.
                var found = parts.Select(x =>
                {
                    var candidates = Articles.Where(a => a.Id != box.Id && string.Equals(a.Code, x.Code, StringComparison.OrdinalIgnoreCase)).ToList();
                    return (x.Quantity, Article: candidates.FirstOrDefault(a => string.Equals(a.Client, box.Client, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault());
                }).ToList();
                if (found.Count == 0 || found.Any(x => x.Article == null))
                {
                    continue;
                }

                const string mixPrefix = "CAI-MIX-";
                var mixType = box.Code.StartsWith(mixPrefix, StringComparison.OrdinalIgnoreCase)
                    ? Cases.Where(c => box.Code.Substring(mixPrefix.Length).StartsWith(c.Code, StringComparison.OrdinalIgnoreCase) &&
                                       (box.Code.Length == mixPrefix.Length + c.Code.Length || box.Code[mixPrefix.Length + c.Code.Length] == '-'))
                        .MaxBy(c => c.Code.Length)
                    : null;
                mixType ??= Cases.FirstOrDefault(c => string.Equals(c.Name, mixed.Groups["name"].Value.Trim(), StringComparison.OrdinalIgnoreCase));
                if (InnerOf(box.Notes, mixType, fr) is not (var ml, var mw, var mh, var mt))
                {
                    continue;
                }

                var mixTare = mixType?.Tare ?? Math.Max(0, Math.Round(box.Weight - found.Sum(x => x.Quantity * x.Article!.Weight), 5));
                var ordered = found.OrderByDescending(x => x.Quantity).ToList();
                box.CaseContent = new CaseContent(ordered[0].Article!.Id, mixType?.Code, ml, mw, mh, mt, mixTare, 0, null,
                    ordered.Select(x => new CaseContentLine(x.Article!.Id, x.Quantity)).ToList());
                count++;
                continue;
            }

            var single = System.Text.RegularExpressions.Regex.Match(designation, @"^(?<name>.*?)\s+de\s+(?<q>\d+)\s*×\s*(?<code>\S+)$");
            if (!single.Success)
            {
                continue;
            }

            var quantity = int.Parse(single.Groups["q"].Value);
            var code = single.Groups["code"].Value;
            var candidates = Articles.Where(a => a.Id != box.Id && string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase)).ToList();
            var content = candidates.FirstOrDefault(a => string.Equals(a.Client, box.Client, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
            box.QuantityPerCase ??= quantity;
            count++;
            if (content == null)
            {
                continue;
            }

            // Caisse du catalogue : suffixe du code « CAI-produit-caisse », sinon nom de la caisse en tête de désignation.
            var prefix = $"CAI-{content.Code}-";
            var type = box.Code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? Cases.FirstOrDefault(c => box.Code.Substring(prefix.Length).StartsWith(c.Code, StringComparison.OrdinalIgnoreCase) &&
                                            (box.Code.Length == prefix.Length + c.Code.Length || box.Code[prefix.Length + c.Code.Length] == '-'))
                : null;
            type ??= Cases.FirstOrDefault(c => string.Equals(c.Name, single.Groups["name"].Value.Trim(), StringComparison.OrdinalIgnoreCase));

            var notes = box.Notes ?? "";
            if (InnerOf(notes, type, fr) is not (var l, var w, var h, var t))
            {
                continue;
            }

            CoilAxis? axis = content.Kind is ArticleKind.Tube or ArticleKind.Bobine
                ? notes.Contains("Couché", StringComparison.OrdinalIgnoreCase) || notes.Contains("couchés", StringComparison.OrdinalIgnoreCase) ? CoilAxis.Horizontal
                : notes.Contains("debout", StringComparison.OrdinalIgnoreCase) || notes.Contains("vertical", StringComparison.OrdinalIgnoreCase) ? CoilAxis.Vertical
                : null
                : null;
            var tare = type?.Tare ?? Math.Max(0, Math.Round(box.Weight - quantity * content.Weight, 5));
            box.CaseContent = new CaseContent(content.Id, type?.Code, l, w, h, t, tare, 0, axis);
        }

        return count;
    }

    /// <summary>Dimensions intérieures et paroi : notes « Intérieur 786 × 586 × 386 mm, paroi 7 mm », sinon la caisse du catalogue.</summary>
    private static (double L, double W, double H, double T)? InnerOf(string? notes, CaseType? type, System.Globalization.CultureInfo fr)
    {
        static double Num(string v, System.Globalization.CultureInfo fr) =>
            double.TryParse(v.Replace('.', ','), System.Globalization.NumberStyles.Float, fr, out var d) ? d : 0;
        var inner = System.Text.RegularExpressions.Regex.Match(notes ?? "", @"Intérieur\s+(?<l>[\d.,]+)\s*×\s*(?<w>[\d.,]+)\s*×\s*(?<h>[\d.,]+)\s*mm,\s*paroi\s+(?<t>[\d.,]+)\s*mm");
        if (inner.Success)
        {
            return (Num(inner.Groups["l"].Value, fr), Num(inner.Groups["w"].Value, fr), Num(inner.Groups["h"].Value, fr), Num(inner.Groups["t"].Value, fr));
        }

        return type == null ? null : (type.InnerLength, type.InnerWidth, type.InnerHeight, type.WallThickness);
    }

    /// <summary>
    /// Tubes importés avant la v0.0.7 avec une HAUTEUR : c'était l'épaisseur de paroi (la hauteur n'a pas de sens pour un
    /// tube). Elle devient le diamètre intérieur (Ø − 2 × épaisseur), comme à l'import. Renvoie le nombre de tubes convertis.
    /// </summary>
    public int ConvertTubeWallThickness()
    {
        var count = 0;
        foreach (var a in Articles.Where(a => a.Kind == ArticleKind.Tube && a.Height > 0 && a.InnerDiameter <= 0 && a.Diameter > 0))
        {
            if (a.Height < a.Diameter / 2)
            {
                a.InnerDiameter = Math.Round(a.Diameter - 2 * a.Height, 3);
            }

            a.Height = 0;
            count++;
        }

        return count;
    }

    /// <summary>Répercute le changement de code d'un client sur ses articles. Renvoie le nombre d'articles modifiés.</summary>
    public int RenameClientCode(string oldCode, string newCode)
    {
        InvalidateClientIndex();
        var count = 0;
        foreach (var a in Articles.Where(a => string.Equals(a.Client?.Trim(), oldCode.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            a.Client = newCode;
            count++;
        }

        return count;
    }

    public int ArticleCountOf(Client c) => ArticlesOf(c).Count();

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

        // Valeurs calculées (FirstUnit, TotalItems, MaxX…) : recalculées à la lecture, jamais écrites.
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DatabaseStore(string path) => Path = path;

    /// <summary>
    /// Fichiers que .NET lit au démarrage du programme : une base enregistrée sous l'un de ces noms rend l'application
    /// impossible à lancer (plantage de l'hôte .NET, hostpolicy.dll). Ils sont donc interdits comme base.
    /// </summary>
    public static bool IsReservedFileName(string path)
    {
        var name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return name.EndsWith(".deps.json", StringComparison.Ordinal) ||
               name.EndsWith(".runtimeconfig.json", StringComparison.Ordinal) ||
               name.EndsWith(".runtimeconfig.dev.json", StringComparison.Ordinal) ||
               name is "appsettings.json" or "settings.json";
    }

    /// <summary>Raison pour laquelle un fichier ne peut pas servir de base (null = utilisable).</summary>
    public static string? CheckUsable(string path, bool mustExist)
    {
        if (IsReservedFileName(path))
        {
            return $"« {System.IO.Path.GetFileName(path)} » est un fichier réservé (paramètres ou démarrage du programme) : " +
                   "l'utiliser comme base empêcherait PalTunes de démarrer. Choisissez un autre nom, par exemple « base-paltunes.json ».";
        }

        if (!File.Exists(path))
        {
            return mustExist ? $"Le fichier {path} n'existe pas." : null;
        }

        return LooksLikeDatabase(path)
            ? null
            : $"« {System.IO.Path.GetFileName(path)} » n'est pas une base PalTunes : il ne sera ni ouvert ni remplacé.";
    }

    /// <summary>Base PalTunes : objet JSON avec au moins une collection connue (ou un fichier vide).</summary>
    public static bool LooksLikeDatabase(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.TryGetProperty("runtimeTarget", out _) ||
                doc.RootElement.TryGetProperty("runtimeOptions", out _))
            {
                return false;
            }

            return new[] { "SchemaVersion", "Articles", "Pallets", "Packagings", "Clients", "Cases" }
                .Any(p => doc.RootElement.TryGetProperty(p, out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Copie horodatée de la base avant une remise à zéro (même dossier). Renvoie le chemin de la copie.</summary>
    public string? Backup()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        var name = System.IO.Path.GetFileNameWithoutExtension(Path);
        var target = System.IO.Path.Combine(folder, $"{name}.sauvegarde-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.Copy(Path, target, overwrite: false);
        return target;
    }

    public string Path { get; private set; }

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PalTunes", "paltunes-base.json");

    public Database Load(bool seedDemoIfMissing = true)
    {
        if (CheckUsable(Path, mustExist: false) is { } problem)
        {
            throw new InvalidDataException(problem);
        }

        if (!File.Exists(Path) || new FileInfo(Path).Length == 0)
        {
            var db = Database.CreateDefault(seedDemoIfMissing);
            Save(db);
            return db;
        }

        var loaded = JsonSerializer.Deserialize<Database>(File.ReadAllText(Path), Json) ?? new Database();
        if (loaded.SchemaVersion < new Database().SchemaVersion)
        {
            // Base d'une version précédente : copie de sauvegarde avant toute conversion.
            try
            {
                Backup();
            }
            catch (IOException)
            {
            }
        }

        // Avant la v0.0.6, les articles citaient le nom du client : conversion en code (avant de créer les absents).
        var changed = loaded.NormalizeClientReferences() + loaded.MergeCatalog() + loaded.MergeClients() + loaded.MergeCaseCatalog();
        if (loaded.SchemaVersion < 5)
        {
            loaded.ConvertTubeWallThickness();
            loaded.SchemaVersion = 5;
            changed++;
        }

        if (loaded.SchemaVersion < 6)
        {
            // Caisses créées au colisage avant la 0.1.3 : quantité par caisse et contenu repris de la fiche.
            loaded.CompleteLegacyCaseArticles();
            loaded.SchemaVersion = 6;
            changed++;
        }

        if (loaded.SchemaVersion < 7)
        {
            // Caisses mixtes créées avant la 0.1.9 : articles × quantités et caisse repris de la fiche (fiche de colisage complète).
            loaded.CompleteLegacyCaseArticles();
            loaded.SchemaVersion = 7;
            changed++;
        }

        if (changed > 0)
        {
            Save(loaded);
        }

        return loaded;
    }

    public void Save(Database db)
    {
        if (IsReservedFileName(Path))
        {
            throw new InvalidDataException(CheckUsable(Path, mustExist: false));
        }

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
