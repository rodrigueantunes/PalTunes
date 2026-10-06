using PalTunes.Core.Catalog;
using System.Text.Json;
using PalTunes.Core.Engine;
using PalTunes.Core.Export;
using PalTunes.Core.Import;
using PalTunes.Core.Models;
using PalTunes.Core.Storage;

namespace PalTunes.Tests;

public class ImportExportTests
{
    [Fact]
    public void Template_ImportsAllKinds()
    {
        var db = new List<Article>();
        var report = ArticleCsv.Import(ArticleCsv.Template(), db);
        Assert.Equal(0, report.Errors);
        Assert.Equal(8, report.Created);
        Assert.Equal(8, db.Select(a => a.Kind).Distinct().Count());
        Assert.Equal(ArticleKind.Bidon, db.Single(a => a.Code == "BID-20").Kind);
        Assert.Equal(CoilAxis.Indifferent, db.Single(a => a.Kind == ArticleKind.Tube).CoilAxis);
    }

    [Fact]
    public void Import_UpdatesByCode_AndReportsErrors()
    {
        var db = new List<Article> { new() { Code = "A1", Kind = ArticleKind.Caisse, Length = 100, Width = 100, Height = 100, Weight = 1, Client = "X" } };
        const string csv = "code;longueur;Poids;Client;type\nA1;250,5;2;X;\nB2;300;;Z;caisse\nC3;300;1;Z;bidule\nD4;;3;;tube\n";
        var report = ArticleCsv.Import(csv, db);
        Assert.Equal(1, report.Updated);
        Assert.Equal(0, report.Created);
        Assert.Equal(3, report.Errors);
        Assert.Equal(250.5, db[0].Length);
        Assert.Equal("X", db[0].Client); // même code chez le même client : mis à jour
    }

    [Fact]
    public void Import_CommaSeparated_WithAliasesAndQuotes()
    {
        var db = new List<Article>();
        const string csv = "REF,TYPE,DIAM,LAIZE,MASSE,LIBELLE\n\"BOB-1\",rouleau,1000,700,380,\"Bobine, film\"\n";
        var report = ArticleCsv.Import(csv, db);
        Assert.Equal(1, report.Created);
        Assert.Equal(ArticleKind.Bobine, db[0].Kind);
        Assert.Equal(700, db[0].Width);
        Assert.Equal("Bobine, film", db[0].Designation);
    }

    [Fact]
    public void Export_RoundTrip()
    {
        var db = Database.CreateDefault(withDemo: true);
        var csv = ArticleCsv.Export(db.Articles);
        var copy = new List<Article>();
        var report = ArticleCsv.Import(csv, copy);
        Assert.Equal(0, report.Errors);
        Assert.Equal(db.Articles.Count, copy.Count);
    }

    [Fact]
    public void PackagingExport_ContainsRequiredColumns_MonoArticleOnly()
    {
        var db = Database.CreateDefault(withDemo: true);
        foreach (var p in db.Packagings)
        {
            p.Solution = PackagingCalculator.Compute(p, db).Recommended;
        }

        var csv = PackagingSpec.ExportCsv(db.Packagings.Select(p => (p, db.FindArticle(p.ArticleId))), out var skipped);
        Assert.Equal(2, skipped);
        var lines = csv.Trim().Split('\n');
        Assert.Equal(1 + 3, lines.Length);
        foreach (var header in new[] { "Nombre de produits conditionnés par palette produit", "Nombre de palettes physiques par conditionnement",
                     "Nombre de gerbages", "Hauteur de bois de la palette (mm)", "Hauteur d'encombrement de la charge (mm)" })
        {
            Assert.Contains(header, lines[0]);
        }

        var plate = lines.Single(l => l.StartsWith("CDT-PLQ-1600")).Split(';');
        var col = Array.IndexOf(PackagingSpec.ExportHeaders, "Nombre de palettes physiques par conditionnement");
        Assert.Equal("2", plate[col]);
    }

