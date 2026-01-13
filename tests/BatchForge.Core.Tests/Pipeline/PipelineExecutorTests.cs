using BatchForge.Core.Pipeline;
using FluentAssertions;
using Xunit;

namespace BatchForge.Core.Tests.Pipeline;

public class PipelineExecutorTests
{
    [Fact]
    public void Plan_WithNoSteps_ThrowsArgumentException()
    {
        // Arrange & Act
        var act = () => new PipelineExecutor(Array.Empty<IPipelineStep>());

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one step*");
    }

    [Fact]
    public void Plan_WithInvalidOptions_ReturnsErrors()
    {
        // Arrange
        var step = new TestStep();
        var executor = new PipelineExecutor(step);
        var options = new PipelineOptions { InputPath = "" };

        // Act
        var plan = executor.Plan(options);

        // Assert
        plan.IsValid.Should().BeFalse();
        plan.Errors.Should().Contain(e => e.Contains("Input path"));
    }

    [Fact]
    public void Plan_WithNonexistentPath_ReturnsWarning()
    {
        // Arrange
        var step = new TestStep();
        var executor = new PipelineExecutor(step);
        var options = new PipelineOptions { InputPath = "/nonexistent/path/that/does/not/exist" };

        // Act
        var plan = executor.Plan(options);

        // Assert
        plan.Operations.Should().BeEmpty();
        plan.Warnings.Should().Contain(w => w.Contains("No files found"));
    }

