using PalTunes.Core.Engine;

namespace PalTunes.Core.Models;

public enum CaseMaterial
{
    CartonSimple,
    CartonDouble,
    CartonTriple,
    Plastique,
    Bois
}

/// <summary>
/// Caisse du catalogue (carton, bac, caisse bois), à l'image des palettes : dimensions intérieures utiles, paroi,
/// tare et charge maximale. Dimensions en mm, masses en kg.
/// </summary>
public sealed class CaseType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "Carton standard";
    public CaseMaterial Material { get; set; } = CaseMaterial.CartonSimple;

    public double InnerLength { get; set; }
    public double InnerWidth { get; set; }
    public double InnerHeight { get; set; }
    public double WallThickness { get; set; } = 3;
    public double Tare { get; set; }

    /// <summary>Poids maximal du contenu. 0 = non limité.</summary>
    public double MaxWeight { get; set; }

    public string Color { get; set; } = "#C9A26B";

    /// <summary>Comparée par « Proposer la meilleure caisse ».</summary>
    public bool Active { get; set; } = true;

    public bool IsBuiltIn { get; set; }
    public string? Notes { get; set; }

    public double OuterLength => InnerLength + 2 * WallThickness;
    public double OuterWidth => InnerWidth + 2 * WallThickness;
    public double OuterHeight => InnerHeight + 2 * WallThickness;

    public string InnerText => $"{InnerLength:0} × {InnerWidth:0} × {InnerHeight:0}";
    public string OuterText => $"{OuterLength:0} × {OuterWidth:0} × {OuterHeight:0}";

    public string MaterialLabel => MaterialName(Material);

    public static string MaterialName(CaseMaterial m) => m switch
    {
        CaseMaterial.CartonDouble => "Carton double cannelure",
        CaseMaterial.CartonTriple => "Carton triple cannelure",
        CaseMaterial.Plastique => "Bac plastique",
        CaseMaterial.Bois => "Caisse bois",
        _ => "Carton simple cannelure"
    };

    public CaseType Clone() => (CaseType)MemberwiseClone();

    public CaseSpec ToSpec(double gap = 0) => new()
    {
        InnerLength = InnerLength,
        InnerWidth = InnerWidth,
        InnerHeight = InnerHeight,
        WallThickness = WallThickness,
        Tare = Tare,
        MaxWeight = MaxWeight,
        Gap = gap
    };

    public override string ToString() => $"{Code} – {Name} ({InnerText})";

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code))
        {
            errors.Add("Le code caisse est obligatoire.");
        }

        if (!(InnerLength > 0) || !(InnerWidth > 0) || !(InnerHeight > 0))
        {
            errors.Add("Les dimensions intérieures doivent être > 0.");
        }

        if (WallThickness < 0 || Tare < 0 || MaxWeight < 0)
        {
            errors.Add("Paroi, tare et charge maxi ne peuvent pas être négatives.");
        }

        return errors;
    }
}
