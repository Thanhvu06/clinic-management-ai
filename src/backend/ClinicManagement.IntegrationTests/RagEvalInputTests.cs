extern alias RagEval;

using EvaluationInput = RagEval::ClinicManagement.AI.RagEval.EvaluationInput;

namespace ClinicManagement.IntegrationTests;

public sealed class RagEvalInputTests
{
    [Theory]
    [InlineData("baseline", 156, 60)]
    [InlineData("v2", 36, 10)]
    [InlineData("combined", 192, 70)]
    public async Task Selected_sets_load_independently_and_combined_with_actual_denominators(
        string selection, int inScope, int outside)
    {
        var input = EvaluationInput.Parse(["report.json", "--questions", selection]);

        var questions = await input.LoadAsync();

        Assert.Equal("report.json", input.OutputPath);
        Assert.Equal(inScope, questions.Count(q => q.ExpectedDocId is not null));
        Assert.Equal(outside, questions.Count(q => q.ExpectedDocId is null));
    }

    [Fact]
    public async Task Legacy_arguments_keep_baseline_and_explicit_files_match_combined_alias()
    {
        var legacy = EvaluationInput.Parse(["report.json"]);
        var combined = EvaluationInput.Parse(["--questions", "combined"]);
        var explicitFiles = EvaluationInput.Parse([
            "--questions", combined.QuestionFiles[0], "--questions", combined.QuestionFiles[1]]);

        Assert.Single(legacy.QuestionFiles);
        Assert.EndsWith("rag-dev-questions.json", legacy.QuestionFiles[0]);
        Assert.Null(combined.OutputPath);
        Assert.Equal(await combined.LoadAsync(), await explicitFiles.LoadAsync());
    }

    [Fact]
    public void Invalid_arguments_and_duplicate_selections_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => EvaluationInput.Parse(["--questions"]));
        Assert.Throws<ArgumentException>(() => EvaluationInput.Parse(["--questions", "--unknown"]));
        Assert.Throws<ArgumentException>(() => EvaluationInput.Parse(["--unknown"]));
        Assert.Throws<ArgumentException>(() => EvaluationInput.Parse(["one.json", "two.json"]));
        Assert.Throws<ArgumentException>(() => EvaluationInput.Parse(["--questions", "combined", "--questions", "baseline"]));
    }

    [Theory]
    [InlineData("baseline", "rag-dev-questions.json")]
    [InlineData("v2", "rag-dev-questions-v2.json")]
    public void Output_cannot_overwrite_the_selected_file_or_a_source_copy(string selection, string file)
    {
        var input = EvaluationInput.Parse(["--questions", selection]);
        Assert.Throws<ArgumentException>(() => EvaluationInput.Parse([input.QuestionFiles[0], "--questions", selection]));
        Assert.Throws<ArgumentException>(() => EvaluationInput.Parse([
            Path.Combine("source-copy", file), "--questions", selection]));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"question\":\"\",\"expectedDocId\":null}]")]
    [InlineData("[{\"question\":\"Blank document?\",\"expectedDocId\":\" \"}]")]
    [InlineData("[{\"question\":\"Only outside?\",\"expectedDocId\":null}]")]
    [InlineData("[{\"question\":\"Only inside?\",\"expectedDocId\":\"accounts\"}]")]
    public async Task Empty_or_incomplete_sets_are_not_reported_as_valid_metrics(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "rag-v2-input-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            var input = EvaluationInput.Parse(["--questions", path]);
            await Assert.ThrowsAsync<InvalidDataException>(() => input.LoadAsync());
        }
        finally { File.Delete(path); }
    }
}
