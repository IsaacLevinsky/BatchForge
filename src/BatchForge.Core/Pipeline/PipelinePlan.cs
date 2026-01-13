namespace BatchForge.Core.Pipeline;

/// <summary>
/// Represents a planned pipeline execution.
/// Generated during dry-run to show what WILL happen.
/// This is a key differentiator - shows "safe operations mindset".
/// </summary>
public sealed class PipelinePlan
{
    public required IReadOnlyList<PlannedOperation> Operations { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required PipelineOptions Options { get; init; }

    public int TotalFiles => Operations.Count;
    public int WillProcess => Operations.Count(o => o.Action == PlannedAction.Process);
    public int WillSkip => Operations.Count(o => o.Action == PlannedAction.Skip);
    public int WillOverwrite => Operations.Count(o => o.WillOverwrite);
    public long EstimatedInputBytes => Operations.Sum(o => o.InputSizeBytes);

    public bool IsValid => Errors.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;

    public void PrintSummary(TextWriter writer)
    {
        writer.WriteLine($"Pipeline Plan Summary");
        writer.WriteLine($"─────────────────────");
        writer.WriteLine($"  Total files:    {TotalFiles}");
        writer.WriteLine($"  Will process:   {WillProcess}");
        writer.WriteLine($"  Will skip:      {WillSkip}");
        writer.WriteLine($"  Will overwrite: {WillOverwrite}");
        writer.WriteLine($"  Input size:     {FormatBytes(EstimatedInputBytes)}");
        writer.WriteLine();

        if (Warnings.Count > 0)
        {
            writer.WriteLine($"Warnings ({Warnings.Count}):");
            foreach (var warning in Warnings)
                writer.WriteLine($"  ⚠ {warning}");
            writer.WriteLine();
        }

        if (Errors.Count > 0)
        {
            writer.WriteLine($"Errors ({Errors.Count}):");
            foreach (var error in Errors)
                writer.WriteLine($"  ✗ {error}");
            writer.WriteLine();
        }
    }

    public void PrintDetails(TextWriter writer, int maxItems = 20)
    {
        var shown = 0;
        foreach (var op in Operations.Take(maxItems))
        {
            var actionSymbol = op.Action switch
            {
                PlannedAction.Process => "→",
                PlannedAction.Skip => "○",
                _ => "?"
            };

            var overwriteFlag = op.WillOverwrite ? " [overwrite]" : "";
            writer.WriteLine($"  {actionSymbol} {op.InputPath}");
            if (op.Action == PlannedAction.Process)
                writer.WriteLine($"    └─> {op.OutputPath}{overwriteFlag}");
            else if (op.SkipReason != null)
                writer.WriteLine($"    └─ skip: {op.SkipReason}");
            shown++;
        }

        if (Operations.Count > maxItems)
            writer.WriteLine($"  ... and {Operations.Count - maxItems} more files");
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}

public sealed record PlannedOperation
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required PlannedAction Action { get; init; }
    public required long InputSizeBytes { get; init; }
    public required bool WillOverwrite { get; init; }
    public string? SkipReason { get; init; }
    public required IReadOnlyList<string> StepIds { get; init; }
}

public enum PlannedAction
{
    Process,
    Skip
}
