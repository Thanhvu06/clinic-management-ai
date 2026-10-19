using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ClinicManagement.AI.Training;

public static class RoleIntentHumanData
{
    public const string DataFile = "role_intent_human_train_v1.tsv";
    public const string ManifestFile = "role_intent_human_train_v1_manifest.json";
    public const string SupplementFile = "role_intent_human_train_v1b.tsv";
    public const string SupplementManifestFile = "role_intent_human_train_v1b_manifest.json";
    public const string SupplementProvenance = "Nhiều người viết, Nguyễn Thanh Vũ tổng hợp và duyệt nhãn, 2026-10-19, không dùng AI; bổ sung câu sát ranh giới cho các cặp nhầm trên validation";
    public const string Source = "human_crowd_2026-10-19";
    public const string Provenance = "Nhiều người viết, Nguyễn Thanh Vũ tổng hợp và duyệt nhãn, 2026-10-19, không dùng AI; 2 câu gần giống bộ đề ẩn đã được viết lại trước khi nộp";
    internal const string Header = "vai_tro\tcau\tnhan\tnguon";
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly string[] Headers = ["STT", "Vai trò", "Câu train", "Nhãn đúng", "Ghi chú", "Kiểm tra"];
    private static readonly Dictionary<string, string> Roles = new(StringComparer.Ordinal)
    {
        ["Bệnh nhân"] = "BenhNhan", ["Lễ tân"] = "LeTan", ["Bác sĩ"] = "BacSi",
        ["Kỹ thuật viên"] = "KTV", ["Dược sĩ"] = "DuocSi", ["Quản trị"] = "Admin"
    };

