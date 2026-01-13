namespace BatchForge.Core.Pipeline;

/// <summary>
/// Aggregate result of a complete pipeline execution.
/// </summary>
public sealed class PipelineResult
{
    public required IReadOnlyList<StepResult> Results { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required bool WasCancelled { get; init; }

    public int Succeeded => Results.Count(r => r.Outcome == StepOutcome.Succeeded);
    public int Failed => Results.Count(r => r.Outcome == StepOutcome.Failed);
    public int Skipped => Results.Count(r => r.Outcome == StepOutcome.Skipped);
    public int Cancelled => Results.Count(r => r.Outcome == StepOutcome.Cancelled);
    public int Total => Results.Count;

    public long TotalInputBytes => Results.Sum(r => r.InputBytes);
    public long TotalOutputBytes => Results.Sum(r => r.OutputBytes);

    public bool IsSuccess => Failed == 0 && !WasCancelled;
    public bool HasFailures => Failed > 0;

    public IEnumerable<StepResult> FailedResults => 
        Results.Where(r => r.Outcome == StepOutcome.Failed);

    public void PrintSummary(TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine($"Pipeline Execution Summary");
        writer.WriteLine($"──────────────────────────");
        writer.WriteLine($"  Total:     {Total}");
        writer.WriteLine($"  Succeeded: {Succeeded}");
        writer.WriteLine($"  Failed:    {Failed}");
        writer.WriteLine($"  Skipped:   {Skipped}");
        if (WasCancelled)
            writer.WriteLine($"  Cancelled: {Cancelled}");
        writer.WriteLine($"  Duration:  {TotalDuration.TotalSeconds:F2}s");
        
        if (TotalInputBytes > 0)
        {
            writer.WriteLine($"  Input:     {FormatBytes(TotalInputBytes)}");
            writer.WriteLine($"  Output:    {FormatBytes(TotalOutputBytes)}");
            
            if (TotalOutputBytes > 0 && TotalOutputBytes < TotalInputBytes)
            {
                var savings = (1 - (double)TotalOutputBytes / TotalInputBytes) * 100;
                writer.WriteLine($"  Savings:   {savings:F1}%");
            }
        }

        if (HasFailures)
        {
            writer.WriteLine();
            writer.WriteLine($"Failed operations:");
            foreach (var failure in FailedResults.Take(10))
            {
                writer.WriteLine($"  ✗ {failure.InputPath}");
                writer.WriteLine($"    {failure.Message}");
            }
            if (Failed > 10)
                writer.WriteLine($"  ... and {Failed - 10} more failures");
        }
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
