using TrainingProgram = ClinicManagement.AI.Training.Program;

namespace ClinicManagement.IntegrationTests;

public sealed class IntentTrainingOutputDirectoryTests
{
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    [Fact]
    public void Explicit_absolute_output_directory_is_preserved()
    {
        var output = Path.Combine(Path.GetTempPath(), "cliniccare-intent-output");
        Assert.Equal(output, TrainingProgram.ResolveIntentOutputDirectory(output, RepositoryRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Default_output_directory_is_infrastructure_models_from_repo_or_training_directory(bool fromTrainingDirectory)
    {
        var root = RepositoryRoot;
        var start = fromTrainingDirectory
            ? Path.Combine(root, "src", "tools", "ClinicManagement.AI.Training")
            : root;
        var expected = Path.Combine(root, "src", "backend", "ClinicManagement.Infrastructure", "models");
        Assert.Equal(expected, TrainingProgram.ResolveIntentOutputDirectory(null, start));
    }
}