    public static RoleIntentHumanManifest Import(string workbook, string dataDirectory, bool supplementary = false)
    {
        using var stream = File.OpenRead(workbook);
        var rows = ReadWorkbook(stream);
        Validate(rows, dataDirectory, supplementary ? 50 : 310);
        if (supplementary) CheckBatches(Read(dataDirectory), rows);
        var tsv = Header + "\n" + string.Join("\n", rows.Select(r => $"{r.Role}\t{r.Text}\t{r.Label}\t{Source}")) + "\n";
        var bytes = new UTF8Encoding(false).GetBytes(tsv);
        var manifest = new RoleIntentHumanManifest(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
            rows.Length, rows.GroupBy(x => x.Label).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
            rows.GroupBy(x => RoleIntentDatasetGenerator.RoleCodes[x.Role]).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
            supplementary ? SupplementProvenance : Provenance, RoleIntentDatasetGenerator.FileSha256(workbook));
        File.WriteAllBytes(Path.Combine(dataDirectory, supplementary ? SupplementFile : DataFile), bytes);
        File.WriteAllText(Path.Combine(dataDirectory, supplementary ? SupplementManifestFile : ManifestFile),
            JsonSerializer.Serialize(manifest, RoleIntentModelPipeline.JsonOptions).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        return manifest;
    }

    internal static RoleIntentRecord[] ReadWorkbook(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var sheets = Xml(archive, "xl/workbook.xml").Descendants(Ns + "sheet").ToArray();
        var sheet = sheets.SingleOrDefault(x => (string?)x.Attribute("name") == "Câu train");
        if (sheet is null) throw new InvalidDataException("Sheets: " + string.Join(" | ", sheets.Select(x => (string?)x.Attribute("name"))));
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var id = (string?)sheet.Attribute(relationships + "id");
        var relation = Xml(archive, "xl/_rels/workbook.xml.rels").Root!.Elements().Single(x => (string?)x.Attribute("Id") == id);
        if ((string?)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("External sheet is unsupported.");
        var target = (string?)relation.Attribute("Target") ?? throw new InvalidDataException("Missing sheet target.");
        var path = new Uri(new Uri("https://xlsx.local/xl/workbook.xml"), target).AbsolutePath.TrimStart('/');
        var shared = archive.GetEntry("xl/sharedStrings.xml") is null ? [] : Xml(archive, "xl/sharedStrings.xml").Descendants(Ns + "si").Select(Text).ToArray();
        var physicalRows = Xml(archive, path).Descendants(Ns + "row").ToArray();
        Dictionary<string, string> Cells(XElement row) => row.Elements(Ns + "c").ToDictionary(
            x => Regex.Replace((string?)x.Attribute("r") ?? "", "[0-9]", ""),
            x => ((string?)x.Attribute("t") switch
            {
                "s" => shared[int.Parse((string?)x.Element(Ns + "v") ?? "-1", CultureInfo.InvariantCulture)],
                "inlineStr" => Text(x), _ => (string?)x.Element(Ns + "v") ?? ""
            }).Trim(), StringComparer.Ordinal);
        var header = physicalRows.Length == 0 ? new Dictionary<string, string>() : Cells(physicalRows[0]);
        if (!Headers.Select((h, i) => header.GetValueOrDefault(((char)('A' + i)).ToString()) == h).All(x => x))
            throw new InvalidDataException("Sheets: " + string.Join(" | ", sheets.Select(x => (string?)x.Attribute("name"))) +
                "; Câu train headers: " + string.Join(" | ", header.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value)));
        var rows = new List<RoleIntentRecord>();
        var ids = new HashSet<int>();
        var errors = new List<string>();
        foreach (var physical in physicalRows.Skip(1))
        {
            var cells = Cells(physical);
            string Cell(string column) => cells.GetValueOrDefault(column, "");
            if (Cell("E").Equals("VÍ DỤ", StringComparison.OrdinalIgnoreCase) || new[] { "B", "C", "D", "E" }.All(c => string.IsNullOrWhiteSpace(Cell(c)))) continue;
            var location = "Excel row " + (string?)physical.Attribute("r");
            if (!int.TryParse(Cell("A"), NumberStyles.None, CultureInfo.InvariantCulture, out var stt) || stt <= 0 || !ids.Add(stt))
                errors.Add(location + ": invalid or duplicate STT.");
            if (!Roles.TryGetValue(Cell("B"), out var role)) errors.Add(location + ": invalid role.");
            if (new[] { Cell("C"), Cell("D") }.Any(x => string.IsNullOrWhiteSpace(x) || x.Contains('\t') || x.Contains('\r') || x.Contains('\n')))
                errors.Add(location + ": missing or multiline text/label.");
            rows.Add(new($"HUMAN-{stt:D3}", $"HUMAN-{stt:D3}", role ?? "", Cell("D"), Cell("C"), "train", "human_original", Source));
        }
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        return rows.ToArray();
    }

    public static RoleIntentRecord[] Read(string dataDirectory)
        => ReadBatch(dataDirectory, DataFile, ManifestFile, 310, Provenance);

    public static RoleIntentRecord[] ReadAll(string dataDirectory)
    {
        var original = Read(dataDirectory);
        var supplement = ReadBatch(dataDirectory, SupplementFile, SupplementManifestFile, 50, SupplementProvenance)
            .Select(x => x with { Id = x.Id.Replace("HUMAN-", "HUMAN-B-", StringComparison.Ordinal), SeedId = x.SeedId.Replace("HUMAN-", "HUMAN-B-", StringComparison.Ordinal) }).ToArray();
        CheckBatches(original, supplement);
        return original.Concat(supplement).ToArray();
    }

    internal static void CheckBatches(RoleIntentRecord[] original, RoleIntentRecord[] supplement)
    {
        var known = original.Select(x => RoleIntentDatasetGenerator.Normalize(x.Text)).ToHashSet(StringComparer.Ordinal);
        var duplicates = supplement.Where(x => known.Contains(RoleIntentDatasetGenerator.Normalize(x.Text))).Select(x => x.Id).ToArray();
        if (duplicates.Length > 0) throw new InvalidDataException("Supplement duplicates dot1: " + string.Join(", ", duplicates));
    }

    private static RoleIntentRecord[] ReadBatch(string dataDirectory, string dataFile, string manifestFile, int expectedCount, string provenance)
    {
        var manifest = RoleIntentModelPipeline.Read<RoleIntentHumanManifest>(Path.Combine(dataDirectory, manifestFile));
        if (manifest.Sha256 != RoleIntentDatasetGenerator.FileSha256(Path.Combine(dataDirectory, dataFile)) || manifest.RowCount != expectedCount || manifest.Provenance != provenance)
            throw new InvalidDataException("Human TSV manifest mismatch.");
        var rows = ParseTsv(File.ReadAllText(Path.Combine(dataDirectory, dataFile), new UTF8Encoding(false, true)));
        Validate(rows, dataDirectory, expectedCount);
        var labels = rows.GroupBy(x => x.Label).ToDictionary(x => x.Key, x => x.Count());
        var roles = rows.GroupBy(x => RoleIntentDatasetGenerator.RoleCodes[x.Role]).ToDictionary(x => x.Key, x => x.Count());
        if (!Same(labels, manifest.CountsByLabel) || !Same(roles, manifest.CountsByRole)) throw new InvalidDataException("Human TSV count mismatch.");
        return rows;
    }

    internal static RoleIntentRecord[] ParseTsv(string text)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine() != Header) throw new InvalidDataException("Invalid human TSV header.");
        var rows = new List<RoleIntentRecord>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var columns = line.Split('\t');
            if (columns.Length != 4 || columns.Any(string.IsNullOrWhiteSpace) || columns[3] != Source)
                throw new InvalidDataException($"TSV row {rows.Count + 2}: invalid columns/source.");
            var id = $"HUMAN-{rows.Count + 1:D3}";
            rows.Add(new(id, id, columns[0], columns[2], columns[1], "train", "human_original", columns[3]));
        }
        return rows.ToArray();
    }

    internal static void Validate(RoleIntentRecord[] rows, string dataDirectory, int expectedCount = 310)
    {
        var errors = new List<string>();
        if (rows.Length != expectedCount) errors.Add($"Expected {expectedCount} human rows; found {rows.Length}.");
        var catalog = RoleIntentDatasetGenerator.ReadLabels(dataDirectory).ToDictionary(x => x.Label, StringComparer.Ordinal);
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in new[] { RoleIntentDatasetGenerator.TrainFile, RoleIntentDatasetGenerator.ValidationFile })
            foreach (var row in RoleIntentModelPipeline.Read<RoleIntentRecord[]>(Path.Combine(dataDirectory, file))) known[RoleIntentDatasetGenerator.Normalize(row.Text)] = file;
        foreach (var file in new[] { RoleIntentDatasetGenerator.SeedsFile, RoleIntentModelPipeline.EvalFile })
            foreach (var line in File.ReadLines(Path.Combine(dataDirectory, file)).Skip(1)) known[RoleIntentDatasetGenerator.Normalize(line.Split('\t')[1])] = file;
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var location = $"Row {i + 2} ({row.Id})";
            if (!catalog.TryGetValue(row.Label, out var label) || !RoleIntentDatasetGenerator.RoleCodes.TryGetValue(row.Role, out var role) || !label.Roles.Contains(role, StringComparer.Ordinal))
                errors.Add(location + ": invalid role/label.");
            var normalized = RoleIntentDatasetGenerator.Normalize(row.Text);
            if (normalized.Length == 0) errors.Add(location + ": empty normalized text.");
            if (known.TryGetValue(normalized, out var file)) errors.Add(location + ": duplicate of " + file + ".");
            if (!seen.TryAdd(normalized, i + 2)) errors.Add(location + $": internal duplicate of row {seen[normalized]}.");
        }
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
    }

    private static bool Same(Dictionary<string, int> left, Dictionary<string, int> right) => left.Count == right.Count && left.All(x => right.GetValueOrDefault(x.Key) == x.Value);
    private static string Text(XElement node) => string.Concat(node.Descendants(Ns + "t").Where(t => !t.Ancestors(Ns + "rPh").Any()).Select(t => t.Value));
    private static XDocument Xml(ZipArchive archive, string path)
    {
        using var stream = (archive.GetEntry(path) ?? throw new InvalidDataException("Missing XLSX XML part.")).Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
}

public sealed record RoleIntentHumanManifest(string Sha256, int RowCount, Dictionary<string, int> CountsByLabel,
    Dictionary<string, int> CountsByRole, string Provenance, string SourceWorkbookSha256);