    [Fact]
    public void Database_SerializesWithSolution()
    {
        var db = Database.CreateDefault(withDemo: true);
        var p = db.Packagings[0];
        p.Solution = PackagingCalculator.Compute(p, db).Recommended;
        var json = JsonSerializer.Serialize(db, DatabaseStore.Json);
        var back = JsonSerializer.Deserialize<Database>(json, DatabaseStore.Json)!;
        Assert.Equal(p.Solution!.ItemsPerUnit, back.Packagings[0].Solution!.ItemsPerUnit);
        Assert.Equal(db.Pallets.Count, back.Pallets.Count);
    }
}

public class SampleFileTests
{
    [Theory]
    [InlineData("articles_exemple.csv", 11)]
    [InlineData("articles_minimal.csv", 7)]
    public void SampleFiles_ImportWithoutError(string file, int expected)
    {
        var db = new List<Article>();
        var report = ArticleCsv.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", file)), db);
        Assert.Equal(0, report.Errors);
        Assert.Equal(expected, report.Created);
        Assert.Empty(report.UnknownColumns);
    }

    [Fact]
    public void SampleClients_ThenArticles_LinkedByClientCode()
    {
        var db = Database.CreateDefault(withDemo: false);
        var clients = ClientCsv.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "clients_exemple.csv")), db);
        Assert.Equal(0, clients.Errors);
        Assert.Equal(7, clients.Created);
        ArticleCsv.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "articles_exemple.csv")), db.Articles);
        Assert.Equal(0, db.MergeClients());
        Assert.Equal("BISNORD - Biscuiterie Nord", db.ClientLabel(db.Articles.First(a => a.Code == "CAR-0402").Client));
    }
}

public class TubeCaseTests
{
    private static Article Ring(double weight) => new()
    {
        Code = "BAG", Kind = ArticleKind.Tube, Diameter = 76, Length = 50, InnerDiameter = 68, Weight = weight, CoilAxis = CoilAxis.Indifferent
    };

    [Fact]
    public void WeightWarning_OnlyForImpossibleDensity()
    {
        Assert.NotNull(ArticleSchema.WeightWarning(Ring(700)));
        Assert.Null(ArticleSchema.WeightWarning(Ring(0.032)));
        // Matière seule (tube creux) : 45 cm³ de carton pour la bague.
        Assert.InRange(ArticleSchema.Density(Ring(0.032)), 0.6, 0.8);
    }

    [Fact]
    public void Import_TubeHeightIsWallThickness()
    {
        var db = new List<Article>();
        var report = ArticleCsv.Import("CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;DIAMETRE;POIDS\nBAG1;TUBE;50;76;4;76;700\nBAG2;TUBE;12;25,7;2;25,7;0,001\n", db);
        Assert.Equal(0, report.Errors);
        Assert.Equal(68, db[0].InnerDiameter);
        Assert.Equal(21.7, db[1].InnerDiameter, 3);
        Assert.Equal(0, db[0].Height);
        Assert.Contains(report.Lines, l => l.Status == "Avertissement" && l.Code == "BAG1");
        Assert.DoesNotContain(report.Lines, l => l.Status == "Avertissement" && l.Code == "BAG2");
    }

