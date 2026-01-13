using System.Diagnostics;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using BatchForge.Core.Pipeline;

namespace BatchForge.Core.Operations.Pdf;

/// <summary>
/// Splits a PDF into multiple files by page ranges.
/// Supports: single pages, ranges, and "burst" mode (one file per page).
/// </summary>
public sealed class PdfSplitStep : IPipelineStep
{
    public string StepId => "pdf.split";
    public string Description => "Split PDF into multiple files by page ranges";
    public IReadOnlyList<string> SupportedExtensions => [".pdf", ".PDF"];

    // Parameter keys
    public const string ParamPages = "pages";      // e.g., "1-5,10,15-20"
    public const string ParamBurst = "burst";      // true = one file per page

    public ValidationResult Validate(StepOptions options)
    {
        var pages = options.GetParameter<string?>(ParamPages, null);
        var burst = options.GetParameter<bool>(ParamBurst, false);

        // Treat empty string as null for pages
        var hasPages = !string.IsNullOrWhiteSpace(pages);

        if (!hasPages && !burst)
        {
            return ValidationResult.Invalid(
                "Specify --pages (e.g., '1-5,10') or --burst for one file per page");
        }

        // Only validate page ranges if pages were actually provided
        if (hasPages && !TryParsePageRanges(pages!, out _, out var error))
        {
            return ValidationResult.Invalid($"Invalid page range: {error}");
        }

        return ValidationResult.Valid();
    }

    public string GetOutputPath(string inputPath, StepOptions options)
    {
        var outputDir = string.IsNullOrEmpty(options.OutputDirectory)
            ? Path.GetDirectoryName(inputPath) ?? "."
            : options.OutputDirectory;

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        
        // For split, we return a pattern - actual files will be baseName_001.pdf, etc.
        return Path.Combine(outputDir, $"{baseName}_split.pdf");
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
            long totalOutputBytes = 0;

            var outputDir = Path.GetDirectoryName(outputPath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(inputPath);
            var burst = options.GetParameter<bool>(ParamBurst, false);
            var pagesParam = options.GetParameter<string?>(ParamPages, null);

            // Ensure output directory exists
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            using var sourceDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            var pageCount = sourceDocument.PageCount;

            List<PageRange> ranges;

            if (burst)
            {
                // One file per page
                ranges = Enumerable.Range(1, pageCount)
                    .Select(p => new PageRange(p, p))
                    .ToList();
            }
            else if (!string.IsNullOrWhiteSpace(pagesParam) && TryParsePageRanges(pagesParam, out var parsed, out _))
            {
                ranges = parsed;
            }
            else
            {
                return StepResult.Failed(inputPath, "No valid page ranges specified");
            }

            var outputFiles = new List<string>();
            var fileIndex = 1;

            foreach (var range in ranges)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startPage = Math.Max(1, range.Start);
                var endPage = Math.Min(pageCount, range.End);

                if (startPage > pageCount)
                    continue;

                using var outputDocument = new PdfDocument();

                for (int pageNum = startPage; pageNum <= endPage; pageNum++)
                {
                    // PDF pages are 0-indexed internally
                    outputDocument.AddPage(sourceDocument.Pages[pageNum - 1]);
                }

                var outputFileName = burst
                    ? $"{baseName}_page{startPage:D3}.pdf"
                    : $"{baseName}_part{fileIndex:D3}.pdf";

                var outputFilePath = Path.Combine(outputDir, outputFileName);

                if (File.Exists(outputFilePath) && !options.Overwrite)
                {
                    return StepResult.Failed(inputPath, $"Output exists: {outputFilePath}");
                }

                outputDocument.Save(outputFilePath);
                outputFiles.Add(outputFilePath);
                totalOutputBytes += new FileInfo(outputFilePath).Length;
                fileIndex++;

                progress?.Report(new StepProgress(inputPath, (double)fileIndex / ranges.Count * 100, $"Created {outputFileName}"));
            }

            stopwatch.Stop();

            return new StepResult
            {
                InputPath = inputPath,
                OutputPath = outputFiles.FirstOrDefault() ?? outputPath,
                Outcome = StepOutcome.Succeeded,
                Duration = stopwatch.Elapsed,
                InputBytes = inputBytes,
                OutputBytes = totalOutputBytes,
                Message = $"Split into {outputFiles.Count} files"
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
            return StepResult.Failed(inputPath, $"Split failed: {ex.Message}", ex);
        }
    }

    private static bool TryParsePageRanges(string input, out List<PageRange> ranges, out string? error)
    {
        ranges = new List<PageRange>();
        error = null;

        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2)
                {
                    error = $"Invalid range format: {part}";
                    return false;
                }

                if (!int.TryParse(rangeParts[0], out var start) || 
                    !int.TryParse(rangeParts[1], out var end))
                {
                    error = $"Invalid page numbers in range: {part}";
                    return false;
                }

                if (start > end)
                {
                    error = $"Start page greater than end page: {part}";
                    return false;
                }

                ranges.Add(new PageRange(start, end));
            }
            else
            {
                if (!int.TryParse(part, out var page))
                {
                    error = $"Invalid page number: {part}";
                    return false;
                }

                ranges.Add(new PageRange(page, page));
            }
        }

        return ranges.Count > 0;
    }

    private record PageRange(int Start, int End);
}
