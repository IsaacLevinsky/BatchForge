using System.Diagnostics;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using BatchForge.Core.Pipeline;

namespace BatchForge.Core.Operations.Pdf;

/// <summary>
/// Compresses PDF files by rewriting with optimized settings.
/// Note: PdfSharpCore has limited compression capabilities compared to commercial tools.
/// For advanced compression, consider GPU-accelerated commercial module.
/// </summary>
public sealed class PdfCompressStep : IPipelineStep
{
    public string StepId => "pdf.compress";
    public string Description => "Compress/optimize PDF file size";
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

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);

        // If output dir is same as input dir, add suffix to avoid overwrite confusion
        if (string.IsNullOrEmpty(options.OutputDirectory))
        {
            return Path.Combine(outputDir, $"{baseName}_compressed{ext}");
        }

        return Path.Combine(outputDir, $"{baseName}{ext}");
    }

    public async Task<StepResult> ExecuteAsync(
        string inputPath,
        string outputPath,
        StepOptions options,
        IProgress<StepProgress>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() => ExecuteSync(inputPath, outputPath, options, progress, cancellationToken), cancellationToken);
    }

    private StepResult ExecuteSync(
        string inputPath,
        string outputPath,
        StepOptions options,
        IProgress<StepProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var inputInfo = new FileInfo(inputPath);
            var inputBytes = inputInfo.Length;

            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new StepProgress(inputPath, 10, "Opening PDF..."));

            // Open and rewrite with compression
            using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

            progress?.Report(new StepProgress(inputPath, 30, "Optimizing..."));

            // Apply compression options
            document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;
            document.Options.NoCompression = false;
            document.Options.CompressContentStreams = true;

            // Clear verbose metadata that bloats file (only writable properties)
            try
            {
                if (document.Info != null)
                {
                    document.Info.Creator = "";
                }
            }
            catch
            {
                // Some PDFs have locked metadata
            }

            progress?.Report(new StepProgress(inputPath, 70, "Writing compressed PDF..."));

            cancellationToken.ThrowIfCancellationRequested();

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            if (File.Exists(outputPath) && !options.Overwrite)
            {
                return StepResult.Failed(inputPath, $"Output exists: {outputPath}");
            }

            document.Save(outputPath);

            progress?.Report(new StepProgress(inputPath, 100, "Done"));

            stopwatch.Stop();

            var outputInfo = new FileInfo(outputPath);
            var outputBytes = outputInfo.Length;

            // Calculate savings
            var savings = inputBytes > 0 ? (1.0 - (double)outputBytes / inputBytes) * 100 : 0;
            var message = savings > 0
                ? $"Compressed {savings:F1}% ({FormatBytes(inputBytes)} → {FormatBytes(outputBytes)})"
                : $"No size reduction achieved";

            return new StepResult
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                Outcome = StepOutcome.Succeeded,
                Duration = stopwatch.Elapsed,
                InputBytes = inputBytes,
                OutputBytes = outputBytes,
                Message = message
            };
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
            return StepResult.Failed(inputPath, $"Compression failed: {ex.Message}", ex);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.#}{sizes[order]}";
    }
}
