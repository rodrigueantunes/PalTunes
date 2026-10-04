namespace PalTunes.Core.Models;

/// <summary>
/// Client de la base. Les articles le référencent par son code (champ « Client » de l'article, colonne CLIENT de
/// l'import) ; un changement de code est répercuté sur les articles. Affiché seul, il apparaît « CODE - Nom ».
/// </summary>
public sealed class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Contact { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>Palette imposée par défaut pour les conditionnements de ses articles (null = EUR 1).</summary>
    public Guid? DefaultPalletId { get; set; }

    /// <summary>Hauteur totale maximale imposée par le client (mm). Null = valeur standard (1800).</summary>
    public double? MaxTotalHeight { get; set; }

    /// <summary>Niveaux de la pile acceptés (stockage interne, 1 = non gerbable). Null = non renseigné.</summary>
    public int? MaxStackLevels { get; set; }

    /// <summary>Gerbages acceptés par le client : 0 = non gerbable, 1 = un conditionnement gerbé dessus… Null = non renseigné.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? MaxStacking
    {
        get => MaxStackLevels is { } l ? Math.Max(0, l - 1) : null;
        set => MaxStackLevels = value is { } v ? Math.Max(0, v) + 1 : null;
    }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    public Client Clone() => (Client)MemberwiseClone();

    public string DisplayName => string.IsNullOrWhiteSpace(Code) || Code == Name ? Name : $"{Name} ({Code})";

    /// <summary>Libellé quand seul le client est affiché : « CODE - Nom ».</summary>
    public string Label => string.IsNullOrWhiteSpace(Code) ? Name : string.IsNullOrWhiteSpace(Name) || Code == Name ? Code : $"{Code} - {Name}";

    public string Location => string.Join(" ", new[] { PostalCode, City }.Where(s => !string.IsNullOrWhiteSpace(s))) +
                              (string.IsNullOrWhiteSpace(Country) ? "" : $" · {Country}");

    public override string ToString() => Label;

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Code))
        {
            errors.Add("Le code client est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Le nom du client est obligatoire.");
        }

        if (MaxTotalHeight is <= 0)
        {
            errors.Add("Hauteur maxi : valeur > 0 ou vide.");
        }

        if (MaxStackLevels is < 1)
        {
            errors.Add("Gerbages : 0 ou plus, ou vide.");
        }

        return errors;
    }

    /// <summary>Code proposé à partir du nom : majuscules sans accents, 12 caractères.</summary>
    public static string CodeFromName(string name)
    {
        var normalized = Import.Csv.NormalizeHeader(name);
        return normalized.Length > 12 ? normalized[..12].TrimEnd('_') : normalized;
    }
}

/// <summary>Formats d'affichage partagés.</summary>
public static class Formats
{
    /// <summary>Poids unitaire d'un article : jusqu'à 5 décimales (0,00001 kg).</summary>
    public const string UnitWeight = "0.#####";

    /// <summary>Poids cumulés (charge, total) : séparateur de milliers, 3 décimales au plus.</summary>
    public const string TotalWeight = "#,0.###";
}