    [Fact]
    public void Import_NoDiameter_UsesInnerDiameter_WithWarning()
    {
        var db = new List<Article>();
        var report = ArticleCsv.Import("CODE;TYPE;LONGUEUR;LARGEUR;DIAMETRE;DIAMETRE_INT;POIDS\nT1;TUBE;1000;;;50;2\nB1;BOBINE;;300;;76;5\nT2;TUBE;1000;;60;50;1\n", db);
        Assert.Equal(0, report.Errors);
        Assert.Equal(3, report.Created);
        // Données gardées telles quelles ; le logiciel prend le diamètre intérieur comme diamètre.
        Assert.Equal((0d, 50d, 50d), (db[0].Diameter, db[0].InnerDiameter, db[0].EffectiveDiameter));
        Assert.Equal((0d, 76d, 76d), (db[1].Diameter, db[1].InnerDiameter, db[1].EffectiveDiameter));
        Assert.Equal((60d, 50d, 60d), (db[2].Diameter, db[2].InnerDiameter, db[2].EffectiveDiameter)); // les deux : diamètre extérieur
        Assert.Empty(ArticleSchema.Validate(db[0]));
        Assert.NotNull(ArticleSchema.DiameterWarning(db[0]));
        Assert.Null(ArticleSchema.DiameterWarning(db[2]));
        // La palettisation utilise le diamètre pris en compte.
        var pallet = PalletCatalog.Defaults().First(p => p.Code == "EUR1");
        var best = HomogeneousEngine.Solve(db[0], BaseInfo.From(pallet, false, 1, 1), new PackagingConstraints()).Recommended!;
        Assert.All(best.FirstUnit!.Items, i => Assert.Equal(50, Math.Min(i.DY, i.DZ), 3));
        Assert.Equal(db[0].Id, best.FirstUnit!.Items[0].ArticleId);
        Assert.Contains(report.Lines, l => l.Status == "Avertissement" && l.Code == "T1" && l.Message.Contains("Diamètre extérieur absent"));
        Assert.Contains(report.Lines, l => l.Status == "Avertissement" && l.Code == "B1");
        Assert.DoesNotContain(report.Lines, l => l.Status == "Avertissement" && l.Code == "T2");
        Assert.Contains(report.Lines, l => l.Status == "Avertissement" && l.Message.StartsWith("2 article(s) sans DIAMETRE"));
    }

    [Fact]
    public void NoCase_DiagnosisExplainsWeight()
    {
        var cases = CaseCatalog.Defaults();
        var heavy = Ring(700);
        Assert.Empty(CaseEngine.PossibleCases(heavy, cases));
        var message = CaseEngine.Propose(heavy, cases).Messages.Single();
        Assert.Contains("charge maxi", message);
        Assert.Contains("Poids unitaire suspect", message);
        Assert.NotEmpty(CaseEngine.PossibleCases(Ring(0.032), cases));
    }

    [Fact]
    public void CaseAxis_BothByDefault_OrForced()
    {
        var cases = CaseCatalog.Defaults();
        var both = CaseEngine.Propose(Ring(0.032), cases, axis: CoilAxis.Indifferent).Solutions;
        Assert.Contains(both, s => CaseEngine.IsUpright(s) && s.Title.EndsWith(" debout"));
        Assert.Contains(both, s => !CaseEngine.IsUpright(s) && s.Title.EndsWith(" couchés"));
        Assert.Single(both, s => s.Recommended);
        Assert.All(CaseEngine.Propose(Ring(0.032), cases, axis: CoilAxis.Vertical).Solutions, s => Assert.True(CaseEngine.IsUpright(s)));
        Assert.All(CaseEngine.Propose(Ring(0.032), cases, axis: CoilAxis.Horizontal).Solutions, s => Assert.False(CaseEngine.IsUpright(s)));
    }

    [Fact]
    public void HollowTube_PlacementsKeepInnerDiameter()
    {
        var both = CaseEngine.Propose(Ring(0.032), CaseCatalog.Defaults(), axis: CoilAxis.Indifferent).Solutions;
        Assert.All(both.SelectMany(s => s.FirstUnit!.Items), p => Assert.Equal(68, p.InnerDiameter));
        var solid = Ring(0.032);
        solid.InnerDiameter = 0;
        Assert.All(CaseEngine.Propose(solid, CaseCatalog.Defaults()).Solutions.SelectMany(s => s.FirstUnit!.Items), p => Assert.Equal(0, p.InnerDiameter));
    }

    [Fact]
    public void CaseSolutions_HaveNoPalletRollingWarnings()
    {
        var ring = Ring(0.032);
        ring.CoilAxis = CoilAxis.Horizontal;
        var result = CaseEngine.Propose(ring, CaseCatalog.Defaults());
        Assert.NotEmpty(result.Solutions);
        Assert.All(result.Solutions, s => Assert.DoesNotContain(s.Warnings, w => w.StartsWith("Tubes couchés")));
    }
}

