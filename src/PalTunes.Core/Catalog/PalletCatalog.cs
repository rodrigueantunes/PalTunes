using PalTunes.Core.Models;

namespace PalTunes.Core.Catalog;

/// <summary>Palettes courantes livrées par défaut (étude §8). Identifiants stables pour les mises à jour.</summary>
public static class PalletCatalog
{
    public const string Wood = "#C8A165";
    public const string WoodDark = "#A8834B";
    public const string PlasticGrey = "#8E969D";
    public const string Cardboard = "#B9925E";

    public static IReadOnlyList<PalletType> Defaults() =>
    [
        P("EUR1", "Europe EPAL / EUR 1", "Europe", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1200, 800, 144, 25, 1500, 4000, Wood, "EN 13698-1. La plus répandue en Europe."),
        P("EUR2", "EUR 2 (1200 × 1000)", "Europe", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1200, 1000, 162, 33, 1250, 4000, Wood, "EN 13698-2."),
        P("EUR3", "EUR 3 (1000 × 1200)", "Europe", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1000, 1200, 144, 29, 1500, 4000, Wood, "Planches de dessus dans le sens 1000."),
        P("EUR6", "Demi-palette EUR 6", "Demi / quart", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 800, 600, 144, 10, 500, 1500, Wood, "Grande distribution, mise en avant."),
        P("QUART", "Quart de palette (display)", "Demi / quart", PalletMaterial.Bois, PalletConstruction.Blocs9, 600, 400, 144, 6, 250, 750, Wood, "Module ISO 3394 600 × 400."),
        P("ISO1210", "Palette industrielle ISO", "ISO", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1200, 1000, 144, 30, 1500, 4000, Wood, "ISO 6780 – Royaume-Uni, industrie."),
        P("US4840", "US GMA 48\" × 40\"", "ISO", PalletMaterial.Bois, PalletConstruction.Patins3, 1219, 1016, 140, 22, 1200, 2500, WoodDark, "ISO 6780 – Amérique du Nord."),
        P("AS1165", "Australienne 1165 × 1165", "ISO", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1165, 1165, 150, 35, 1500, 4000, WoodDark, "ISO 6780 – Australie."),
        P("AS1100", "Asie 1100 × 1100", "ISO", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1100, 1100, 150, 30, 1500, 4000, WoodDark, "ISO 6780 – Asie."),
        P("ISO1067", "42\" × 42\" (1067 × 1067)", "ISO", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1067, 1067, 150, 28, 1200, 3000, WoodDark, "ISO 6780 – télécoms, peintures."),
        P("CP1", "CP1 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Patins3, 1000, 1200, 138, 22, 1250, 3000, Wood, "Sacs, big-bags."),
        P("CP2", "CP2 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Patins3, 800, 1200, 138, 18, 1000, 2500, Wood, "Sacs."),
        P("CP3", "CP3 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1140, 1140, 138, 25, 1250, 3000, Wood, "Fûts, big-bags (périmétrique)."),
        P("CP4", "CP4 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Patins3, 1100, 1300, 138, 30, 1250, 3000, Wood, "Sacs."),
        P("CP5", "CP5 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Patins3, 760, 1140, 138, 15, 1000, 2000, Wood, "Sacs."),
        P("CP6", "CP6 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1200, 1000, 156, 30, 1250, 3000, Wood, "Fûts, bidons."),
        P("CP7", "CP7 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1300, 1100, 156, 34, 1250, 3000, Wood, "Big-bags."),
        P("CP8", "CP8 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1140, 1140, 156, 30, 1250, 3000, Wood, "Fûts (périmétrique)."),
        P("CP9", "CP9 (chimie)", "Chimie CP", PalletMaterial.Bois, PalletConstruction.Blocs9Semelles3, 1140, 1140, 156, 32, 1250, 3000, Wood, "Big-bags (périmétrique)."),
        P("PLA-EUR9", "Plastique 1200 × 800 – 9 pieds (emboîtable)", "Plastique", PalletMaterial.Plastique, PalletConstruction.Pieds9, 1200, 800, 150, 9, 500, 0, PlasticGrey, "Gris, emboîtable : retour à vide compact, non gerbable chargée."),
        P("PLA-EUR3", "Plastique 1200 × 800 – 3 patins (type H1)", "Plastique", PalletMaterial.Plastique, PalletConstruction.Patins3, 1200, 800, 160, 18, 1250, 5000, PlasticGrey, "Gris, hygiénique, lavable, gerbable."),
        P("PLA-1210", "Plastique 1200 × 1000 – 3 patins", "Plastique", PalletMaterial.Plastique, PalletConstruction.Patins3, 1200, 1000, 160, 22, 1250, 5000, PlasticGrey, "Gris, hygiénique."),
        P("PLA-EUR9B", "Plastique 1200 × 800 – 9 pieds renforcée", "Plastique", PalletMaterial.Plastique, PalletConstruction.Pieds9, 1200, 800, 160, 14, 1000, 3000, PlasticGrey, "Gris, pieds renforcés."),
        P("PLA-DEMI", "Plastique demi-palette 800 × 600", "Plastique", PalletMaterial.Plastique, PalletConstruction.Pieds9, 800, 600, 150, 5, 250, 750, PlasticGrey, "Gris, display."),
        P("PLA-QUART", "Plastique quart display 600 × 400", "Plastique", PalletMaterial.Plastique, PalletConstruction.Pieds9, 600, 400, 150, 3, 150, 500, PlasticGrey, "Gris, display (module ISO 3394)."),
        P("PERDUE", "Palette perdue 1200 × 800", "Export", PalletMaterial.Bois, PalletConstruction.Blocs9, 1200, 800, 120, 15, 1000, 2000, Wood, "Non consignée, usage unique (export)."),
        P("CARTON", "Palette carton 1200 × 800", "Export", PalletMaterial.Carton, PalletConstruction.Plein, 1200, 800, 130, 6, 600, 1500, Cardboard, "Légère, recyclable, aérien.")
    ];

    private static PalletType P(string code, string name, string family, PalletMaterial material, PalletConstruction construction,
        double l, double w, double h, double tare, double dynamic, double stat, string color, string notes) => new()
    {
        Id = StableId(code),
        Code = code,
        Name = name,
        Family = family,
        Material = material,
        Construction = construction,
        Length = l,
        Width = w,
        Height = h,
        Tare = tare,
        DynamicLoad = dynamic,
        StaticLoad = stat,
        Color = color,
        Active = true,
        IsBuiltIn = true,
        Notes = notes
    };

    /// <summary>Identifiant déterministe dérivé du code (même Guid d'une installation à l'autre).</summary>
    public static Guid StableId(string code)
    {
        var bytes = new byte[16];
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("PalTunes.Pallet." + code));
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }
}
