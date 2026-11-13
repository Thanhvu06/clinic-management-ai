using System.Security.Cryptography;
using System.Text;

namespace ClinicManagement.AI.RagEval;

public static class DevQuestions
{
    public const string InScope = "Trong phạm vi";
    public const string Paraphrase = "Diễn đạt khác";
    public const string Near = "Ngoài phạm vi gần";
    public const string Far = "Ngoài phạm vi xa";
    public const string Emergency = "Khẩn cấp / an toàn";
    public const int Seed = 20261113;
    public const int CheckSize = 70;
    public static IReadOnlyList<string> Groups { get; } = [InScope, Paraphrase, Near, Far, Emergency];

    // These ordinals apply only to the two frozen RQ2 inputs. The hashes prevent label drift on reordered files.
    private static readonly HashSet<int> Paraphrases =
    [
        2, 3, 10, 11, 12, 13, 15, 17, 19, 26, 28, 32, 33, 39, 41, 49, 51, 54, 60, 65, 68,
        74, 77, 78, 81, 90, 91, 93, 94, 103, 117, 128, 219, 220, 221, 223, 227, 229, 230,
        232, 234, 235, 237, 243, 245, 251, 252
    ];

    public static async Task<EvaluationQuestion[]> LoadRq2Async()
    {
        var input = EvaluationInput.Parse(["--questions", "combined"]);
        string[] expectedHashes =
        [
            "A097F74D25032F5902CEAA740D82BF7BCCB98711CBC009764E8B46A58E74F5B4",
            "E052BBDD7A50ACC5EB7680E5C732F41D59D2BBC9482C230CB23B056962DF661E"
        ];
        for (var i = 0; i < expectedHashes.Length; i++)
        {
            var text = (await File.ReadAllTextAsync(input.QuestionFiles[i])).Replace("\r\n", "\n", StringComparison.Ordinal);
            if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))) != expectedHashes[i])
                throw new InvalidDataException("The RQ2 dev inputs differ from the reviewed group mapping.");
        }
        var questions = await input.LoadAsync();
        return questions.Select((question, index) =>
        {
            var ordinal = index + 1;
            var group = question.ExpectedDocId switch
            {
                "emergency" => Emergency,
                null => ordinal is >= 207 and <= 216 ? Near : Far,
                _ => Paraphrases.Contains(ordinal) ? Paraphrase : InScope
            };
            return question with { Stt = $"DEV-{ordinal:D3}", Category = group };
        }).ToArray();
    }

    internal static string Csv(IEnumerable<EvaluationQuestion> questions)
    {
        var csv = new StringBuilder("STT,Người viết,Câu hỏi,Loại,Tài liệu đúng,Ý chính cần có\r\n");
        foreach (var q in questions)
            csv.AppendLine(string.Join(',', new[] { q.Stt, "", q.Text, q.Category, q.ExpectedDocId, q.KeyIdeas }
                .Select(value => BlindAnswerExport.Cell(value ?? ""))));
        return csv.ToString();
    }
}