public class FoldedCartonTests
{
    private static Article Carton() => new()
    {
        Code = "CAR", Kind = ArticleKind.Caisse, Length = 400, Width = 300, Height = 250, Weight = 0.3
    };

    [Fact]
    public void EachFoldedDimension_ReplacesOnlyItsOwn()
    {
        var a = Carton();
        Assert.False(a.IsFolded);
        Assert.Same(a, a.ForPalletizing());
        a.FoldedHeight = 5;
        Assert.Equal((400d, 300d, 5d), a.PackedDimensions);
        a.FoldedLength = 700;
        Assert.Equal((700d, 300d, 5d), a.PackedDimensions);
        a.FoldedWidth = 550;
        var p = a.ForPalletizing();
        Assert.Equal((700d, 550d, 5d), (p.Length, p.Width, p.Height));
        Assert.Equal(a.Id, p.Id);
        Assert.Equal(250, a.Height); // la fiche garde les dimensions montées
    }

    [Fact]
    public void Palletization_UsesFoldedDimensions()
    {
        var a = Carton();
        a.FoldedLength = 600;
        a.FoldedWidth = 400;
        a.FoldedHeight = 5;
        var pallet = PalletCatalog.Defaults().First(p => p.Code == "EUR1");
        var best = HomogeneousEngine.Solve(a, BaseInfo.From(pallet, false, 1, 1), new PackagingConstraints()).Recommended!;
        Assert.All(best.FirstUnit!.Items, i => Assert.Equal(5, i.DZ, 3));
        Assert.Equal(4, best.ItemsPerLayer); // 600 × 400 : 4 par couche sur 1200 × 800
        Assert.Contains(PackagingSpec.Rows(best, new PackagingConstraints(), a), r => r.Label == "Dimensions du produit prises en compte");
    }

    [Fact]
    public void Csv_FoldedColumns_RoundTrip()
    {
        var db = new List<Article>();
        ArticleCsv.Import("CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;POIDS;HAUTEUR_PLIEE;LARGEUR_PLIEE\nC1;CAISSE;400;300;250;0,3;5;550\n", db);
        Assert.Equal((400d, 550d, 5d), db[0].PackedDimensions);
        var back = new List<Article>();
        ArticleCsv.Import(ArticleCsv.Export(db), back);
        Assert.Equal(db[0].PackedDimensions, back[0].PackedDimensions);
    }
}

public class ImportUpsertTests
{
    [Fact]
    public void Article_SameCodeSameClient_Updated_OtherClient_Created()
    {
        var db = new List<Article>();
        ArticleCsv.Import("CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;POIDS;CLIENT\nA1;CAISSE;100;100;100;1;C1\n", db);
        var report = ArticleCsv.Import("CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;POIDS;CLIENT\nA1;CAISSE;200;100;100;1;C1\nA1;CAISSE;300;100;100;1;C2\n", db);
        Assert.Equal(1, report.Updated);
        Assert.Equal(1, report.Created);
        Assert.Equal(2, db.Count);
        Assert.Equal(200, db.Single(a => a.Client == "C1").Length);
        Assert.Equal(300, db.Single(a => a.Client == "C2").Length);
        // Sans colonne CLIENT, un code présent chez deux clients est ambigu.
        var ambiguous = ArticleCsv.Import("CODE;POIDS\nA1;2\n", db);
        Assert.Equal(1, ambiguous.Errors);
    }

    [Fact]
    public void Client_ExistingCode_Updated_DuplicateNamesAccepted()
    {
        var db = Database.CreateDefault(withDemo: false);
        ClientCsv.Import("CODE;NOM\nK1;Société Martin\nK2;Société Martin\n", db);
        var report = ClientCsv.Import("CODE;NOM;VILLE\nK1;Société Martin;Lyon\n", db);
        Assert.Equal(0, report.Errors);
        Assert.Equal(1, report.Updated);
        Assert.Equal(2, db.Clients.Count(c => c.Name == "Société Martin"));
        Assert.Equal("Lyon", db.FindClient("K1")!.City);
    }
}

