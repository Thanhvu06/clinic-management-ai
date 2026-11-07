using System.Text.RegularExpressions;
using ClinicManagement.Application.AI.Knowledge;

namespace ClinicManagement.Infrastructure.AI.Knowledge;

public sealed class MarkdownKnowledgeCatalog
{
    public IReadOnlyList<KnowledgeChunk> Chunks { get; }
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "AI", "Knowledge", "Documents");

    public MarkdownKnowledgeCatalog(string? directory = null)
    {
        Chunks = Directory.GetFiles(directory ?? DefaultDirectory, "*.md").Order(StringComparer.Ordinal)
            .SelectMany(path => Parse(File.ReadAllText(path))).ToArray();
        if (Chunks.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != Chunks.Count)
            throw new InvalidDataException("Duplicate knowledge document IDs.");
    }

    public static IReadOnlyList<KnowledgeChunk> Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 6 || lines[0] != "---") throw new InvalidDataException("Missing knowledge front matter.");
        var end = Array.IndexOf(lines, "---", 1);
        if (end < 0) throw new InvalidDataException("Unclosed knowledge front matter.");
        var metadata = lines[1..end].Select(line => line.Split(':', 2))
            .Where(pair => pair.Length == 2).ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());
        foreach (var key in new[] { "id", "title", "category", "updated" })
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException("Missing knowledge metadata: " + key);
        var chunks = new List<KnowledgeChunk>();
        var heading = metadata["title"];
        var section = new List<string>();
        void Flush()
        {
            var text = Regex.Replace(string.Join(' ', section), @"\s+", " ").Trim();
            for (var start = 0; start < text.Length;)
            {
                var length = Math.Min(550, text.Length - start);
                if (start + length < text.Length)
                {
                    var boundary = text.LastIndexOf(' ', start + length - 1, length);
                    if (boundary > start + 300) length = boundary - start;
                }
                chunks.Add(new KnowledgeChunk($"{metadata["id"]}:{chunks.Count}",
                    new KnowledgeSource(metadata["id"], metadata["title"], heading), text.Substring(start, length).Trim()));
                if (start + length >= text.Length) break;
                var next = text.IndexOf(' ', start + length - 50);
                start = next < 0 ? start + length : next + 1;
            }
            section.Clear();
        }
        foreach (var line in lines[(end + 1)..])
        {
            if (Regex.IsMatch(line, @"^#{1,6}\s")) { Flush(); heading = line.TrimStart('#', ' '); }
            else if (!string.IsNullOrWhiteSpace(line)) section.Add(line.Trim());
        }
        Flush();
        return chunks;
    }
}
