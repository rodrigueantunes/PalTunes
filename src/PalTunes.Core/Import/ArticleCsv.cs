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
        new("CODE", ["CODE_ARTICLE", "ARTICLE", "REF", "REFERENCE", "ITEM"], "Obligatoire", "Code de l'article, unique pour un même client. Même code chez le même client : l'article est mis à jour.", "CAR-400"),
        new("TYPE", ["TYPE_ARTICLE", "NATURE", "KIND"], "Obligatoire", "CAISSE, BOBINE, TUBE, PLAQUE, SAC, FUT, BAC, AUTRE, ou les autres types BIDON (jerrican), SEAU, BOUTEILLE, CUVE (IBC / GRV) ; synonymes acceptés : CARTON, ROULEAU, PANNEAU, JERRICAN, POT, FLACON, IBC…", "CAISSE"),
        new("LONGUEUR", ["L", "LONG", "LENGTH"], "Selon type", "mm. Caisse, sac, bac, plaque, autre ; longueur du tube.", "400"),
        new("LARGEUR", ["LARG", "WIDTH", "LAIZE"], "Selon type", "mm. Caisse, sac, bac, plaque, autre ; laize de la bobine.", "300"),
        new("HAUTEUR", ["H", "HAUT", "HEIGHT", "EPAISSEUR", "EP"], "Selon type", "mm. Hauteur (caisse, sac, bac, fût, autre) ou épaisseur (plaque) ; tube : épaisseur de paroi (Ø intérieur = Ø − 2 × épaisseur, si DIAMETRE_INT est vide).", "250"),
        new("LONGUEUR_PLIEE", ["LONGUEUR_PLIE", "L_PLIEE", "LONGUEUR_A_PLAT", "FOLDED_LENGTH"], "Facultatif", "mm. Carton livré plié : longueur une fois plié ; renseignée, elle remplace la longueur pour le conditionnement.", ""),
        new("LARGEUR_PLIEE", ["LARGEUR_PLIE", "LARG_PLIEE", "LARGEUR_A_PLAT", "FOLDED_WIDTH"], "Facultatif", "mm. Carton plié : largeur une fois plié ; renseignée, elle remplace la largeur pour le conditionnement.", ""),
        new("HAUTEUR_PLIEE", ["HAUTEUR_PLIE", "H_PLIEE", "HAUTEUR_A_PLAT", "EPAISSEUR_PLIEE", "FOLDED_HEIGHT"], "Facultatif", "mm. Carton plié : hauteur (épaisseur) une fois plié ; renseignée, elle remplace la hauteur pour le conditionnement.", ""),
        new("QTE_PAR_CAISSE", ["QUANTITE_PAR_CAISSE", "QTE_CAISSE", "QUANTITE_CAISSE", "PCB", "UNITES_PAR_CAISSE", "QTY_PER_CASE"], "Facultatif", "Caisse / carton : nombre de produits contenus. Renseignée, la palettisation de la caisse indique aussi les produits par palette ; vide, rien ne change.", ""),
        new("DIAMETRE", ["DIAM", "D", "DIAMETRE_EXT", "DIAMETER", "OD"], "Selon type", "mm. Diamètre extérieur (bobine, tube, fût). Absent pour un tube ou une bobine : DIAMETRE_INT sert de diamètre dans les calculs (avertissement).", ""),
        new("DIAMETRE_INT", ["MANDRIN", "DIAM_INT", "ID", "CORE"], "Facultatif", "mm. Diamètre du mandrin d'une bobine, diamètre intérieur d'un tube creux.", ""),
        new("POIDS", ["POIDS_KG", "MASSE", "WEIGHT", "KG"], "Obligatoire", "kg par article.", "12,5"),
        new("ORIENTATION", ["HAUT_IMPOSE", "ROTATION"], "Facultatif", "HAUT_IMPOSE (défaut) ou LIBRE : caisses et « autre ».", "HAUT_IMPOSE"),
        new("AXE", ["AXE_BOBINE", "AXE_TUBE", "AXIS"], "Facultatif", "VERTICAL, HORIZONTAL ou INDIFFERENT : bobines (défaut VERTICAL) et tubes (défaut INDIFFERENT, le meilleur est proposé).", ""),
        new("CHARGE_MAX", ["CHARGE_MAX_DESSUS", "GERBABILITE", "LOAD_ON_TOP"], "Facultatif", "kg supportables par un exemplaire (résistance au gerbage).", "80"),
        new("COUCHES_MAX", ["NB_COUCHES_MAX", "MAX_LAYERS"], "Facultatif", "Nombre maximal de couches superposées de ce produit.", ""),
        new("FRAGILE", [], "Facultatif", "OUI / NON : rien ne sera posé dessus.", "NON"),
        new("FORME_DESSUS", ["DESSUS", "FORME_DU_DESSUS", "TOP_SHAPE"], "Facultatif", "Bidon, seau, bouteille : DROIT ou ARRONDI (bombé). Avec l'angle et la poignée, décide si l'on peut gerber directement, sur intercalaire seulement, ou pas du tout.", ""),
        new("ANGLE_DESSUS", ["ANGLE", "PENTE_DESSUS", "TOP_ANGLE"], "Facultatif", "Bidon, seau, bouteille : angle du dessus par rapport à l'horizontale, en degrés (0 = plat ; dessus bombé : angle au bord).", ""),
        new("POIGNEE", ["ANSE", "HANDLE"], "Facultatif", "Bidon, seau, bouteille, fût : ENCASTREE (sous le plan du dessus), SAILLANTE (dépasse), RABATTABLE (anse couchée) ou AUCUNE.", ""),
        new("POIGNEE_FORME", ["FORME_POIGNEE", "HANDLE_SHAPE"], "Facultatif", "ARRONDIE (section ronde : contact sur une ligne) ou DROITE (dessus plat).", ""),
        new("POIGNEE_LONGUEUR", ["LONGUEUR_POIGNEE", "HANDLE_LENGTH"], "Facultatif", "mm. Longueur de la poignée, au plus la longueur du produit (le diamètre pour un fût, un seau, une bouteille). « 40 % » accepté : % de la longueur du produit.", ""),
        new("POIGNEE_LARGEUR", ["LARGEUR_POIGNEE", "HANDLE_WIDTH"], "Facultatif", "mm. Largeur de la poignée, au plus la largeur du produit (le diamètre pour un fût, un seau, une bouteille).", ""),
        new("POIGNEE_HAUTEUR", ["HAUTEUR_POIGNEE", "HANDLE_HEIGHT"], "Facultatif", "mm. Hauteur de la poignée, au plus la hauteur du produit : saillie, ou profondeur du puits si encastrée.", ""),
        new("POIGNEE_ANGLE_GAUCHE", ["ANGLE_POIGNEE_GAUCHE", "HANDLE_ANGLE_LEFT"], "Facultatif", "Poignée arrondie : inclinaison du côté gauche par rapport à la verticale, en degrés (0 à 80).", ""),
        new("POIGNEE_ANGLE_DROIT", ["ANGLE_POIGNEE_DROIT", "HANDLE_ANGLE_RIGHT"], "Facultatif", "Poignée arrondie : inclinaison du côté droit par rapport à la verticale, en degrés (0 à 80).", ""),
        new("POIGNEE_PLEINE", ["HANDLE_SOLID"], "Facultatif", "OUI / NON : poignée moulée pleine, d'un seul tenant avec le corps.", ""),
        new("TASSEMENT", ["TASSABLE", "COMPRESSION"], "Facultatif", "Sac : tassement accepté à la mise en caisse. OUI (10 %), un pourcentage (0 à 25), ou NON / vide (épaisseur conservée).", ""),
        new("DESIGNATION", ["LIBELLE", "DESCRIPTION", "NOM"], "Facultatif", "Libellé de l'article.", "Carton 400 × 300"),
        new("CLIENT", ["CODE_CLIENT", "CLIENT_CODE", "CUSTOMER"], "Facultatif", "Code du client (base clients) ; un code inconnu crée le client. 1er niveau de l'arborescence par défaut.", "AGRO"),
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
            "FUT" or "TONNEAU" or "DRUM" or "BARIL" => ArticleKind.Fut,
            "BIDON" or "JERRICAN" or "JERRYCAN" or "JERRICANE" or "CANISTER" => ArticleKind.Bidon,
            "SEAU" or "POT" or "PAIL" or "BUCKET" => ArticleKind.Seau,
            "BOUTEILLE" or "FLACON" or "BOTTLE" => ArticleKind.Bouteille,
            "CUVE" or "IBC" or "GRV" or "CUVE_IBC" or "CONTAINER_IBC" => ArticleKind.Cuve,
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

    public static ImportReport Import(string text, IList<Article> database) => Import(text, database, null);

    /// <summary>
    /// Import avec mise à jour : un article est identifié par son code <b>pour son client</b> (le même code peut exister
    /// chez deux clients). Code + client déjà présents : l'article est mis à jour ; sinon il est créé. Sans colonne
    /// CLIENT, le code seul suffit s'il est unique dans la base.
    /// </summary>
    /// <param name="clientKey">Code client normalisé d'une valeur de la colonne CLIENT (code ou nom) ; null = valeur brute.</param>
    public static ImportReport Import(string text, IList<Article> database, Func<string?, string?>? clientKey)
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

        var byCode = database.GroupBy(a => a.Code.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var hasClientColumn = map.ContainsKey("CLIENT");
        string Key(string? client) => (clientKey != null ? clientKey(client) : client)?.Trim() ?? "";
        var wallFromHeight = 0;
        var suspect = 0;
        var innerAsDiameter = 0;
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

            // Article existant : même code chez le même client (ou code unique si le fichier n'a pas de colonne CLIENT).
            var candidates = byCode.TryGetValue(code, out var list) ? list : [];
            Article? existing;
            if (hasClientColumn)
            {
                var client = Key(Get("CLIENT"));
                existing = candidates.FirstOrDefault(a => string.Equals(Key(a.Client), client, StringComparison.OrdinalIgnoreCase));
            }
            else if (candidates.Count > 1)
            {
                report.Lines.Add(new ImportLine
                {
                    Row = r + 1, Code = code, Status = "Erreur", IsError = true,
                    Message = $"Le code {code} existe chez {candidates.Count} clients : ajoutez la colonne CLIENT pour désigner l'article à mettre à jour."
                });
                continue;
            }
            else
            {
                existing = candidates.FirstOrDefault();
            }

            var exists = existing != null;
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
            Num("LONGUEUR_PLIEE", v => article.FoldedLength = v);
            Num("LARGEUR_PLIEE", v => article.FoldedWidth = v);
            Num("HAUTEUR_PLIEE", v => article.FoldedHeight = v);
            if (Has("QTE_PAR_CAISSE"))
            {
                var q = Get("QTE_PAR_CAISSE");
                if (string.IsNullOrWhiteSpace(q))
                {
                    article.QuantityPerCase = null;
                }
                else if (Csv.TryParseNumber(q, out var v) && v >= 1 && Math.Abs(v - Math.Round(v)) < 1e-9)
                {
                    article.QuantityPerCase = (int)Math.Round(v);
                }
                else
                {
                    messages.Add($"QTE_PAR_CAISSE « {q} » : nombre entier > 0 attendu.");
                }
            }
            Num("DIAMETRE", v => article.Diameter = v);
            Num("DIAMETRE_INT", v => article.InnerDiameter = v);
            Num("POIDS", v => article.Weight = v);
            Num("CHARGE_MAX", v => article.MaxLoadOnTop = v);
            Num("COUCHES_MAX", v => article.MaxLayers = (int)Math.Round(v));

            // Tube : la hauteur n'a pas de sens ; HAUTEUR (ou EPAISSEUR) donne l'épaisseur de paroi d'un tube creux.
            if (article.Kind == ArticleKind.Tube && Has("HAUTEUR") && !Has("DIAMETRE_INT") && article.Height > 0 && article.Diameter > 0)
            {
                if (article.Height < article.Diameter / 2)
                {
                    article.InnerDiameter = Math.Round(article.Diameter - 2 * article.Height, 3);
                    wallFromHeight++;
                }

                article.Height = 0;
            }

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

            if (Has("FORME_DESSUS"))
            {
                var f = Csv.NormalizeHeader(Get("FORME_DESSUS"));
                article.TopShape = f.StartsWith("ARR") || f.StartsWith("BOMB") || f.StartsWith("ROUND") ? TopShape.Arrondi : TopShape.Droit;
            }

            Num("ANGLE_DESSUS", v => article.TopAngle = Math.Clamp(v, 0, 90));
            // Poignée en mm ; « 40 % » (ancien format) : % de la dimension du produit.
            void HandleDim(string column, Action<double?> set, Func<double> reference)
            {
                if (!Has(column))
                {
                    return;
                }

                var raw = Get(column).Trim();
                var percent = raw.EndsWith('%');
                if (!double.TryParse(raw.TrimEnd('%').Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                {
                    messages.Add($"{column} « {raw} » : nombre attendu (mm).");
                    return;
                }

                set(percent ? Math.Round(Math.Clamp(v, 0, 100) / 100 * reference(), 1) : v);
            }

            HandleDim("POIGNEE_LONGUEUR", v => article.HandleLength = v, () => article.HandleReference.Length);
            HandleDim("POIGNEE_LARGEUR", v => article.HandleWidth = v, () => article.HandleReference.Width);
            HandleDim("POIGNEE_HAUTEUR", v => article.HandleHeight = v, () => article.HandleReference.Height);
            Num("POIGNEE_ANGLE_GAUCHE", v => article.HandleAngleLeft = v);
            Num("POIGNEE_ANGLE_DROIT", v => article.HandleAngleRight = v);
            if (Has("POIGNEE_PLEINE"))
            {
                article.HandleSolid = ParseBool(Get("POIGNEE_PLEINE"));
            }
            if (Has("POIGNEE_FORME"))
            {
                article.HandleRounded = Csv.NormalizeHeader(Get("POIGNEE_FORME")).StartsWith("ARR") || Csv.NormalizeHeader(Get("POIGNEE_FORME")).StartsWith("ROND");
            }

            if (Has("TASSEMENT"))
            {
                var t = Get("TASSEMENT").Trim().TrimEnd('%').Trim();
                if (double.TryParse(t.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
                {
                    article.Compressible = pct > 0;
                    article.CompressionPercent = pct > 0 ? pct : null;
                }
                else
                {
                    article.Compressible = ParseBool(t);
                    article.CompressionPercent = null;
                }
            }
            if (Has("POIGNEE"))
            {
                var h = Csv.NormalizeHeader(Get("POIGNEE"));
                article.Handle = h.StartsWith("SAIL") || h.StartsWith("DEPASS") ? HandleKind.Saillante
                    : h.StartsWith("RAB") || h.StartsWith("ANSE") ? HandleKind.Rabattable
                    : h.StartsWith("AUC") || h is "NON" or "SANS" or "NONE" ? HandleKind.Aucune
                    : HandleKind.Encastree;
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
                candidates[candidates.IndexOf(existing!)] = article;
                report.Updated++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Mis à jour", Message = article.DimensionsText });
            }
            else
            {
                database.Add(article);
                if (!byCode.TryGetValue(code, out var same))
                {
                    byCode[code] = same = [];
                }

                same.Add(article);
                report.Created++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Créé", Message = article.DimensionsText });
            }

            // Tube ou bobine sans DIAMETRE : les données sont gardées telles quelles, le diamètre intérieur sert de diamètre.
            if (ArticleSchema.DiameterWarning(article) is { } diameterNote)
            {
                innerAsDiameter++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Avertissement", Message = diameterNote });
            }

            if (ArticleSchema.WeightWarning(article) is { } warning)
            {
                suspect++;
                report.Lines.Add(new ImportLine { Row = r + 1, Code = code, Status = "Avertissement", Message = warning });
            }
        }

        if (wallFromHeight > 0)
        {
            report.Lines.Add(new ImportLine { Status = "Info", Message = $"{wallFromHeight} tube(s) : HAUTEUR lue comme épaisseur de paroi, Ø intérieur = Ø − 2 × épaisseur." });
        }

        if (innerAsDiameter > 0)
        {
            report.Lines.Add(new ImportLine { Status = "Avertissement", Message = $"{innerAsDiameter} article(s) sans DIAMETRE : le diamètre intérieur (DIAMETRE_INT) a été pris comme diamètre ; vérifiez ces articles." });
        }

        if (suspect > 0)
        {
            report.Lines.Add(new ImportLine { Status = "Avertissement", Message = $"{suspect} article(s) au poids unitaire suspect (impossible pour leurs dimensions) : vérifiez la colonne POIDS (kg par article)." });
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
        yield return a.Kind == ArticleKind.Caisse ? N(a.FoldedLength) : "";
        yield return a.Kind == ArticleKind.Caisse ? N(a.FoldedWidth) : "";
        yield return a.Kind == ArticleKind.Caisse ? N(a.FoldedHeight) : "";
        yield return a.CaseQuantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        yield return N(a.Diameter);
        yield return N(a.InnerDiameter);
        yield return W(a.Weight);
        yield return ArticleSchema.UsesOrientation(a.Kind) ? (a.Orientation == OrientationRule.Libre ? "LIBRE" : "HAUT_IMPOSE") : "";
        yield return ArticleSchema.UsesCoilAxis(a.Kind) ? a.CoilAxis.ToString().ToUpperInvariant() : "";
        yield return a.MaxLoadOnTop is { } m ? Csv.Number(m, "0.###") : "";
        yield return a.MaxLayers?.ToString() ?? "";
        yield return a.Fragile ? "OUI" : "NON";
        var top = Engine.TopStacking.Applies(a.Kind);
        yield return top && a.TopShape is { } ts ? (ts == TopShape.Arrondi ? "ARRONDI" : "DROIT") : "";
        yield return top && a.TopAngle is { } ta ? Csv.Number(ta, "0.#") : "";
        yield return top && a.Handle is { } h ? h switch { HandleKind.Saillante => "SAILLANTE", HandleKind.Rabattable => "RABATTABLE", HandleKind.Aucune => "AUCUNE", _ => "ENCASTREE" } : "";
        yield return top && a.HandleRounded is { } r ? (r ? "ARRONDIE" : "DROITE") : "";
        yield return top && a.HandleLength is { } hl ? Csv.Number(hl, "0.#") : "";
        yield return top && a.HandleWidth is { } hw ? Csv.Number(hw, "0.#") : "";
        yield return top && a.HandleHeight is { } hh ? Csv.Number(hh, "0.#") : "";
        yield return top && a.HandleAngleLeft is { } al ? Csv.Number(al, "0.#") : "";
        yield return top && a.HandleAngleRight is { } ar ? Csv.Number(ar, "0.#") : "";
        yield return top && a.HandleSolid ? "OUI" : "";
        yield return a.Kind == ArticleKind.Sac && a.Compressible ? Csv.Number(a.CompressionPercent ?? ArticleSchema.DefaultCompressionPercent, "0.#") : "";
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
            new Article { Code = "CAR-400", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 12, QuantityPerCase = 24, MaxLoadOnTop = 90, Designation = "Carton 400 × 300 × 250", Client = "AGRO", Family = "Emballages", SubFamily = "Cartons" },
            new Article { Code = "BOB-1000", Kind = ArticleKind.Bobine, Diameter = 1000, Width = 700, InnerDiameter = 76, Weight = 380, CoilAxis = CoilAxis.Vertical, Designation = "Bobine film Ø1000 laize 700", Client = "AGRO", Family = "Films" },
            new Article { Code = "TUB-110", Kind = ArticleKind.Tube, Diameter = 110, Length = 1200, Weight = 4.5, CoilAxis = CoilAxis.Indifferent, Designation = "Tube PVC Ø110 × 1200", Client = "BATI", Family = "Tubes" },
            new Article { Code = "PLQ-1600", Kind = ArticleKind.Plaque, Length = 1600, Width = 1200, Height = 10, Weight = 15, Designation = "Plaque 1600 × 1200 ép. 10", Client = "BATI", Family = "Plaques" },
            new Article { Code = "SAC-25", Kind = ArticleKind.Sac, Length = 600, Width = 400, Height = 120, Weight = 25, Designation = "Sac 25 kg", Client = "CHIM", Family = "Vrac" },
            new Article { Code = "FUT-200", Kind = ArticleKind.Fut, Diameter = 585, Height = 880, Weight = 220, Designation = "Fût 200 L", Client = "CHIM", Family = "Liquides" },
            new Article { Code = "BID-20", Kind = ArticleKind.Bidon, Length = 290, Width = 190, Height = 370, Weight = 21, TopShape = TopShape.Droit, TopAngle = 0, Handle = HandleKind.Encastree, Designation = "Jerrican 20 L plastique", Client = "CHIM", Family = "Liquides" },
            new Article { Code = "BAC-6040", Kind = ArticleKind.Bac, Length = 600, Width = 400, Height = 300, Weight = 8, MaxLoadOnTop = 200, Designation = "Bac plastique 600 × 400", Client = "CHIM", Family = "Contenants" }
        };
        return Export(examples);
    }
}
