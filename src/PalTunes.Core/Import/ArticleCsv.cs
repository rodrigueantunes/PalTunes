using System.Text;
using PalTunes.Core.Models;

namespace PalTunes.Core.Import;

/// <summary>Colonne du fichier articles : nom, synonymes acceptés, caractère obligatoire, explication.</summary>
public sealed record ColumnDoc(string Name, string[] Aliases, string Requirement, string Description, string Example);

public sealed class ImportLine
{
    public int Row { get; init; }
    public string Code { get; init; } = "";
    public string Status { get; init; } = "";
    public string Message { get; init; } = "";
    public bool IsError { get; init; }
}

public sealed class ImportReport
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Errors => Lines.Count(l => l.IsError);
    public List<ImportLine> Lines { get; } = [];
    public List<string> UnknownColumns { get; } = [];
    public string Summary => $"{Created} créé(s), {Updated} mis à jour, {Errors} erreur(s)";
}

/// <summary>Import / export CSV de la base articles (étude §13, docs/FORMAT_IMPORT_ARTICLES.md).</summary>
public static class ArticleCsv
{
    public static IReadOnlyList<ColumnDoc> Columns { get; } =
    [
        new("CODE", ["CODE_ARTICLE", "ARTICLE", "REF", "REFERENCE", "ITEM"], "Obligatoire", "Code unique de l'article. Un code existant est mis à jour.", "CAR-400"),
        new("TYPE", ["TYPE_ARTICLE", "NATURE", "KIND"], "Obligatoire", "CAISSE, BOBINE, TUBE, PLAQUE, SAC, FUT, BAC ou AUTRE (synonymes acceptés : CARTON, ROULEAU, PANNEAU…).", "CAISSE"),
        new("LONGUEUR", ["L", "LONG", "LENGTH"], "Selon type", "mm. Caisse, sac, bac, plaque, autre ; longueur du tube.", "400"),
        new("LARGEUR", ["LARG", "WIDTH", "LAIZE"], "Selon type", "mm. Caisse, sac, bac, plaque, autre ; laize de la bobine.", "300"),
        new("HAUTEUR", ["H", "HAUT", "HEIGHT", "EPAISSEUR", "EP"], "Selon type", "mm. Hauteur (caisse, sac, bac, fût, autre) ou épaisseur (plaque).", "250"),
        new("DIAMETRE", ["DIAM", "D", "DIAMETRE_EXT", "DIAMETER", "OD"], "Selon type", "mm. Diamètre extérieur (bobine, tube, fût).", ""),
        new("DIAMETRE_INT", ["MANDRIN", "DIAM_INT", "ID", "CORE"], "Facultatif", "mm. Diamètre du mandrin d'une bobine.", ""),
        new("POIDS", ["POIDS_KG", "MASSE", "WEIGHT", "KG"], "Obligatoire", "kg par article.", "12,5"),
        new("ORIENTATION", ["HAUT_IMPOSE", "ROTATION"], "Facultatif", "HAUT_IMPOSE (défaut) ou LIBRE : caisses et « autre ».", "HAUT_IMPOSE"),
        new("AXE", ["AXE_BOBINE", "AXE_TUBE", "AXIS"], "Facultatif", "VERTICAL, HORIZONTAL ou INDIFFERENT : bobines (défaut VERTICAL) et tubes (défaut INDIFFERENT, le meilleur est proposé).", ""),
        new("CHARGE_MAX", ["CHARGE_MAX_DESSUS", "GERBABILITE", "LOAD_ON_TOP"], "Facultatif", "kg supportables par un exemplaire (résistance au gerbage).", "80"),
        new("COUCHES_MAX", ["NB_COUCHES_MAX", "MAX_LAYERS"], "Facultatif", "Nombre maximal de couches superposées de ce produit.", ""),
        new("FRAGILE", [], "Facultatif", "OUI / NON : rien ne sera posé dessus.", "NON"),
        new("DESIGNATION", ["LIBELLE", "DESCRIPTION", "NOM"], "Facultatif", "Libellé de l'article.", "Carton 400 × 300"),
        new("CLIENT", ["NOM_CLIENT", "CUSTOMER"], "Facultatif", "Client : 1er niveau de l'arborescence par défaut.", "Client A"),
        new("FAMILLE", ["FAMILY", "GROUPE"], "Facultatif", "Famille d'articles.", "Emballages"),
        new("SOUS_FAMILLE", ["SOUSFAMILLE", "SUB_FAMILY"], "Facultatif", "Sous-famille.", "Cartons"),
        new("REF_CLIENT", ["REFERENCE_CLIENT", "CUSTOMER_REF"], "Facultatif", "Référence de l'article chez le client.", ""),
        new("EAN", ["GTIN", "CODE_BARRE"], "Facultatif", "Code-barres.", ""),
        new("COULEUR", ["COLOR"], "Facultatif", "Couleur d'affichage #RRGGBB.", "#5DADE2"),
        new("NOTES", ["COMMENTAIRE", "REMARQUES"], "Facultatif", "Texte libre.", "")
    ];

