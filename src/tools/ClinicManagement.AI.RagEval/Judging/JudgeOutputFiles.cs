using System.Text.Json;
using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

internal static class JudgeOutputFiles
{
    public static JudgeRunSummary? ReadPublishedSummary(JudgeOptions options, Guid checkpointId)
    {
        if (!File.Exists(options.Metadata))
        {
            if (File.Exists(options.Output)) throw new JudgeCheckpointException("Judge workbook already exists without matching run metadata.");
            return null;
        }

        using var stream = File.OpenRead(options.Metadata);
        JudgeRunSummary? summary;
        try { summary = JsonSerializer.Deserialize<JudgeRunSummary>(stream, StatisticsReport.JsonOptions); }
        catch (JsonException) { throw new JudgeCheckpointException("Judge run metadata is invalid."); }
        if (summary is null || summary.CheckpointId != checkpointId)
            throw new JudgeCheckpointException("Judge run metadata does not match this checkpoint.");
        return summary;
    }

    public static void PublishSummary(JudgeOptions options, JudgeRunSummary summary, bool overwrite = false) =>
        Publish(options.Metadata, path =>
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(stream, summary, StatisticsReport.JsonOptions);
        }, overwrite);

    public static void PublishWorkbook(JudgeOptions options, XLWorkbook book, bool overwrite) =>
        Publish(options.Output, path => BlindAnswerWorkbook.SaveNew(book, path), overwrite);

    public static void Publish(string destination, Action<string> write, bool overwrite = false)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, $".judge-{Guid.NewGuid():N}.tmp");
        try
        {
            write(staging);
            using (var stream = new FileStream(staging, FileMode.Open, FileAccess.Write, FileShare.None))
                stream.Flush(flushToDisk: true);
            File.Move(staging, destination, overwrite);
        }
        finally
        {
            File.Delete(staging);
        }
    }
}
