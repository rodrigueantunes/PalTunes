using System.Globalization;
using System.Text;

namespace PalTunes.Core.Import;

/// <summary>Lecture / écriture CSV : séparateur détecté, guillemets, décimales « , » ou « . ».</summary>
public static class Csv
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static char DetectSeparator(string headerLine)
    {
        var candidates = new[] { ';', '\t', ',', '|' };
        return candidates.OrderByDescending(c => headerLine.Count(ch => ch == c)).First();
    }

    public static List<string[]> Parse(string text, out char separator)
    {
        text = text.TrimStart('﻿');
        var firstLine = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        separator = DetectSeparator(firstLine);
        var rows = new List<string[]>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }

                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == separator)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(sb.ToString());
                sb.Clear();
                if (fields.Any(f => f.Trim().Length > 0))
                {
                    rows.Add(fields.ToArray());
                }

                fields.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }

        fields.Add(sb.ToString());
        if (fields.Any(f => f.Trim().Length > 0))
        {
            rows.Add(fields.ToArray());
        }

        return rows;
    }

    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim().Replace(" ", "").Replace(" ", "").Replace(" ", "");
        if (t.Contains(',') && !t.Contains('.'))
        {
            t = t.Replace(',', '.');
        }
        else if (t.Contains(',') && t.Contains('.'))
        {
            t = t.Replace(",", "");
        }

        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static string Escape(string? value, char separator = ';')
    {
        value ??= "";
        return value.IndexOfAny([separator, '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Number(double value, string format = "0.##") => value.ToString(format, Fr);

    public static string Line(IEnumerable<string?> values, char separator = ';') => string.Join(separator, values.Select(v => Escape(v, separator)));

    /// <summary>Normalise un en-tête : majuscules, sans accents, espaces et tirets en « _ ».</summary>
    public static string NormalizeHeader(string header)
    {
        var formD = header.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        var s = sb.ToString();
        while (s.Contains("__"))
        {
            s = s.Replace("__", "_");
        }

        return s.Trim('_');
    }
}
