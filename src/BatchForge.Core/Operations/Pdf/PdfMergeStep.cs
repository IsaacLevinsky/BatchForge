using System.Diagnostics;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using BatchForge.Core.Pipeline;

namespace BatchForge.Core.Operations.Pdf;

/// <summary>
/// Merges multiple PDF files into a single PDF.
/// Thread-safe, supports cancellation.
/// </summary>
public sealed class PdfMergeStep : IPipelineStep
{
    public string StepId => "pdf.merge";
    public string Description => "Merge multiple PDF files into a single document";
    public IReadOnlyList<string> SupportedExtensions => [".pdf", ".PDF"];

    public ValidationResult Validate(StepOptions options)
    {
        return ValidationResult.Valid();
    }

    public string GetOutputPath(string inputPath, StepOptions options)
    {
        var outputDir = string.IsNullOrEmpty(options.OutputDirectory) 
            ? Path.GetDirectoryName(inputPath) ?? "."
            : options.OutputDirectory;
        
        var fileName = Path.GetFileName(inputPath);
        return Path.Combine(outputDir, fileName);
    }

    public Task<StepResult> ExecuteAsync(
        string inputPath,
        string outputPath,
        StepOptions options,
        IProgress<StepProgress>? progress,
        CancellationToken cancellationToken)
    {
        // For single-file operations, merge is a pass-through
        // Real merge happens at directory level via PdfMergeOperation
        return Task.FromResult(ExecuteSync(inputPath, outputPath, options, cancellationToken));
    }

    private StepResult ExecuteSync(
        string inputPath,
        string outputPath,
        StepOptions options,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputInfo = new FileInfo(inputPath);
            
            // Simple copy for single file (merge multiple requires MergeFiles method)
            File.Copy(inputPath, outputPath, options.Overwrite);

            stopwatch.Stop();
            var outputInfo = new FileInfo(outputPath);

            return StepResult.Success(
                inputPath, 
                outputPath, 
                stopwatch.Elapsed,
                inputInfo.Length,
                outputInfo.Length);
        }
        catch (OperationCanceledException)
        {
            return new StepResult
            {
                InputPath = inputPath,
                Outcome = StepOutcome.Cancelled,
                Message = "Operation cancelled"
            };
        }
        catch (Exception ex)
        {
            return StepResult.Failed(inputPath, ex.Message, ex);
        }
    }

    /// <summary>
    /// Merges multiple PDF files into one.
    /// Call this directly for directory-level merge operations.
    /// </summary>
    public static StepResult MergeFiles(
        IEnumerable<string> inputPaths,
        string outputPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var inputs = inputPaths.ToList();
        
        if (inputs.Count == 0)
            return StepResult.Failed("(no input)", "No input files provided");

        long totalInputBytes = 0;

        try
        {
            using var outputDocument = new PdfDocument();

            foreach (var inputPath in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var inputInfo = new FileInfo(inputPath);
                totalInputBytes += inputInfo.Length;

                using var inputDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                
                for (int i = 0; i < inputDocument.PageCount; i++)
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
            }

            if (File.Exists(outputPath) && !overwrite)
                return StepResult.Failed(inputs[0], $"Output file exists: {outputPath}");

            outputDocument.Save(outputPath);
            stopwatch.Stop();

            var outputInfo = new FileInfo(outputPath);

            return StepResult.Success(
                inputs[0],
                outputPath,
                stopwatch.Elapsed,
                totalInputBytes,
                outputInfo.Length);
        }
        catch (OperationCanceledException)
        {
            return new StepResult
            {
                InputPath = inputs[0],
                Outcome = StepOutcome.Cancelled,
                Message = "Operation cancelled"
            };
        }
        catch (Exception ex)
        {
            return StepResult.Failed(inputs[0], $"Merge failed: {ex.Message}", ex);
        }
    }
}
