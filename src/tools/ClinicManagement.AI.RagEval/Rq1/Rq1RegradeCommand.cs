using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;

namespace ClinicManagement.AI.RagEval;

internal static class Rq1RegradeCommand
{
    internal static async Task RunAsync(string[] args, TextWriter console)
    {
        var options = Rq1WorkbookCommands.ParseOptions(args, ["--first", "--repeat", "--mapping", "--verify-codex", "--out"]);
        var output = Path.GetFullPath(options["--out"]);
        ExternalOutputDirectory.Validate(Path.GetDirectoryName(output)!);
        StatisticsReport.EnsureNew(output);
        var paths = new[] { "--first", "--repeat", "--mapping", "--verify-codex" }
            .Select(key => Path.GetFullPath(options[key])).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length || paths.Contains(output, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Input and output paths must be distinct.");
        var hashes = paths.ToDictionary(path => path, Hash, StringComparer.OrdinalIgnoreCase);
        using var firstStream = File.Open(paths[0], FileMode.Open, FileAccess.Read, FileShare.Read);
        using var repeatStream = File.Open(paths[1], FileMode.Open, FileAccess.Read, FileShare.Read);
        using var first = new XLWorkbook(firstStream);
        using var repeat = new XLWorkbook(repeatStream);
        var mapping = ReadMapping(paths[2]);
        var report = Rq1Regrade.Analyze(first, repeat, mapping, () =>
        {
            using var stream = File.Open(paths[3], FileMode.Open, FileAccess.Read, FileShare.Read);
            return new XLWorkbook(stream);
        });
        if (hashes.Any(pair => Hash(pair.Key) != pair.Value))
            throw new Rq1RegradeException("An input changed during analysis. No report written.");
        await using var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(destination, new { Hashes = hashes, Report = report }, new JsonSerializerOptions { WriteIndented = true });
        await console.WriteLineAsync($"Content rows matched: {report.MatchingContentRows}; stability ratings: {report.Stability.Agreement.Overall.N}; " +
            $"matching scores: {report.Stability.MatchingScores}; verification: {report.Verification.MatchingScores}/{report.Verification.Agreement.Overall.N}.");
    }

    internal static IReadOnlyList<Rq1RegradeMapping> ReadMapping(string path)
    {
        using var parser = new TextFieldParser(path, System.Text.Encoding.UTF8);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        var header = parser.ReadFields();
        if (header is not ["CL", "STT_goc"]) throw new Rq1RegradeException("Expected CL,STT_goc mapping headers.");
        var mapping = new List<Rq1RegradeMapping>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is not [var cl, var stt] || string.IsNullOrWhiteSpace(stt))
                throw new Rq1RegradeException("Invalid mapping row.");
            if (cl != $"CL{mapping.Count + 1:00}" || mapping.Any(item => item.Stt == stt))
                throw new Rq1RegradeException("Mapping must have unique original identifiers in CL01-CL30 order.");
            mapping.Add(new(cl, stt));
        }
        if (mapping.Count != 30) throw new Rq1RegradeException("Exactly 30 mapped rows are required.");
        return mapping;
    }

    private static string Hash(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
