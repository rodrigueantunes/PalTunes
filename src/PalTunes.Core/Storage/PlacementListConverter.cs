using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PalTunes.Core.Models;

namespace PalTunes.Core.Storage;

/// <summary>
/// Écriture compacte des produits placés d'une unité de charge (des dizaines de milliers possibles) : table des
/// articles, puis une ligne de texte par produit « article;X;Y;Z;DX;DY;DZ;forme;couche;ordre;poids;Ø intérieur ».
/// Environ 50 octets par produit au lieu de 900. L'ancien format (un objet JSON par produit) reste lu.
/// </summary>
public sealed class PlacementListConverter : JsonConverter<List<Placement>>
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public override List<Placement> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            // Ancien format : un objet JSON par produit.
            var legacy = new List<Placement>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                legacy.Add(JsonSerializer.Deserialize<Placement>(ref reader, options)!);
            }

            return legacy;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Liste de produits placés attendue.");
        }

        var articles = new List<Guid>();
        var items = new List<Placement>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            if (name == "Articles")
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    articles.Add(reader.GetGuid());
                }
            }
            else if (name == "Rows")
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    items.Add(Parse(reader.GetString()!, articles));
                }
            }
            else
            {
                reader.Skip();
            }
        }

        return items;
    }

    private static Placement Parse(string row, List<Guid> articles)
    {
        var f = row.Split(';');
        double D(int i) => i < f.Length && f[i].Length > 0 ? double.Parse(f[i], Inv) : 0;
        return new Placement
        {
            ArticleId = articles[int.Parse(f[0], Inv)],
            X = D(1), Y = D(2), Z = D(3), DX = D(4), DY = D(5), DZ = D(6),
            Shape = (ShapeKind)(int)D(7), Layer = (int)D(8), Sequence = (int)D(9), Weight = D(10), InnerDiameter = D(11)
        };
    }

    public override void Write(Utf8JsonWriter writer, List<Placement> value, JsonSerializerOptions options)
    {
        var index = new Dictionary<Guid, int>();
        foreach (var p in value)
        {
            index.TryAdd(p.ArticleId, index.Count);
        }

        static string N(double v) => Math.Round(v, 3).ToString("0.###", Inv);
        writer.WriteStartObject();
        writer.WriteStartArray("Articles");
        foreach (var id in index.Keys)
        {
            writer.WriteStringValue(id);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("Rows");
        foreach (var p in value)
        {
            writer.WriteStringValue(string.Join(';', index[p.ArticleId].ToString(Inv), N(p.X), N(p.Y), N(p.Z), N(p.DX), N(p.DY), N(p.DZ),
                ((int)p.Shape).ToString(Inv), p.Layer.ToString(Inv), p.Sequence.ToString(Inv), N(p.Weight),
                p.InnerDiameter > 0 ? N(p.InnerDiameter) : ""));
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
