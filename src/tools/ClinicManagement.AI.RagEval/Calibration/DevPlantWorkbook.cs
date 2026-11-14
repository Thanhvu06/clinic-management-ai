using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

internal static class DevPlantWorkbook
{
    public static void Save(string path, DevPlantResult result, string digest)
    {
        using var workbook = new XLWorkbook();
        var review = workbook.AddWorksheet("Duyet");
        Fill(review, ["Mã biến thể", "Mã câu gốc", "Phía A/B", "Chế độ", "Nhóm", "Câu trả lời gốc",
                "Câu đưa vào biến thể", "Biến thể", "Loại", "Vị trí", "Duyệt"],
            [17, 14, 10, 15, 21, 66, 62, 66, 35, 13, 16],
            ReviewRows(result));
        if (result.Variants.Length > 0)
        {
            var approvals = review.Range(2, 11, result.Variants.Length + 1, 11).CreateDataValidation();
            approvals.List("\"OK,Không OK\"", true);
            approvals.IgnoreBlanks = true;
        }

        Fill(workbook.AddWorksheet("Goc"), ["Mã câu gốc", "Phía A/B", "Chế độ", "Nhóm", "Câu trả lời gốc", "Có nội dung"],
            [14, 10, 15, 21, 88, 18], result.Originals.Select(a => new[] { a.Code, a.Side, a.Mode,
                result.Questions[a.Code].Category!, a.Text, a.HasContent ? "Có" : "Không" }));

        Fill(workbook.AddWorksheet("TuDong"), ["Mã câu gốc", "Phía A/B", "Thứ tự câu", "Câu trích đoạn nguyên văn", "Mã đoạn nguồn"],
            [14, 10, 13, 82, 28], result.Controls.Select(c => new[] { c.Answer.Code, c.Answer.Side,
                c.Sentence.ToString(), c.Text, c.SourceId }));

        var settings = new List<string[]>
        {
            new[] { "Phạm vi", "dev-kiem. Các biến thể ở Duyet cần được duyệt và chỉ dùng để kiểm định." },
            new[] { "Seed chọn câu", DevPlantModel.Seed.ToString() },
            new[] { "Model sinh biến thể", DevPlantModel.Name },
            new[] { "Model digest", digest },
            new[] { "Seed từng lượt qwen", DevPlantModel.Seed.ToString() },
            new[] { "Tham số qwen", FormattableString.Invariant($"temperature={DevPlantModel.Temperature}, num_ctx={DevPlantModel.Context}, num_predict={DevPlantModel.MaxTokens}, JSON") },
            new[] { "Câu trả lời gốc giữ nguyên", result.Originals.Length.ToString() },
            new[] { "Câu trả lời đủ điều kiện cài bịa", result.Eligible.ToString() },
            new[] { "Câu trích đoạn nguyên văn trong TuDong", result.Controls.Length.ToString() },
            new[] { "Prompt hệ thống", DevPlantModel.SystemPrompt },
            new[] { "Mẫu prompt người dùng", DevPlantModel.InputFormat },
            new[] { DevPlantRun.AddedTrueKind, DevPlantModel.AddedTrue },
            new[] { DevPlantRun.AddedFalseKind, DevPlantModel.AddedFalse },
            new[] { "Lượt bổ sung ngoài nguồn", DevPlantModel.AddedRetry },
            new[] { DevPlantRun.ConditionKind, DevPlantModel.Condition },
            new[] { "Lượt bổ sung đổi điều kiện", DevPlantModel.ConditionRetry },
            new[] { DevPlantRun.UnsafeKind, DevPlantModel.Unsafe },
            new[] { "Kịch bản khẩn cấp: trì hoãn", DevPlantModel.EmergencyWait },
            new[] { "Kịch bản khẩn cấp: tự theo dõi", DevPlantModel.EmergencySelfCare },
            new[] { "Kịch bản trong phạm vi: trì hoãn", DevPlantModel.ScopeDelay },
            new[] { DevPlantRun.ParaphraseKind, DevPlantModel.Paraphrase },
            new[] { "Lượt bổ sung diễn đạt lại", DevPlantModel.ParaphraseRetry },
            new[] { "Cách diễn đạt bổ sung từ cùng câu", DevPlantModel.ParaphraseAlternative }
        };
        settings.AddRange(DevPlantScenarios.Conditions.Select(pair => new[] { "Kịch bản đổi điều kiện", $"{pair.From} → {pair.To}" }));
        settings.AddRange(DevPlantScenarios.OutsideSource.SelectMany(scenario => new[]
        {
            new[] { "Kịch bản ngoài nguồn dự kiến đúng", scenario.True },
            new[] { "Kịch bản ngoài nguồn dự kiến sai", scenario.False }
        }));
        settings.AddRange(DevPlantRun.Targets.Select(t => new[] { $"Số mẫu | {t.Kind}", $"{result.Variants.Count(v => v.Kind == t.Kind)}/{t.Target}" }));
        Fill(workbook.AddWorksheet("CauHinh"), ["Thông số", "Giá trị"], [48, 105], settings);
        BlindAnswerWorkbook.SaveNew(workbook, path);
    }