public class EnclosureNoteTests
{
    [Fact]
    public void Corners_ExplainedOnlyWhenDifferentFromPalletAndLoad()
    {
        var c = new PackagingConstraints { Corners = true, CornerThickness = 5 };
        Assert.Equal(" (charge 1200 + cornières 2 × 5 = +10 mm)", PackagingSpec.EnclosureNote(1210, 1200, 1200, 0, 1200, c));
        Assert.Equal("", PackagingSpec.EnclosureNote(800, 800, 770, 15, 785, c));
    }

    [Fact]
    public void Film_AndCorners_Combined()
    {
        var c = new PackagingConstraints { Corners = true, CornerThickness = 5, FilmThickness = 1 };
        Assert.Equal(" (charge 1200 + cornières 2 × 5 + film 2 × 1 = +12 mm)", PackagingSpec.EnclosureNote(1212, 1200, 1200, 0, 1200, c));
    }

    [Fact]
    public void UnitWeight_FiveDecimals_RoundTrip()
    {
        var db = new List<Article> { new() { Code = "VIS", Kind = ArticleKind.Caisse, Length = 10, Width = 5, Height = 5, Weight = 0.00001 } };
        var csv = ArticleCsv.Export(db);
        Assert.Contains("0,00001", csv);
        var back = new List<Article>();
        ArticleCsv.Import(csv, back);
        Assert.Equal(0.00001, back[0].Weight, 10);
    }

    [Fact]
    public void Clients_MergedFromArticles_AndImported()
    {
        var db = Database.CreateDefault(withDemo: true);
        Assert.Equal(4, db.Clients.Count);
        db.Articles.Add(new Article { Code = "X", Kind = ArticleKind.Caisse, Length = 1, Width = 1, Height = 1, Weight = 1, Client = "Nouveau client" });
        Assert.Equal(1, db.MergeClients());
        var report = ClientCsv.Import("CODE;NOM;VILLE;PALETTE\nAGRO;Agro Renommé;Lille;EUR1\nNEW;Client neuf;;XX\n", db);
        Assert.Equal(1, report.Updated);
        Assert.Equal(1, report.Errors);
        // Les articles citent le code : un nouveau nom ne les modifie pas, il apparaît dans le libellé « CODE - Nom ».
        Assert.All(db.Articles.Where(a => a.Code.StartsWith("CAR")), a => Assert.Equal("AGRO", a.Client));
        Assert.Equal("AGRO - Agro Renommé", db.ClientLabel("AGRO"));
        Assert.Contains("Agro Renommé", ClientCsv.Export(db));
    }

    [Fact]
    public void ArticleImport_ClientColumnIsTheClientCode()
    {
        var db = Database.CreateDefault(withDemo: true);
        ArticleCsv.Import("CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;POIDS;CLIENT\nN1;CAISSE;100;100;100;1;BATI\nN2;CAISSE;100;100;100;1;NOUV\nN3;CAISSE;100;100;100;1;Démo Chimie\n", db.Articles);
        Assert.Equal(1, db.MergeClients());
        Assert.Equal("BATI", db.Articles.Single(a => a.Code == "N1").Client);
        Assert.Equal("BATI - Démo Bâtiment", db.ClientLabel(db.Articles.Single(a => a.Code == "N1").Client));
        // Code inconnu : client créé avec ce code ; nom d'un client existant : remplacé par son code.
        Assert.Contains(db.Clients, c => c.Code == "NOUV");
        Assert.Equal("CHIM", db.Articles.Single(a => a.Code == "N3").Client);
        Assert.Contains(";BATI;", ArticleCsv.Export(db.Articles));
    }