    [Fact]
    public async Task ExecuteAsync_WithDryRun_DoesNotProcess()
    {
        // Arrange
        var step = new TestStep();
        var executor = new PipelineExecutor(step);
        var options = new PipelineOptions 
        { 
            InputPath = "/tmp",
            DryRun = true 
        };

        // Act
        var result = await executor.ExecuteAsync(options);

        // Assert
        result.Results.Should().BeEmpty();
        step.ExecutionCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_StopsProcessing()
    {
        // Arrange
        var step = new SlowTestStep();
        var executor = new PipelineExecutor(step);
        using var cts = new CancellationTokenSource();
        
        // Create temp files
        var tempDir = Path.Combine(Path.GetTempPath(), $"batchforge_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                File.WriteAllText(Path.Combine(tempDir, $"test{i}.test"), "content");
            }

            var options = new PipelineOptions
            {
                InputPath = tempDir,
                FilePattern = "*.test",
                MaxParallelism = 1
            };

            // Cancel after a short delay
            cts.CancelAfter(100);

            // Act
            var result = await executor.ExecuteAsync(options, cancellationToken: cts.Token);

            // Assert
            result.WasCancelled.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private class TestStep : IPipelineStep
    {
        public string StepId => "test";
        public string Description => "Test step";
        public IReadOnlyList<string> SupportedExtensions => [".test"];
        public int ExecutionCount { get; private set; }

        public ValidationResult Validate(StepOptions options) => ValidationResult.Valid();

        public string GetOutputPath(string inputPath, StepOptions options) =>
            Path.ChangeExtension(inputPath, ".out");

        public Task<StepResult> ExecuteAsync(
            string inputPath, string outputPath, StepOptions options,
            IProgress<StepProgress>? progress, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            File.Copy(inputPath, outputPath, true);
            return Task.FromResult(StepResult.Success(inputPath, outputPath, TimeSpan.Zero));
        }
    }

    private class SlowTestStep : IPipelineStep
    {
        public string StepId => "slow";
        public string Description => "Slow test step";
        public IReadOnlyList<string> SupportedExtensions => [".test"];

        public ValidationResult Validate(StepOptions options) => ValidationResult.Valid();

        public string GetOutputPath(string inputPath, StepOptions options) =>
            Path.ChangeExtension(inputPath, ".out");

        public async Task<StepResult> ExecuteAsync(
            string inputPath, string outputPath, StepOptions options,
            IProgress<StepProgress>? progress, CancellationToken cancellationToken)
        {
            await Task.Delay(500, cancellationToken);
            return StepResult.Success(inputPath, outputPath, TimeSpan.Zero);
        }
    }
}

public class StepResultTests
{
    [Fact]
    public void Success_CreatesCorrectResult()
    {
        // Act
        var result = StepResult.Success("/input.pdf", "/output.pdf", TimeSpan.FromSeconds(1), 1000, 800);

        // Assert
        result.Outcome.Should().Be(StepOutcome.Succeeded);
        result.InputPath.Should().Be("/input.pdf");
        result.OutputPath.Should().Be("/output.pdf");
        result.Duration.Should().Be(TimeSpan.FromSeconds(1));
        result.InputBytes.Should().Be(1000);
        result.OutputBytes.Should().Be(800);
    }

    [Fact]
    public void Failed_CreatesCorrectResult()
    {
        // Act
        var ex = new InvalidOperationException("test error");
        var result = StepResult.Failed("/input.pdf", "Something went wrong", ex);

        // Assert
        result.Outcome.Should().Be(StepOutcome.Failed);
        result.InputPath.Should().Be("/input.pdf");
        result.Message.Should().Be("Something went wrong");
        result.Exception.Should().Be(ex);
    }

    [Fact]
    public void Skipped_CreatesCorrectResult()
    {
        // Act
        var result = StepResult.Skipped("/input.pdf", "File already processed");

        // Assert
        result.Outcome.Should().Be(StepOutcome.Skipped);
        result.InputPath.Should().Be("/input.pdf");
        result.Message.Should().Be("File already processed");
    }
}

public class PipelineOptionsTests
{
    [Fact]
    public void Validate_WithEmptyInputPath_ReturnsError()
    {
        // Arrange
        var options = new PipelineOptions { InputPath = "" };

        // Act
        var result = options.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Input path"));
    }

    [Fact]
    public void Validate_WithValidOptions_ReturnsValid()
    {
        // Arrange
        var options = new PipelineOptions { InputPath = "/some/path" };

        // Act
        var result = options.Validate();

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithInvalidParallelism_ReturnsError()
    {
        // Arrange
        var options = new PipelineOptions { InputPath = "/path", MaxParallelism = 0 };

        // Act
        var result = options.Validate();

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxParallelism"));
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        // Arrange
        var options = new PipelineOptions { InputPath = "/path" };

        // Assert
        options.MaxParallelism.Should().Be(Environment.ProcessorCount);
        options.Overwrite.Should().BeFalse();
        options.ContinueOnError.Should().BeTrue();
        options.DryRun.Should().BeFalse();
        options.Recursive.Should().BeFalse();
        options.FilePattern.Should().Be("*");
    }
}

public class PipelineResultTests
{
    [Fact]
    public void IsSuccess_WithNoFailures_ReturnsTrue()
    {
        // Arrange
        var results = new List<StepResult>
        {
            StepResult.Success("/a.pdf", "/a_out.pdf", TimeSpan.Zero),
            StepResult.Success("/b.pdf", "/b_out.pdf", TimeSpan.Zero),
            StepResult.Skipped("/c.pdf", "Already exists")
        };

        var pipelineResult = new PipelineResult
        {
            Results = results,
            TotalDuration = TimeSpan.FromSeconds(5),
            WasCancelled = false
        };

        // Assert
        pipelineResult.IsSuccess.Should().BeTrue();
        pipelineResult.Succeeded.Should().Be(2);
        pipelineResult.Skipped.Should().Be(1);
        pipelineResult.Failed.Should().Be(0);
    }

    [Fact]
    public void IsSuccess_WithFailures_ReturnsFalse()
    {
        // Arrange
        var results = new List<StepResult>
        {
            StepResult.Success("/a.pdf", "/a_out.pdf", TimeSpan.Zero),
            StepResult.Failed("/b.pdf", "Error")
        };

        var pipelineResult = new PipelineResult
        {
            Results = results,
            TotalDuration = TimeSpan.FromSeconds(5),
            WasCancelled = false
        };

        // Assert
        pipelineResult.IsSuccess.Should().BeFalse();
        pipelineResult.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void TotalBytes_CalculatesCorrectly()
    {
        // Arrange
        var results = new List<StepResult>
        {
            StepResult.Success("/a.pdf", "/a_out.pdf", TimeSpan.Zero, 1000, 800),
            StepResult.Success("/b.pdf", "/b_out.pdf", TimeSpan.Zero, 2000, 1500)
        };

        var pipelineResult = new PipelineResult
        {
            Results = results,
            TotalDuration = TimeSpan.FromSeconds(5),
            WasCancelled = false
        };

        // Assert
        pipelineResult.TotalInputBytes.Should().Be(3000);
        pipelineResult.TotalOutputBytes.Should().Be(2300);
    }
}
