namespace BatchForge.Core.Pipeline;

/// <summary>
/// Represents the outcome of a single pipeline step execution.
/// Immutable, designed for safe concurrent aggregation.
/// </summary>
public sealed record StepResult
{
    public required string InputPath { get; init; }
    public string? OutputPath { get; init; }
    public StepOutcome Outcome { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }
    public long InputBytes { get; init; }
    public long OutputBytes { get; init; }

    public static StepResult Success(string inputPath, string outputPath, TimeSpan duration, long inputBytes = 0, long outputBytes = 0) =>
        new()
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            Outcome = StepOutcome.Succeeded,
            Duration = duration,
            InputBytes = inputBytes,
            OutputBytes = outputBytes
        };

    public static StepResult Failed(string inputPath, string message, Exception? ex = null) =>
        new()
        {
            InputPath = inputPath,
            Outcome = StepOutcome.Failed,
            Message = message,
            Exception = ex
        };

    public static StepResult Skipped(string inputPath, string reason) =>
        new()
        {
            InputPath = inputPath,
            Outcome = StepOutcome.Skipped,
            Message = reason
        };
}

public enum StepOutcome
{
    Succeeded,
    Failed,
    Skipped,
    Cancelled
}
