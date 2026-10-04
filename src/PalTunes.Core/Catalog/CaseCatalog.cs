using PalTunes.Core.Models;

namespace PalTunes.Core.Catalog;

/// <summary>
/// Caisses livrées par défaut : cartons américains (FEFCO 0201) aux formats modulaires ISO 3394 (600 × 400 et
/// sous-multiples, qui pavent exactement les palettes 1200 × 800 et 1200 × 1000), bacs plastiques et caisses bois.
/// Dimensions intérieures en mm.
/// </summary>
public static class CaseCatalog
{
    private const string Kraft = "#C9A26B";
    private const string KraftDark = "#A9824E";
    private const string Grey = "#8E969D";
    private const string Wood = "#C8A165";

    public static IReadOnlyList<CaseType> Defaults() =>
    [
        C("CRT-2015", "Carton 200 × 150 × 100", "Carton standard", CaseMaterial.CartonSimple, 194, 144, 94, 3, 0.08, 5, Kraft),
        C("CRT-3020", "Carton 300 × 200 × 150 (module 1/4)", "Carton modulaire ISO", CaseMaterial.CartonSimple, 294, 194, 144, 3, 0.15, 10, Kraft),
        C("CRT-3020H", "Carton 300 × 200 × 200 (module 1/4)", "Carton modulaire ISO", CaseMaterial.CartonSimple, 294, 194, 194, 3, 0.18, 10, Kraft),
        C("CRT-4030", "Carton 400 × 300 × 200 (module 1/2)", "Carton modulaire ISO", CaseMaterial.CartonSimple, 394, 294, 194, 3, 0.30, 15, Kraft),
        C("CRT-4030H", "Carton 400 × 300 × 300 (module 1/2)", "Carton modulaire ISO", CaseMaterial.CartonDouble, 390, 290, 290, 5, 0.45, 20, KraftDark),
        C("CRT-6040", "Carton 600 × 400 × 200 (module 1)", "Carton modulaire ISO", CaseMaterial.CartonDouble, 590, 390, 190, 5, 0.60, 25, KraftDark),
        C("CRT-6040H", "Carton 600 × 400 × 300 (module 1)", "Carton modulaire ISO", CaseMaterial.CartonDouble, 590, 390, 290, 5, 0.75, 30, KraftDark),
        C("CRT-6040X", "Carton 600 × 400 × 400 (module 1)", "Carton modulaire ISO", CaseMaterial.CartonDouble, 590, 390, 390, 5, 0.90, 30, KraftDark),
        C("CRT-8060", "Carton 800 × 600 × 400", "Carton standard", CaseMaterial.CartonTriple, 786, 586, 386, 7, 1.60, 40, KraftDark),
        C("CRT-1208", "Carton palette 1200 × 800 × 600 (box)", "Carton standard", CaseMaterial.CartonTriple, 1186, 786, 586, 7, 4.50, 300, KraftDark),
        C("BAC-4030", "Bac plastique 400 × 300 × 220", "Bac plastique", CaseMaterial.Plastique, 365, 265, 200, 15, 1.10, 25, Grey),
        C("BAC-6040", "Bac plastique 600 × 400 × 220", "Bac plastique", CaseMaterial.Plastique, 565, 365, 200, 15, 1.90, 40, Grey),
        C("BAC-6040H", "Bac plastique 600 × 400 × 320", "Bac plastique", CaseMaterial.Plastique, 565, 365, 300, 15, 2.40, 50, Grey),
        C("BOIS-8060", "Caisse bois 800 × 600 × 500", "Caisse bois", CaseMaterial.Bois, 764, 564, 464, 18, 18, 250, Wood),
        C("BOIS-1208", "Caisse bois 1200 × 800 × 800", "Caisse bois", CaseMaterial.Bois, 1164, 764, 764, 18, 40, 600, Wood)
    ];

    private static CaseType C(string code, string name, string family, CaseMaterial material, double l, double w, double h,
        double wall, double tare, double max, string color) => new()
    {
        Id = PalletCatalog.StableId("Case." + code),
        Code = code,
        Name = name,
        Family = family,
        Material = material,
        InnerLength = l,
        InnerWidth = w,
        InnerHeight = h,
        WallThickness = wall,
        Tare = tare,
        MaxWeight = max,
        Color = color,
        Active = true,
        IsBuiltIn = true
    };
}
