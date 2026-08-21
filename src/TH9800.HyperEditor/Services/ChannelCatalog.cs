using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using TH9800.HyperEditor.Models;

namespace TH9800.HyperEditor.Services;

public static class ChannelCatalog
{
    public static IReadOnlyList<ChannelOption> Empty() =>
        Enumerable.Range(1, 800).Select(number => new ChannelOption(number, string.Empty, string.Empty)).ToList();

    public static IReadOnlyList<ChannelOption> Load(string path)
    {
        using var parser = new TextFieldParser(path) { TextFieldType = FieldType.Delimited };
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var headers = parser.ReadFields() ?? throw new InvalidDataException("CSV has no header row.");
        var memoryIndex = Find(headers, "Memory", "Location");
        var nameIndex = Find(headers, "Name");
        var rxIndex = Find(headers, "RX MHz", "Frequency");
        var byNumber = new Dictionary<int, ChannelOption>();

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null || memoryIndex >= fields.Length) continue;
            if (!int.TryParse(fields[memoryIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) continue;
            if (number is < 1 or > 800) continue;
            var name = nameIndex < fields.Length ? fields[nameIndex] : string.Empty;
            var rx = rxIndex < fields.Length ? fields[rxIndex] : string.Empty;
            byNumber[number] = new ChannelOption(number, name, rx);
        }

        return Enumerable.Range(1, 800)
            .Select(number => byNumber.TryGetValue(number, out var item) ? item : new ChannelOption(number, string.Empty, string.Empty))
            .ToList();
    }

    private static int Find(string[] headers, params string[] names)
    {
        foreach (var name in names)
        {
            var index = Array.FindIndex(headers, header => string.Equals(header.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
        }
        throw new InvalidDataException($"CSV is missing required column: {string.Join(" or ", names)}.");
    }
}
