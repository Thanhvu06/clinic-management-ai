using ClosedXML.Excel;

namespace ClinicManagement.AI.RagEval;

internal static class DevPlantWorkbook
{
    public static void Save(string path, IReadOnlyList<DevVariant> variants, IReadOnlyList<DevAnswer> controls,
        IReadOnlyList<(string Kind, int Target, int Actual)> counts, int answerCount, int plantCount)
    {
        using var workbook = new XLWorkbook();
        var review = workbook.AddWorksheet("Duyet");
        Fill(review, ["Mã câu", "Phía A/B", "Chế độ LLM/trích đoạn", "Câu gốc", "Câu đã sửa", "Loại", "Vị trí", "Duyệt OK/Không OK"],
            [14, 10, 20, 66, 66, 35, 13, 23],
            variants.Select(v => new[] { v.Answer.Code, v.Answer.Side, v.Answer.Mode, v.Answer.Text, v.Edited, v.Kind, v.Position, "" }));
        if (variants.Count > 0)
        {
            var approvals = review.Range(2, 8, variants.Count + 1, 8).CreateDataValidation();
            approvals.List("\"OK,Không OK\"", true);
            approvals.IgnoreBlanks = true;
        }

        var automatic = workbook.AddWorksheet("TuDong");
        Fill(automatic, ["STT", "Mã câu", "Phía A/B", "Câu trích đoạn nguyên văn", "Mã đoạn nguồn"],
            [10, 14, 10, 82, 28],
            controls.OrderBy(a => a.Code, StringComparer.Ordinal).Select((a, i) =>
                new[] { (i + 1).ToString(), a.Code, a.Side, a.Text, a.ExactSourceId! }));

        var config = workbook.AddWorksheet("CauHinh");
        var settings = new List<string[]>
        {
            new[] { "Phạm vi", "dev-kiem. Các bản sửa là ứng viên để duyệt, chỉ dùng kiểm định." },
            new[] { "Seed chia và cài", DevQuestions.Seed.ToString() },
            new[] { "Seed blind-answers", "20261110" },
            new[] { "Retrieval blind-answers", "hybrid" },
            new[] { "Model", DevPlantModel.Name },
            new[] { "Tham số qwen", FormattableString.Invariant($"temperature={DevPlantModel.Temperature}, num_ctx={DevPlantModel.Context}, num_predict={DevPlantModel.MaxTokens}, JSON") },
            new[] { "Seed từng lượt qwen", "Random(20261113).Next() theo thứ tự gọi." },
            new[] { "Tổng câu trả lời A/B", answerCount.ToString() },
            new[] { "Câu trả lời giữ nguyên trong phép cài bịa", (answerCount - plantCount).ToString() },
            new[] { "Câu trích đoạn nguyên văn trong TuDong", controls.Count.ToString() },
            new[] { "Prompt hệ thống", DevPlantModel.SystemPrompt },
            new[] { "Mẫu prompt người dùng", DevPlantModel.InputFormat },
            new[] { "Thêm ngoài nguồn, dự kiến đúng y khoa", DevPlantModel.AddedTrue },
            new[] { "Thêm ngoài nguồn, dự kiến sai y khoa", DevPlantModel.AddedFalse },
            new[] { "Đổi đối tượng hoặc điều kiện", DevPlantModel.Condition },
            new[] { "Không an toàn", DevPlantModel.Unsafe },
            new[] { "Diễn đạt lại đúng nguồn", DevPlantModel.Paraphrase }
        };
        settings.AddRange(counts.Select(count => new[] { count.Kind, $"{count.Actual}/{count.Target}" }));
        Fill(config, ["Thông số", "Giá trị"], [48, 105], settings);
        BlindAnswerWorkbook.SaveNew(workbook, path);
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
            if (sheet.Name == "TuDong") sheet.Cell(row, 1).Value = row - 1;
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