    public static ArticleKind? ParseKind(string? text)
    {
        var t = Csv.NormalizeHeader(text ?? "");
        return t switch
        {
            "CAISSE" or "CARTON" or "COLIS" or "BOX" or "CASE" or "CAISSE_CARTON" => ArticleKind.Caisse,
            "BOBINE" or "ROULEAU" or "COIL" or "REEL" or "ROLL" => ArticleKind.Bobine,
            "TUBE" or "PROFILE" or "BARRE" or "PIPE" => ArticleKind.Tube,
            "PLAQUE" or "PLANCHE" or "FEUILLE" or "PANNEAU" or "SHEET" or "PLATE" => ArticleKind.Plaque,
            "SAC" or "SACHET" or "BAG" => ArticleKind.Sac,
            "FUT" or "BIDON" or "TONNEAU" or "DRUM" => ArticleKind.Fut,
            "BAC" or "CAISSE_PLASTIQUE" or "CONTENANT" or "CRATE" or "BAC_CAISSE_PLASTIQUE" => ArticleKind.Bac,
            "AUTRE" or "OTHER" => ArticleKind.Autre,
            _ => null
        };
    }

    private static bool ParseBool(string text)
    {
        var t = Csv.NormalizeHeader(text);
        return t is "OUI" or "O" or "1" or "TRUE" or "VRAI" or "X" or "YES" or "Y";
    }

    public static ImportReport Import(string text, IList<Article> database)
    {
        var report = new ImportReport();
        var rows = Csv.Parse(text, out _);
        if (rows.Count == 0)
        {
            report.Lines.Add(new ImportLine { Row = 0, Status = "Erreur", Message = "Fichier vide.", IsError = true });
            return report;
        }

        var map = new Dictionary<string, int>();
        var header = rows[0];
        for (var i = 0; i < header.Length; i++)
        {
            var h = Csv.NormalizeHeader(header[i]);
            var col = Columns.FirstOrDefault(c => c.Name == h || c.Aliases.Contains(h));
            if (col == null)
            {
                if (h.Length > 0)
                {
                    report.UnknownColumns.Add(header[i].Trim());
                }

                continue;
            }

            map.TryAdd(col.Name, i);
        }

        if (!map.ContainsKey("CODE"))
        {
            report.Lines.Add(new ImportLine { Row = 1, Status = "Erreur", Message = "Colonne CODE introuvable dans l'en-tête.", IsError = true });
            return report;
        }

        var byCode = database.GroupBy(a => a.Code.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Get(string name) => map.TryGetValue(name, out var i) && i < row.Length ? row[i].Trim() : "";
            bool Has(string name) => Get(name).Length > 0;

            var code = Get("CODE");
            if (code.Length == 0)
            {
                report.Lines.Add(new ImportLine { Row = r + 1, Status = "Erreur", Message = "Code vide.", IsError = true });
                continue;
            }

            var exists = byCode.TryGetValue(code, out var existing);
            var article = exists ? existing!.Clone() : new Article { Code = code };
            var messages = new List<string>();

            if (Has("TYPE"))
            {
                var kind = ParseKind(Get("TYPE"));
                if (kind == null)
                {
                    report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Erreur", Message = $"Type « {Get("TYPE")} » inconnu.", IsError = true });
                    continue;
                }

                if (!exists || article.Kind != kind)
                {
                    article.CoilAxis = ArticleSchema.DefaultAxis(kind.Value);
                }

                article.Kind = kind.Value;
            }
            else if (!exists)
            {
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Erreur", Message = "Colonne TYPE obligatoire pour un nouvel article.", IsError = true });
                continue;
            }

            void Num(string column, Action<double> set)
            {
                if (!Has(column))
                {
                    return;
                }

                if (Csv.TryParseNumber(Get(column), out var v))
                {
                    set(v);
                }
                else
                {
                    messages.Add($"{column} « {Get(column)} » n'est pas un nombre.");
                }
            }

            Num("LONGUEUR", v => article.Length = v);
            Num("LARGEUR", v => article.Width = v);
            Num("HAUTEUR", v => article.Height = v);
            Num("DIAMETRE", v => article.Diameter = v);
            Num("DIAMETRE_INT", v => article.InnerDiameter = v);
            Num("POIDS", v => article.Weight = v);
            Num("CHARGE_MAX", v => article.MaxLoadOnTop = v);
            Num("COUCHES_MAX", v => article.MaxLayers = (int)Math.Round(v));

            if (Has("ORIENTATION"))
            {
                var o = Csv.NormalizeHeader(Get("ORIENTATION"));
                article.Orientation = o is "LIBRE" or "NON" or "FREE" or "NO" or "0" ? OrientationRule.Libre : OrientationRule.HautImpose;
            }

            if (Has("AXE"))
            {
                var o = Csv.NormalizeHeader(Get("AXE"));
                article.CoilAxis = o.StartsWith('H') ? CoilAxis.Horizontal : o.StartsWith('I') || o.StartsWith("LIB") ? CoilAxis.Indifferent : CoilAxis.Vertical;
            }

            if (Has("FRAGILE"))
            {
                article.Fragile = ParseBool(Get("FRAGILE"));
            }

            string? Opt(string column) => map.ContainsKey(column) ? (Has(column) ? Get(column) : null) : null;
            void Text(string column, Action<string?> set)
            {
                if (map.ContainsKey(column))
                {
                    set(Opt(column));
                }
            }

            Text("DESIGNATION", v => article.Designation = v);
            Text("CLIENT", v => article.Client = v);
            Text("FAMILLE", v => article.Family = v);
            Text("SOUS_FAMILLE", v => article.SubFamily = v);
            Text("REF_CLIENT", v => article.CustomerRef = v);
            Text("EAN", v => article.Ean = v);
            Text("COULEUR", v => article.Color = v);
            Text("NOTES", v => article.Notes = v);

            var errors = ArticleSchema.Validate(article);
            if (messages.Count > 0 || errors.Count > 0)
            {
                report.Lines.Add(new ImportLine
                {
                    Row = r + 1, Code = code, Status = "Erreur", IsError = true,
                    Message = string.Join(" ", messages.Concat(errors))
                });
                continue;
            }

            article.ModifiedAt = DateTime.Now;
            if (exists)
            {
                var index = database.IndexOf(existing!);
                database[index] = article;
                byCode[code] = article;
                report.Updated++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Mis à jour", Message = article.DimensionsText });
            }
            else
            {
                database.Add(article);
                byCode[code] = article;
                report.Created++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Créé", Message = article.DimensionsText });
            }
        }

        return report;
    }

