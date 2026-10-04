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
        Assert.Equal(7, report.Created);
        Assert.Equal(7, db.Select(a => a.Kind).Distinct().Count());
        Assert.Equal(CoilAxis.Indifferent, db.Single(a => a.Kind == ArticleKind.Tube).CoilAxis);
    }

    [Fact]
    public void Import_UpdatesByCode_AndReportsErrors()
    {
        var db = new List<Article> { new() { Code = "A1", Kind = ArticleKind.Caisse, Length = 100, Width = 100, Height = 100, Weight = 1, Client = "X" } };
        const string csv = "code;longueur;Poids;Client;type\nA1;250,5;2;Y;\nB2;300;;Z;caisse\nC3;300;1;Z;bidule\nD4;;3;;tube\n";
        var report = ArticleCsv.Import(csv, db);
        Assert.Equal(1, report.Updated);
        Assert.Equal(0, report.Created);
        Assert.Equal(3, report.Errors);
        Assert.Equal(250.5, db[0].Length);
        Assert.Equal("Y", db[0].Client);
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
        Assert.All(db.Articles.Where(a => a.Code.StartsWith("CAR")), a => Assert.Equal("Agro Renommé", a.Client));
        Assert.Contains("Agro Renommé", ClientCsv.Export(db));
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