    private static IEnumerable<string[]> ReviewRows(DevPlantResult result)
    {
        var sequences = new Dictionary<(string Code, string Side, string Kind), int>();
        foreach (var variant in result.Variants)
        {
            var answer = variant.Answer;
            var kind = variant.Kind switch
            {
                DevPlantRun.UnsafeKind => "AT",
                DevPlantRun.ParaphraseKind => "DD",
                _ => "CB"
            };
            var key = (answer.Code, answer.Side, kind);
            var sequence = sequences[key] = sequences.GetValueOrDefault(key) + 1;
            yield return [$"{answer.Code}-{answer.Side}-{kind}{sequence:D2}", answer.Code, answer.Side, answer.Mode,
                result.Questions[answer.Code].Category!, answer.Text, variant.OriginalSentence ?? "", variant.Edited,
                variant.Kind, variant.Position, ""];
        }
    }

    public static async Task CompareAsync(string firstPath, string secondPath, TextWriter output)
    {
        using var first = new XLWorkbook(firstPath);
        using var second = new XLWorkbook(secondPath);
        foreach (var sheet in new[] { "Duyet", "Goc", "TuDong", "CauHinh" })
        {
            var a = ReadRows(first.Worksheet(sheet));
            var b = ReadRows(second.Worksheet(sheet));
            if (sheet == "Duyet")
                foreach (var (kind, _) in DevPlantRun.Targets)
                    await CompareRowsAsync(kind, a.Where(r => r[8] == kind).ToArray(),
                        b.Where(r => r[8] == kind).ToArray(), output);
            await CompareRowsAsync(sheet, a, b, output);
        }
    }

    private static string[][] ReadRows(IXLWorksheet sheet)
    {
        var columns = sheet.LastColumnUsed()!.ColumnNumber();
        return sheet.RowsUsed().Where(row => row.RowNumber() > 1)
            .Select(row => Enumerable.Range(1, columns).Select(column => row.Cell(column).GetString()).ToArray()).ToArray();
    }

    private static Task CompareRowsAsync(string name, string[][] a, string[][] b, TextWriter output)
    {
        var remaining = a.GroupBy(row => System.Text.Json.JsonSerializer.Serialize(row))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var same = 0;
        foreach (var row in b)
        {
            var key = System.Text.Json.JsonSerializer.Serialize(row);
            if (remaining.GetValueOrDefault(key) <= 0) continue;
            same++;
            remaining[key]--;
        }
        return output.WriteLineAsync($"So sánh {name} | lần 1: {a.Length} | lần 2: {b.Length} | trùng nội dung: {same}");
    }

    private static void Fill(IXLWorksheet sheet, string[] headers, double[] widths, IEnumerable<string[]> rows)
    {
        sheet.SheetView.FreezeRows(1);
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Column(i + 1).Width = widths[i];
        }
        var row = 2;
        foreach (var values in rows)
        {
            for (var i = 0; i < values.Length; i++) sheet.Cell(row, i + 1).Value = values[i];
            row++;
        }
        var header = sheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.DarkSlateBlue;
        sheet.Row(1).Height = 26;
        var table = sheet.Range(1, 1, row - 1, headers.Length);
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        table.Style.Alignment.WrapText = true;
        if (row > 2)
        {
            table.SetAutoFilter();
            sheet.Rows(2, row - 1).AdjustToContents(22, 220);
        }
    }
}
