using System.Diagnostics;
using System.Text;

namespace ClinicManagement.IntegrationTests;

public class TrackedCSharpEncodingTests
{
    [Fact]
    public async Task AllTrackedCSharpFilesInSrc_DecodeStrictlyWithoutReplacementCharacters()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository != null && !Directory.Exists(Path.Combine(repository.FullName, ".git")) &&
               !File.Exists(Path.Combine(repository.FullName, ".git")))
            repository = repository.Parent;
        Assert.NotNull(repository);

        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true), CreateNoWindow = true
        };
        foreach (var argument in new[] { "-C", repository.FullName, "ls-files", "-z", "--", "src" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        Assert.True(process.ExitCode == 0, error);
        var files = output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(repository.FullName, file));
            Encoding encoding = new UTF8Encoding(false, true);
            var bomLength = 0;
            // A valid explicit Unicode BOM identifies an existing alternative encoding;
            // decode it strictly rather than incorrectly treating its BOM as broken UTF-8.
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
            {
                encoding = new UTF32Encoding(false, true, true);
                bomLength = 4;
            }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
            {
                encoding = new UTF32Encoding(true, true, true);
                bomLength = 4;
            }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            {
                encoding = new UnicodeEncoding(false, true, true);
                bomLength = 2;
            }
            else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            {
                encoding = new UnicodeEncoding(true, true, true);
                bomLength = 2;
            }
            try
            {
                var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
                if (text.Contains('\uFFFD')) failures.Add($"{file}: contains U+FFFD");
            }
            catch (DecoderFallbackException exception)
            {
                failures.Add($"{file}: invalid {encoding.WebName}: {exception.Message}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
