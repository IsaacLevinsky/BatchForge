namespace BatchForge.Core.Pipeline;

/// <summary>
/// Configuration for pipeline execution.
/// Designed for safe defaults that don't destroy data.
/// </summary>
public sealed class PipelineOptions
{
    /// <summary>
    /// Input directory or file pattern
    /// </summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Output directory. If null, outputs alongside inputs.
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// Maximum parallel operations. Default: Environment.ProcessorCount
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// If true, overwrite existing files. Default: false (safe)
    /// </summary>
    public bool Overwrite { get; init; } = false;

    /// <summary>
    /// If true, continue processing other files when one fails. Default: true
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// If true, only plan and report - don't execute. Default: false
    /// </summary>
    public bool DryRun { get; init; } = false;

    /// <summary>
    /// If true, recurse into subdirectories. Default: false
    /// </summary>
    public bool Recursive { get; init; } = false;

    /// <summary>
    /// File pattern filter (e.g., "*.pdf"). Default: all files
    /// </summary>
    public string FilePattern { get; init; } = "*";

    /// <summary>
    /// Step-specific parameters (e.g., page ranges for split)
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = new();

    /// <summary>
    /// Validate options before execution
    /// </summary>
    public ValidationResult Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(InputPath))
            errors.Add("Input path is required");

        if (MaxParallelism < 1)
            errors.Add("MaxParallelism must be at least 1");

        if (MaxParallelism > 64)
            errors.Add("MaxParallelism cannot exceed 64");

        return errors.Count > 0 
            ? ValidationResult.Invalid(errors.ToArray()) 
            : ValidationResult.Valid();
    }
}
