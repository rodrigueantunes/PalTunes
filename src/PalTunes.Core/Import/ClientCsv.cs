using System.Text;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.Core.Import;

/// <summary>Import / export CSV de la base clients (même conventions que les articles : séparateur détecté, mise à jour par code).</summary>
public static class ClientCsv
{
    public static IReadOnlyList<ColumnDoc> Columns { get; } =
    [
        new("CODE", ["CODE_CLIENT", "CLIENT_CODE", "REF"], "Obligatoire", "Code unique du client. Un code existant est mis à jour.", "AGRO"),
        new("NOM", ["CLIENT", "RAISON_SOCIALE", "NAME", "LIBELLE"], "Obligatoire", "Nom affiché ; c'est lui qui relie les articles (colonne CLIENT des articles).", "Démo Agro"),
        new("ADRESSE", ["ADDRESS", "RUE"], "Facultatif", "Adresse.", ""),
        new("CODE_POSTAL", ["CP", "ZIP"], "Facultatif", "", "59000"),
        new("VILLE", ["CITY"], "Facultatif", "", "Lille"),
        new("PAYS", ["COUNTRY"], "Facultatif", "", "France"),
        new("CONTACT", [], "Facultatif", "Interlocuteur.", ""),
        new("TELEPHONE", ["TEL", "PHONE"], "Facultatif", "", ""),
        new("EMAIL", ["MAIL", "COURRIEL"], "Facultatif", "", ""),
        new("PALETTE", ["PALETTE_DEFAUT", "PALLET"], "Facultatif", "Code de la palette imposée par défaut (EUR1, CP3, PLA-EUR3…).", "EUR1"),
        new("HAUTEUR_MAX", ["HAUTEUR_MAXI", "MAX_HEIGHT"], "Facultatif", "Hauteur totale maximale imposée (mm, palette comprise).", "1800"),
        new("GERBAGE_MAX", ["GERBAGES", "NB_GERBAGES", "MAX_STACK"], "Facultatif", "Gerbages acceptés : 0 = non gerbable, 1 = un conditionnement gerbé dessus…", "0"),
        new("NOTES", ["COMMENTAIRE", "REMARQUES"], "Facultatif", "Texte libre.", "")
    ];

    public static ImportReport Import(string text, Database db)
    {
        var report = new ImportReport();
        var rows = Csv.Parse(text, out _);
        if (rows.Count == 0)
        {
            report.Lines.Add(new ImportLine { Status = "Erreur", Message = "Fichier vide.", IsError = true });
            return report;
        }

        var map = new Dictionary<string, int>();
        for (var i = 0; i < rows[0].Length; i++)
        {
            var h = Csv.NormalizeHeader(rows[0][i]);
            var col = Columns.FirstOrDefault(c => c.Name == h || c.Aliases.Contains(h));
            if (col == null)
            {
                if (h.Length > 0)
                {
                    report.UnknownColumns.Add(rows[0][i].Trim());
                }

                continue;
            }

            map.TryAdd(col.Name, i);
        }

        if (!map.ContainsKey("CODE") && !map.ContainsKey("NOM"))
        {
            report.Lines.Add(new ImportLine { Row = 1, Status = "Erreur", Message = "Colonnes CODE ou NOM introuvables.", IsError = true });
            return report;
        }

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Get(string name) => map.TryGetValue(name, out var i) && i < row.Length ? row[i].Trim() : "";
            bool Has(string name) => map.ContainsKey(name);
            string? Opt(string name) => Get(name) is { Length: > 0 } v ? v : null;

            var name = Get("NOM");
            var code = Get("CODE");
            if (code.Length == 0 && name.Length > 0)
            {
                code = Client.CodeFromName(name);
            }

            if (code.Length == 0)
            {
                report.Lines.Add(new ImportLine { Row = r + 1, Status = "Erreur", Message = "Code et nom vides.", IsError = true });
                continue;
            }

            var existing = db.Clients.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
            var client = existing?.Clone() ?? new Client { Code = code };
            var messages = new List<string>();
            if (name.Length > 0)
            {
                client.Name = name;
            }

            if (Has("ADRESSE")) client.Address = Opt("ADRESSE");
            if (Has("CODE_POSTAL")) client.PostalCode = Opt("CODE_POSTAL");
            if (Has("VILLE")) client.City = Opt("VILLE");
            if (Has("PAYS")) client.Country = Opt("PAYS");
            if (Has("CONTACT")) client.Contact = Opt("CONTACT");
            if (Has("TELEPHONE")) client.Phone = Opt("TELEPHONE");
            if (Has("EMAIL")) client.Email = Opt("EMAIL");
            if (Has("NOTES")) client.Notes = Opt("NOTES");
            if (Has("PALETTE"))
            {
                var palletCode = Get("PALETTE");
                var pallet = db.Pallets.FirstOrDefault(p => string.Equals(p.Code, palletCode, StringComparison.OrdinalIgnoreCase));
                if (palletCode.Length > 0 && pallet == null)
                {
                    messages.Add($"Palette « {palletCode} » inconnue.");
                }

                client.DefaultPalletId = pallet?.Id;
            }

            if (Has("HAUTEUR_MAX"))
            {
                client.MaxTotalHeight = Csv.TryParseNumber(Get("HAUTEUR_MAX"), out var h) ? h : null;
            }

            if (Has("GERBAGE_MAX"))
            {
                client.MaxStacking = Csv.TryParseNumber(Get("GERBAGE_MAX"), out var g) ? (int)Math.Round(g) : null;
            }

            // Le client est identifié par son code (mis à jour s'il existe) ; deux clients peuvent porter le même nom.
            var errors = client.Validate();

            if (messages.Count + errors.Count > 0)
            {
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Erreur", IsError = true, Message = string.Join(" ", messages.Concat(errors)) });
                continue;
            }

            client.ModifiedAt = DateTime.Now;
            if (existing != null)
            {
                db.Clients[db.Clients.IndexOf(existing)] = client;
                db.InvalidateClientIndex();
                report.Updated++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Mis à jour", Message = client.Name });
            }
            else
            {
                db.Clients.Add(client);
                report.Created++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Créé", Message = client.Name });
            }
        }

        return report;
    }

    public static string Export(Database db)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Csv.Line(Columns.Select(c => c.Name)));
        foreach (var c in db.Clients.OrderBy(c => c.Code))
        {
            sb.AppendLine(Csv.Line(
            [
                c.Code, c.Name, c.Address, c.PostalCode, c.City, c.Country, c.Contact, c.Phone, c.Email,
                db.FindPallet(c.DefaultPalletId)?.Code, c.MaxTotalHeight is { } h ? Csv.Number(h, "0") : "",
                c.MaxStacking?.ToString(), c.Notes
            ]));
        }

        return sb.ToString();
    }
}
