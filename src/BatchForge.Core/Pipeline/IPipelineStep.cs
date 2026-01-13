namespace BatchForge.Core.Pipeline;

/// <summary>
/// Contract for a single pipeline step.
/// Implementations must be thread-safe for parallel execution.
/// </summary>
public interface IPipelineStep
{
    /// <summary>
    /// Unique identifier for this step type (e.g., "pdf.merge", "image.resize")
    /// </summary>
    string StepId { get; }

    /// <summary>
    /// Human-readable description of what this step does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// File extensions this step can process (e.g., [".pdf", ".PDF"])
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Validates that the step can execute with given options.
    /// Called before execution to fail fast.
    /// </summary>
    ValidationResult Validate(StepOptions options);

    /// <summary>
    /// Executes the step on a single file.
    /// Must be thread-safe - may be called concurrently.
    /// Must respect cancellation token.
    /// Must not throw - return StepResult.Failed instead.
    /// </summary>
    Task<StepResult> ExecuteAsync(
        string inputPath,
        string outputPath,
        StepOptions options,
        IProgress<StepProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns what the output path would be for a given input.
    /// Used for dry-run planning.
    /// </summary>
    string GetOutputPath(string inputPath, StepOptions options);
}

/// <summary>
/// Options passed to a pipeline step
/// </summary>
public class StepOptions
{
    public string OutputDirectory { get; set; } = string.Empty;
    public bool Overwrite { get; set; } = false;
    public string? OutputExtension { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();

    public T GetParameter<T>(string key, T defaultValue)
    {
        if (Parameters.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return defaultValue;
    }
}

/// <summary>
/// Progress reporting for long-running steps
/// </summary>
public record StepProgress(string InputPath, double PercentComplete, string? Status = null);

/// <summary>
/// Result of step validation
/// </summary>
public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static ValidationResult Valid() => new() { IsValid = true };
    
    public static ValidationResult Invalid(params string[] errors) => 
        new() { IsValid = false, Errors = errors };

    public static ValidationResult WithWarnings(params string[] warnings) =>
        new() { IsValid = true, Warnings = warnings };
}