    public static string KindCode(ArticleKind kind) => kind.ToString().ToUpperInvariant();

    /// <summary>Export de la base (même format que l'import : réimportable).</summary>
    public static string Export(IEnumerable<Article> articles)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Csv.Line(Columns.Select(c => c.Name)));
        foreach (var a in articles)
        {
            sb.AppendLine(Csv.Line(Values(a)));
        }

        return sb.ToString();
    }

    private static IEnumerable<string?> Values(Article a)
    {
        string N(double v) => v > 0 ? Csv.Number(v, "0.###") : "";
        string W(double v) => v > 0 ? Csv.Number(v, Formats.UnitWeight) : "";
        yield return a.Code;
        yield return KindCode(a.Kind);
        yield return N(a.Length);
        yield return N(a.Width);
        yield return N(a.Height);
        yield return N(a.Diameter);
        yield return N(a.InnerDiameter);
        yield return W(a.Weight);
        yield return ArticleSchema.UsesOrientation(a.Kind) ? (a.Orientation == OrientationRule.Libre ? "LIBRE" : "HAUT_IMPOSE") : "";
        yield return ArticleSchema.UsesCoilAxis(a.Kind) ? a.CoilAxis.ToString().ToUpperInvariant() : "";
        yield return a.MaxLoadOnTop is { } m ? Csv.Number(m, "0.###") : "";
        yield return a.MaxLayers?.ToString() ?? "";
        yield return a.Fragile ? "OUI" : "NON";
        yield return a.Designation;
        yield return a.Client;
        yield return a.Family;
        yield return a.SubFamily;
        yield return a.CustomerRef;
        yield return a.Ean;
        yield return a.Color;
        yield return a.Notes;
    }

    /// <summary>Modèle à remplir : toutes les colonnes et un exemple par type d'article.</summary>
    public static string Template()
    {
        var examples = new[]
        {
            new Article { Code = "CAR-400", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12, MaxLoadOnTop = 90, Designation = "Carton 400 × 300 × 250", Client = "Client A", Family = "Emballages", SubFamily = "Cartons" },
            new Article { Code = "BOB-1000", Kind = ArticleKind.Bobine, Diameter = 1000, Width = 700, InnerDiameter = 76, Weight = 380, CoilAxis = CoilAxis.Vertical, Designation = "Bobine film Ø1000 laize 700", Client = "Client A", Family = "Films" },
            new Article { Code = "TUB-110", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 4.5, CoilAxis = CoilAxis.Indifferent, Designation = "Tube PVC Ø110 × 1200", Client = "Client B", Family = "Tubes" },
            new Article { Code = "PLQ-1600", Kind = ArticleKind.Plaque, Length = 1600, Width = 1200, Height = 10, Weight = 15, Designation = "Plaque 1600 × 1200 ép. 10", Client = "Client B", Family = "Plaques" },
            new Article { Code = "SAC-25", Kind = ArticleKind.Sac, Length = 600, Width = 400, Height = 120, Weight = 25, Designation = "Sac 25 kg", Client = "Client C", Family = "Vrac" },
            new Article { Code = "FUT-200", Kind = ArticleKind.Fut, Diameter = 585, Height = 880, Weight = 220, Designation = "Fût 200 L", Client = "Client C", Family = "Liquides" },
            new Article { Code = "BAC-6040", Kind = ArticleKind.Bac, Length = 600, Width = 400, Height = 300, Weight = 8, MaxLoadOnTop = 200, Designation = "Bac plastique 600 × 400", Client = "Client C", Family = "Contenants" }
        };
        return Export(examples);
    }
}
