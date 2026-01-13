using System.Diagnostics;
using System.Text;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Content;
using PdfSharpCore.Pdf.Content.Objects;
using PdfSharpCore.Pdf.IO;
using BatchForge.Core.Pipeline;

namespace BatchForge.Core.Operations.Pdf;

/// <summary>
/// Extracts text content from PDF files.
/// Outputs to .txt file alongside or in output directory.
/// </summary>
public sealed class PdfExtractTextStep : IPipelineStep
{
    public string StepId => "pdf.text";
    public string Description => "Extract text content from PDF to text file";
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
        return Path.Combine(outputDir, $"{baseName}.txt");
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

            using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.ReadOnly);
            var pageCount = document.PageCount;
            var textBuilder = new StringBuilder();

            for (int i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = document.Pages[i];
                var pageText = ExtractTextFromPage(page);
                
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    if (textBuilder.Length > 0)
                        textBuilder.AppendLine();
                    
                    textBuilder.AppendLine($"--- Page {i + 1} ---");
                    textBuilder.AppendLine(pageText);
                }

                progress?.Report(new StepProgress(inputPath, (double)(i + 1) / pageCount * 100, $"Page {i + 1}/{pageCount}"));
            }

            var text = textBuilder.ToString();

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(text))
            {
                // No extractable text - this is a valid result, not a failure
                return new StepResult
                {
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    Outcome = StepOutcome.Succeeded,
                    Duration = stopwatch.Elapsed,
                    InputBytes = inputBytes,
                    OutputBytes = 0,
                    Message = "No extractable text found (may be scanned/image PDF)"
                };
            }

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

            File.WriteAllText(outputPath, text, Encoding.UTF8);

            var outputInfo = new FileInfo(outputPath);

            return StepResult.Success(
                inputPath,
                outputPath,
                stopwatch.Elapsed,
                inputBytes,
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
            return StepResult.Failed(inputPath, $"Text extraction failed: {ex.Message}", ex);
        }
    }

    private static string ExtractTextFromPage(PdfPage page)
    {
        try
        {
            var content = ContentReader.ReadContent(page);
            var text = new StringBuilder();
            ExtractText(content, text);
            return text.ToString().Trim();
        }
        catch
        {
            // Some PDFs have malformed content streams
            return string.Empty;
        }
    }

    private static void ExtractText(CObject obj, StringBuilder text)
    {
        if (obj is COperator op)
        {
            if (op.OpCode.Name == "Tj" || op.OpCode.Name == "TJ")
            {
                foreach (var operand in op.Operands)
                {
                    ExtractText(operand, text);
                }
            }
            else if (op.OpCode.Name == "'" || op.OpCode.Name == "\"")
            {
                text.AppendLine();
                foreach (var operand in op.Operands)
                {
                    ExtractText(operand, text);
                }
            }
        }
        else if (obj is CSequence seq)
        {
            foreach (var element in seq)
            {
                ExtractText(element, text);
            }
        }
        else if (obj is CString str)
        {
            text.Append(str.Value);
        }
        else if (obj is CArray arr)
        {
            foreach (var element in arr)
            {
                if (element is CString s)
                {
                    text.Append(s.Value);
                }
                else if (element is CInteger num && Math.Abs(num.Value) > 100)
                {
                    // Large negative numbers usually indicate word spacing
                    text.Append(' ');
                }
                else if (element is CReal real && Math.Abs(real.Value) > 100)
                {
                    // Large negative numbers usually indicate word spacing
                    text.Append(' ');
                }
            }
        }
    }
}
