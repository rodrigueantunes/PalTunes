namespace PalTunes.Core.Models;

/// <summary>Palette physique (support), étude §8 et §9.2. Dimensions en mm, masses en kg.</summary>
public sealed class PalletType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "Europe";
    public PalletMaterial Material { get; set; } = PalletMaterial.Bois;
    public PalletConstruction Construction { get; set; } = PalletConstruction.Blocs9Semelles3;

    public double Length { get; set; }
    public double Width { get; set; }

    /// <summary>Hauteur de bois : hauteur du support seul.</summary>
    public double Height { get; set; }

    public double Tare { get; set; }

    /// <summary>Charge dynamique admissible (manutention), utilisée comme poids maxi par défaut.</summary>
    public double DynamicLoad { get; set; }

    /// <summary>Charge statique admissible (gerbage au sol). 0 = non renseignée.</summary>
    public double StaticLoad { get; set; }

    /// <summary>Couleur de représentation (#RRGGBB).</summary>
    public string Color { get; set; } = "#C8A165";

    /// <summary>Incluse dans les propositions automatiques.</summary>
    public bool Active { get; set; } = true;

    public bool IsBuiltIn { get; set; }

    public string? Notes { get; set; }

    public PalletType Clone() => (PalletType)MemberwiseClone();

    public string DimensionsText => $"{Length:0} × {Width:0} × {Height:0}";

    public string MaterialLabel => Material switch
    {
        PalletMaterial.Plastique => "Plastique",
        PalletMaterial.Carton => "Carton",
        PalletMaterial.Metal => "Métal",
        PalletMaterial.BoisMoule => "Bois moulé",
        _ => "Bois"
    };

    public static string ConstructionLabel(PalletConstruction c) => c switch
    {
        PalletConstruction.Blocs9Semelles3 => "9 blocs + 3 semelles (EUR)",
        PalletConstruction.Blocs9 => "9 blocs sans semelle",
        PalletConstruction.Patins3 => "Plateau + 3 patins",
        PalletConstruction.Pieds9 => "Plateau + 9 pieds (emboîtable)",
        _ => "Bloc plein"
    };

    public override string ToString() => $"{Code} – {Name} ({DimensionsText})";

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code))
        {
            errors.Add("Le code palette est obligatoire.");
        }

        if (!(Length > 0) || !(Width > 0) || !(Height > 0))
        {
            errors.Add("Longueur, largeur et hauteur de bois doivent être > 0.");
        }

        if (Tare < 0 || DynamicLoad < 0 || StaticLoad < 0)
        {
            errors.Add("Tare et charges admissibles ne peuvent pas être négatives.");
        }

        return errors;
    }
}
