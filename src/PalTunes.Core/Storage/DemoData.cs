using PalTunes.Core.Catalog;
using PalTunes.Core.Models;

namespace PalTunes.Core.Storage;

/// <summary>Articles et conditionnements d'exemple créés au premier lancement (un par type de produit).</summary>
public static class DemoData
{
    public static void Fill(Database db)
    {
        Article A(string code, ArticleKind kind, string designation, string client, string family, string? sub, string color,
            double l = 0, double w = 0, double h = 0, double d = 0, double weight = 1, double? top = null, CoilAxis? axis = null)
        {
            var a = new Article
            {
                Code = code, Kind = kind, Designation = designation, Client = client, Family = family, SubFamily = sub, Color = color,
                Length = l, Width = w, Height = h, Diameter = d, Weight = weight, MaxLoadOnTop = top,
                CoilAxis = axis ?? ArticleSchema.DefaultAxis(kind)
            };
            db.Articles.Add(a);
            return a;
        }

        var carton = A("CAR-400", ArticleKind.Caisse, "Carton 400 × 300 × 250", "AGRO", "Emballages", "Cartons", "#5DADE2", 400, 300, 250, weight: 12, top: 90);
        var carton2 = A("CAR-600", ArticleKind.Caisse, "Carton 600 × 400 × 300", "AGRO", "Emballages", "Cartons", "#48C9B0", 600, 400, 300, weight: 9, top: 120);
        var carton3 = A("CAR-300", ArticleKind.Caisse, "Carton 300 × 200 × 150", "AGRO", "Emballages", "Cartons", "#F5B041", 300, 200, 150, weight: 4, top: 60);
        A("SAC-25", ArticleKind.Sac, "Sac 25 kg", "AGRO", "Vrac", "Sacs", "#D7BDE2", 600, 400, 120, weight: 25);
        A("BOB-1000", ArticleKind.Bobine, "Bobine film Ø1000 laize 700", "PLAS", "Films", "Bobines", "#F39C12", w: 700, d: 1000, weight: 380);
        A("BOB-250", ArticleKind.Bobine, "Bobine étiquettes Ø250 laize 330", "PLAS", "Films", "Bobines", "#EB984E", w: 330, d: 250, weight: 9);
        var tube = A("TUB-110", ArticleKind.Tube, "Tube PVC Ø110 × 1200", "BATI", "Tubes", "PVC", "#AEB6BF", l: 1200, d: 110, weight: 4.5);
        var plaque = A("PLQ-1600", ArticleKind.Plaque, "Plaque 1600 × 1200 ép. 10", "BATI", "Plaques", "Composite", "#7FB3D5", 1600, 1200, 10, weight: 15);
        A("FUT-200", ArticleKind.Fut, "Fût 200 L", "CHIM", "Liquides", "Fûts", "#2E86C1", h: 880, d: 585, weight: 220);
        A("BAC-6040", ArticleKind.Bac, "Bac plastique 600 × 400 × 300", "CHIM", "Contenants", "Bacs", "#52BE80", 600, 400, 300, weight: 8, top: 200);
        db.Articles.Single(a => a.Code == "BOB-250").InnerDiameter = 76;
        db.Articles.Single(a => a.Code == "BOB-1000").InnerDiameter = 152;

        db.Clients.AddRange(
        [
            new Client { Code = "AGRO", Name = "Démo Agro", Address = "12 rue des Moissons", PostalCode = "59000", City = "Lille", Country = "France", Contact = "Service logistique" },
            new Client { Code = "BATI", Name = "Démo Bâtiment", Address = "ZI des Carrières", PostalCode = "69200", City = "Vénissieux", Country = "France", MaxTotalHeight = 2000 },
            new Client { Code = "CHIM", Name = "Démo Chimie", Address = "Port industriel", PostalCode = "76600", City = "Le Havre", Country = "France", DefaultPalletId = PalletCatalog.StableId("CP3"), Notes = "Palettes CP imposées." },
            new Client { Code = "PLAS", Name = "Démo Plasturgie", City = "Oyonnax", PostalCode = "01100", Country = "France", DefaultPalletId = PalletCatalog.StableId("PLA-EUR3"), Notes = "Palettes plastiques grises (hygiène)." }
        ]);

        var eur = PalletCatalog.StableId("EUR1");
        db.Packagings.Add(new Packaging
        {
            Code = "CDT-CAR-400", Name = "Carton 400 sur EUR, filmé et cerclé", ArticleId = carton.Id, PalletId = eur,
            Constraints = new PackagingConstraints { FilmThickness = 1, Straps = 2 }
        });
        db.Packagings.Add(new Packaging
        {
            Code = "CDT-PLQ-1600", Name = "Plaques sur 2 palettes 800 × 1200", ArticleId = plaque.Id, PalletId = eur,
            PalletRotated = true, CountAlongLength = 2, Constraints = new PackagingConstraints { MaxTotalHeight = 1200, MaxStackLevels = 2 }
        });
        db.Packagings.Add(new Packaging
        {
            Code = "CDT-TUB-110", Name = "Tubes avec cornières", ArticleId = tube.Id, PalletId = eur,
            Constraints = new PackagingConstraints { Corners = true, CornerThickness = 5, CornerLeg = 60, Straps = 2 }
        });
        db.Packagings.Add(new Packaging
        {
            Code = "CDT-MIX-01", Name = "Commande mixte cartons", Kind = PackagingKind.Heterogene, PalletId = eur,
            Lines =
            [
                new PackagingLine { ArticleId = carton.Id, Quantity = 16 },
                new PackagingLine { ArticleId = carton2.Id, Quantity = 6 },
                new PackagingLine { ArticleId = carton3.Id, Quantity = 24 }
            ]
        });
        db.Packagings.Add(new Packaging
        {
            Code = "CDT-MIX-02", Name = "Commande mixte : sacs, bacs, cartons, étiquettes", Kind = PackagingKind.Heterogene, PalletId = eur,
            Lines =
            [
                new PackagingLine { ArticleId = db.Articles.Single(a => a.Code == "CAR-300").Id, Quantity = 12 },
                new PackagingLine { ArticleId = db.Articles.Single(a => a.Code == "SAC-25").Id, Quantity = 8 },
                new PackagingLine { ArticleId = db.Articles.Single(a => a.Code == "BAC-6040").Id, Quantity = 4 },
                new PackagingLine { ArticleId = db.Articles.Single(a => a.Code == "BOB-250").Id, Quantity = 6 },
                new PackagingLine { ArticleId = carton2.Id, Quantity = 4 }
            ]
        });
    }
}