    [Fact]
    public void OldDatabase_ClientNamesConvertedToCodes_AndCodeChangeFollowed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"paltunes-test-{Guid.NewGuid():N}.json");
        try
        {
            var db = Database.CreateDefault(withDemo: true);
            foreach (var a in db.Articles)
            {
                a.Client = db.FindClient(a.Client)!.Name; // base 0.0.5 : nom du client
            }

            db.SchemaVersion = 3;
            var store = new DatabaseStore(path);
            store.Save(db);
            var loaded = store.Load();
            Assert.Equal(6, loaded.SchemaVersion);
            Assert.All(loaded.Articles, a => Assert.Contains(loaded.Clients, c => c.Code == a.Client));
            var agro = loaded.FindClient("AGRO")!;
            var count = loaded.ArticleCountOf(agro);
            Assert.True(count > 0);
            Assert.Equal(count, loaded.RenameClientCode("AGRO", "AGR"));
            agro.Code = "AGR";
            Assert.Equal(count, loaded.ArticleCountOf(agro));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class StackingConventionTests
{
    [Fact]
    public void Stacking_ZeroMeansNotStackable()
    {
        var c = new PackagingConstraints();
        Assert.Equal(0, c.MaxStacking);
        Assert.Equal(1, c.MaxStackLevels);
        c.MaxStacking = 2;
        Assert.Equal(3, c.MaxStackLevels);
    }

    [Fact]
    public void Export_GerbagesColumn_UsesStackingCount()
    {
        var db = Database.CreateDefault(withDemo: true);
        var plate = db.Packagings.Single(p => p.Code == "CDT-PLQ-1600");
        plate.Solution = PackagingCalculator.Compute(plate, db).Recommended;
        Assert.Equal(1, plate.Solution!.Stackings);
        var csv = PackagingSpec.ExportCsv([(plate, db.FindArticle(plate.ArticleId))], out _);
        var line = csv.Trim().Split('\n')[1].Split(';');
        Assert.Equal("1", line[Array.IndexOf(PackagingSpec.ExportHeaders, "Nombre de gerbages")]);
    }

    [Fact]
    public void ClientCsv_GerbageMax_IsStackingCount()
    {
        var db = Database.CreateDefault(withDemo: false);
        ClientCsv.Import("CODE;NOM;GERBAGE_MAX\nX;Client X;0\n", db);
        Assert.Equal(0, db.Clients.Single().MaxStacking);
        Assert.Equal(1, db.Clients.Single().MaxStackLevels);
    }
}

public class DatabaseSafetyTests
{
    [Theory]
    [InlineData(@"C:\PalTunes\PalTunes.deps.json")]
    [InlineData(@"C:\PalTunes\PalTunes.runtimeconfig.json")]
    [InlineData(@"C:\x\settings.json")]
    public void ReservedFiles_AreRefused(string path)
    {
        Assert.True(DatabaseStore.IsReservedFileName(path));
        Assert.NotNull(DatabaseStore.CheckUsable(path, mustExist: false));
        Assert.Throws<InvalidDataException>(() => new DatabaseStore(path).Save(new Database()));
    }

    [Fact]
    public void ForeignJson_IsNotOpenedNorOverwritten()
    {
        var file = Path.Combine(Path.GetTempPath(), $"paltunes-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(file, "{ \"runtimeTarget\": { \"name\": \".NETCoreApp\" } }");
            Assert.NotNull(DatabaseStore.CheckUsable(file, mustExist: true));
            Assert.Throws<InvalidDataException>(() => new DatabaseStore(file).Load());
            File.WriteAllText(file, "");
            Assert.Null(DatabaseStore.CheckUsable(file, mustExist: true));
            var db = new DatabaseStore(file).Load(seedDemoIfMissing: false);
            Assert.Equal(15, db.Cases.Count);
            Assert.Equal(27, db.Pallets.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Backup_CreatesTimestampedCopy()
    {
        var file = Path.Combine(Path.GetTempPath(), $"paltunes-test-{Guid.NewGuid():N}.json");
        var store = new DatabaseStore(file);
        store.Save(Database.CreateDefault(withDemo: true));
        var backup = store.Backup();
        try
        {
            Assert.NotNull(backup);
            Assert.True(File.Exists(backup));
            Assert.True(DatabaseStore.LooksLikeDatabase(backup!));
        }
        finally
        {
            File.Delete(file);
            if (backup != null) File.Delete(backup);
        }
    }
}

public class PalletizationPlanTests
{
    [Fact]
    public void Plan_GroupsIdenticalLayers_AndNumbersPositions()
    {
        var a = new Article { Code = "C", Kind = ArticleKind.Caisse, Length = 500, Width = 300, Height = 120, Weight = 5 };
        var eur = BaseInfo.From(PalTunes.Core.Catalog.PalletCatalog.Defaults().Single(p => p.Code == "EUR1"), false, 1, 1);
        var c = new PackagingConstraints { SlipSheetThickness = 3, SlipSheetEvery = 2 };
        var crossed = HomogeneousEngine.Solve(a, eur, c).Solutions.First(s => s.Pattern == StackPattern.Croise);
        var unit = crossed.FirstUnit!;
        var groups = PalletizationPlan.Groups(unit, _ => "C");
        Assert.Equal(2, groups.Count);
        Assert.Equal(unit.Layers.Count, groups.Sum(g => g.Layers.Count));
        Assert.All(groups, g => Assert.Equal(Enumerable.Range(1, g.Count), g.Positions.Select(p => p.Number)));
        Assert.Contains(groups, g => g.SlipSheetLayers.Count > 0);
        var rows = PalletizationPlan.LayerRows(unit, groups);
        Assert.Equal(crossed.ItemsPerUnit, rows.Sum(r => r.Count));
        Assert.Equal("1-3, 5, 7-9", PalletizationPlan.Ranges([1, 2, 3, 5, 7, 8, 9]));
    }

    [Fact]
    public void Export_ContainsLayerPlanColumns()
    {
        var db = Database.CreateDefault(withDemo: true);
        var p = db.Packagings.Single(x => x.Code == "CDT-CAR-400");
        p.Solution = PackagingCalculator.Compute(p, db).Recommended;
        var csv = PackagingSpec.ExportCsv([(p, db.FindArticle(p.ArticleId))], out _);
        var line = csv.Trim().Split('\n')[1].Split(';');
        Assert.Contains("C1-6 : plan A, 8 produit(s)", line[Array.IndexOf(PackagingSpec.ExportHeaders, "Plan par couche")]);
        Assert.Equal("aucun", line[Array.IndexOf(PackagingSpec.ExportHeaders, "Intercalaires (couches)")].Trim());
    }
}

public class QuantityPerCaseTests
{
    private static readonly Article Product = new() { Code = "B", Kind = ArticleKind.Caisse, Length = 100, Width = 100, Height = 120, Weight = 0.5 };
    private static readonly CaseSpec Spec = new() { InnerLength = 400, InnerWidth = 300, InnerHeight = 250, WallThickness = 4, Tare = 0.4 };

    [Fact]
    public void Import_QuantityPerCase_SetClearedAndExported()
    {
        var db = new List<Article>();
        var report = ArticleCsv.Import("CODE;TYPE;LONGUEUR;LARGEUR;HAUTEUR;POIDS;QTE_PAR_CAISSE\nC1;CAISSE;400;300;250;5;24\nC2;CAISSE;400;300;250;5;\nC3;CAISSE;400;300;250;5;2,5\n", db);
        Assert.Equal(24, db.Single(a => a.Code == "C1").QuantityPerCase);
        Assert.Null(db.Single(a => a.Code == "C2").QuantityPerCase);
        Assert.Contains(report.Lines, l => l.Code == "C3" && l.Message.Contains("QTE_PAR_CAISSE"));

        var back = new List<Article>();
        ArticleCsv.Import(ArticleCsv.Export(db), back);
        Assert.Equal(24, back.Single(a => a.Code == "C1").QuantityPerCase);

        // Colonne absente : la quantité existante est conservée.
        ArticleCsv.Import("CODE;POIDS\nC1;6\n", db);
        Assert.Equal(24, db.Single(a => a.Code == "C1").QuantityPerCase);
    }

    [Fact]
    public void CaseArticle_CarriesQuantityAndContent_AndRebuildsSameColisage()
    {
        var best = CaseEngine.Solve(Product, Spec).Recommended!;
        var box = CaseEngine.CreateCaseArticle(Product, best, Spec, "CAI-B");
        Assert.Equal(24, box.QuantityPerCase);
        Assert.Equal(Product.Id, box.CaseContent!.ArticleId);

        Article? Find(Guid id) => id == Product.Id ? Product : null;
        Assert.True(CaseEngine.CanRebuild(box, Find));
        var sheet = CaseEngine.Rebuild(box, Find, [])!;
        Assert.Equal(24, sheet.Solution.ItemsPerUnit);
        Assert.Equal(Spec.InnerLength, sheet.Spec.InnerLength);

        // Quantité par caisse modifiée : imposée au recalcul.
        box.QuantityPerCase = 12;
        Assert.Equal(12, CaseEngine.Rebuild(box, Find, [])!.Solution.ItemsPerUnit);

        // Produit supprimé, ou caisse importée sans contenu : pas de fiche de colisage.
        Assert.False(CaseEngine.CanRebuild(box, _ => null));
        Assert.False(CaseEngine.CanRebuild(new Article { Kind = ArticleKind.Caisse, QuantityPerCase = 24 }, Find));
    }

    [Fact]
    public void PalletSpec_ShowsProductsPerPallet_OnlyWhenQuantityKnown()
    {
        var best = CaseEngine.Solve(Product, Spec).Recommended!;
        var box = CaseEngine.CreateCaseArticle(Product, best, Spec, "CAI-B");
        var pallet = PalletCatalog.Defaults().First(p => p.Code == "EUR1");
        var s = HomogeneousEngine.Solve(box, BaseInfo.From(pallet, false, 1, 1), new PackagingConstraints()).Recommended!;
        var rows = PackagingSpec.Rows(s, new PackagingConstraints(), box);
        Assert.Equal((s.ItemsPerUnit * 24).ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")),
            rows.Single(r => r.Label == "Produits contenus par palette produit").Value);

        box.QuantityPerCase = null;
        Assert.DoesNotContain(PackagingSpec.Rows(s, new PackagingConstraints(), box), r => r.Label.Contains("caisse", StringComparison.OrdinalIgnoreCase));
        Assert.Single(ArticleSchema.Validate(new Article { Code = "X", Kind = ArticleKind.Caisse, Length = 1, Width = 1, Height = 1, Weight = 1, QuantityPerCase = 0 }));
    }

    [Fact]
    public void CaseContent_SurvivesDatabaseRoundTrip()
    {
        var best = CaseEngine.Solve(Product, Spec).Recommended!;
        var db = new Database();
        db.Articles.Add(Product);
        db.Articles.Add(CaseEngine.CreateCaseArticle(Product, best, Spec, "CAI-B", axis: CoilAxis.Vertical));
        var back = JsonSerializer.Deserialize<Database>(JsonSerializer.Serialize(db, DatabaseStore.Json), DatabaseStore.Json)!;
        var box = back.Articles.Single(a => a.Code == "CAI-B");
        Assert.Equal(24, box.QuantityPerCase);
        Assert.Equal(db.Articles[1].CaseContent, box.CaseContent);
    }
}

public class PalletOfCasesTests
{
    [Fact]
    public void CaseFromColisage_IsPalletized_WithQuantityAndContent()
    {
        var product = new Article { Code = "B", Kind = ArticleKind.Caisse, Length = 100, Width = 100, Height = 120, Weight = 0.5 };
        var spec = new CaseSpec { InnerLength = 400, InnerWidth = 300, InnerHeight = 250, WallThickness = 4, Tare = 0.4 };
        var colisage = CaseEngine.Solve(product, spec).Recommended!;
        var pallet = PalletCatalog.Defaults().First(p => p.Code == "EUR1");
        var (box, s) = CaseEngine.PalletOfCases(product, colisage, spec, null, null, pallet, new PackagingConstraints { MaxTotalHeight = 1800 }, "CAI-B");
        Assert.NotNull(s);
        Assert.Equal(24, box.QuantityPerCase);
        Assert.Equal(product.Id, box.CaseContent!.ArticleId);
        Assert.True(s!.ItemsPerUnit > 0);
    }
}
