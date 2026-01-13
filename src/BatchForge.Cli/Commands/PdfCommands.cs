using System.CommandLine;
using BatchForge.Core.Operations.Pdf;
using BatchForge.Core.Pipeline;
using Spectre.Console;

namespace BatchForge.Cli.Commands;

public static class PdfCommands
{
    public static Command Create()
    {
        var pdfCommand = new Command("pdf", "PDF operations - merge, split, compress, extract");

        pdfCommand.AddCommand(CreateMergeCommand());
        pdfCommand.AddCommand(CreateSplitCommand());
        pdfCommand.AddCommand(CreateCompressCommand());
        pdfCommand.AddCommand(CreateTextCommand());
        pdfCommand.AddCommand(CreateImagesCommand());

        return pdfCommand;
    }

    private static Command CreateMergeCommand()
    {
        var inputArg = new Argument<string>("input", "Input directory or glob pattern");
        var outputOption = new Option<string?>("--output", "Output file path");
        outputOption.AddAlias("-o");
        var dryRunOption = CreateDryRunOption();

        var command = new Command("merge", "Merge multiple PDFs into one")
        {
            inputArg,
            outputOption,
            dryRunOption
        };

        command.SetHandler(async (string input, string? output, bool dryRun) =>
        {
            var inputPath = Path.GetFullPath(input);
            var outputPath = output ?? Path.Combine(
                Path.GetDirectoryName(inputPath) ?? ".",
                "merged.pdf");

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // For merge, we need to collect all PDFs first
            var files = DiscoverFiles(inputPath, "*.pdf");
            
            if (files.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No PDF files found[/]");
                return;
            }

            AnsiConsole.MarkupLine($"Found [green]{files.Count}[/] PDF files to merge");

            if (dryRun)
            {
                AnsiConsole.MarkupLine("[blue]Dry run - would merge:[/]");
                foreach (var file in files.Take(10))
                    AnsiConsole.MarkupLine($"  • {file}");
                if (files.Count > 10)
                    AnsiConsole.MarkupLine($"  ... and {files.Count - 10} more");
                AnsiConsole.MarkupLine($"[blue]Output:[/] {outputPath}");
                return;
            }

            var result = await AnsiConsole.Status()
                .StartAsync("Merging PDFs...", async ctx =>
                {
                    return await Task.Run(() => PdfMergeStep.MergeFiles(files, outputPath, true));
                });

            PrintResult(result);

        }, inputArg, outputOption, dryRunOption);

        return command;
    }

    private static Command CreateSplitCommand()
    {
        var inputArg = new Argument<string>("input", "Input PDF file or directory");
        var outputOption = new Option<string?>("--output", "Output directory");
        outputOption.AddAlias("-o");
        var pagesOption = new Option<string?>("--pages", "Page ranges (e.g., '1-5,10,15-20')");
        pagesOption.AddAlias("-p");
        var burstOption = new Option<bool>("--burst", "Split into one file per page");
        var dryRunOption = CreateDryRunOption();
        var parallelOption = CreateParallelOption();

        var command = new Command("split", "Split PDF by page ranges")
        {
            inputArg,
            outputOption,
            pagesOption,
            burstOption,
            dryRunOption,
            parallelOption
        };

        command.SetHandler(async (string input, string? output, string? pages, bool burst, bool dryRun, int parallel) =>
        {
            var parameters = new Dictionary<string, object>
            {
                [PdfSplitStep.ParamPages] = pages ?? "",
                [PdfSplitStep.ParamBurst] = burst
            };

            await ExecutePipelineAsync(
                input,
                output,
                new PdfSplitStep(),
                parameters,
                dryRun,
                parallel);

        }, inputArg, outputOption, pagesOption, burstOption, dryRunOption, parallelOption);

        return command;
    }

    private static Command CreateCompressCommand()
    {
        var inputArg = new Argument<string>("input", "Input PDF file or directory");
        var outputOption = new Option<string?>("--output", "Output directory");
        outputOption.AddAlias("-o");
        var overwriteOption = CreateOverwriteOption();
        var recursiveOption = CreateRecursiveOption();
        var dryRunOption = CreateDryRunOption();
        var parallelOption = CreateParallelOption();

        var command = new Command("compress", "Compress/optimize PDF files")
        {
            inputArg,
            outputOption,
            overwriteOption,
            recursiveOption,
            dryRunOption,
            parallelOption
        };

        command.SetHandler(async (string input, string? output, bool overwrite, bool recursive, bool dryRun, int parallel) =>
        {
            await ExecutePipelineAsync(
                input,
                output,
                new PdfCompressStep(),
                new Dictionary<string, object>(),
                dryRun,
                parallel,
                overwrite,
                recursive);

        }, inputArg, outputOption, overwriteOption, recursiveOption, dryRunOption, parallelOption);

        return command;
    }

    private static Command CreateTextCommand()
    {
        var inputArg = new Argument<string>("input", "Input PDF file or directory");
        var outputOption = new Option<string?>("--output", "Output directory");
        outputOption.AddAlias("-o");
        var recursiveOption = CreateRecursiveOption();
        var dryRunOption = CreateDryRunOption();
        var parallelOption = CreateParallelOption();

        var command = new Command("text", "Extract text from PDF files")
        {
            inputArg,
            outputOption,
            recursiveOption,
            dryRunOption,
            parallelOption
        };

        command.SetHandler(async (string input, string? output, bool recursive, bool dryRun, int parallel) =>
        {
            await ExecutePipelineAsync(
                input,
                output,
                new PdfExtractTextStep(),
                new Dictionary<string, object>(),
                dryRun,
                parallel,
                overwrite: true,
                recursive: recursive);

        }, inputArg, outputOption, recursiveOption, dryRunOption, parallelOption);

        return command;
    }

    private static Command CreateImagesCommand()
    {
        var inputArg = new Argument<string>("input", "Input PDF file or directory");
        var outputOption = new Option<string?>("--output", "Output directory");
        outputOption.AddAlias("-o");
        var recursiveOption = CreateRecursiveOption();
        var dryRunOption = CreateDryRunOption();
        var parallelOption = CreateParallelOption();

        var command = new Command("images", "Extract images from PDF files")
        {
            inputArg,
            outputOption,
            recursiveOption,
            dryRunOption,
            parallelOption
        };

        command.SetHandler(async (string input, string? output, bool recursive, bool dryRun, int parallel) =>
        {
            await ExecutePipelineAsync(
                input,
                output,
                new PdfExtractImagesStep(),
                new Dictionary<string, object>(),
                dryRun,
                parallel,
                overwrite: true,
                recursive: recursive);

        }, inputArg, outputOption, recursiveOption, dryRunOption, parallelOption);

        return command;
    }

    // Common options
    private static Option<bool> CreateDryRunOption()
    {
        var option = new Option<bool>("--dry-run", "Preview operations without executing");
        option.AddAlias("-n");
        return option;
    }

    private static Option<bool> CreateOverwriteOption()
    {
        var option = new Option<bool>("--overwrite", "Overwrite existing files");
        option.AddAlias("-f");
        return option;
    }

    private static Option<bool> CreateRecursiveOption()
    {
        var option = new Option<bool>("--recursive", "Process subdirectories");
        option.AddAlias("-r");
        return option;
    }

    private static Option<int> CreateParallelOption()
    {
        var option = new Option<int>("--parallel", () => Environment.ProcessorCount, "Max parallel operations");
        option.AddAlias("-j");
        return option;
    }

    // Execution helpers
    private static async Task ExecutePipelineAsync(
        string input,
        string? output,
        IPipelineStep step,
        Dictionary<string, object> parameters,
        bool dryRun,
        int parallel,
        bool overwrite = false,
        bool recursive = false)
    {
        var inputPath = Path.GetFullPath(input);

        var options = new PipelineOptions
        {
            InputPath = inputPath,
            OutputDirectory = output != null ? Path.GetFullPath(output) : null,
            FilePattern = "*.pdf",
            DryRun = dryRun,
            MaxParallelism = parallel,
            Overwrite = overwrite,
            Recursive = recursive,
            Parameters = parameters
        };

        var executor = new PipelineExecutor(step);

        if (dryRun)
        {
            var plan = executor.Plan(options);
            AnsiConsole.WriteLine();
            plan.PrintSummary(Console.Out);
            plan.PrintDetails(Console.Out);
            return;
        }

        // Execute with progress
        var result = await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Processing", maxValue: 100);

                var progress = new Progress<PipelineProgress>(p =>
                {
                    task.Value = p.PercentComplete;
                    task.Description = Path.GetFileName(p.CurrentFile);
                });

                return await executor.ExecuteAsync(options, progress);
            });

        result.PrintSummary(Console.Out);
    }

    private static List<string> DiscoverFiles(string path, string pattern)
    {
        if (File.Exists(path))
            return [path];

        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, pattern).OrderBy(f => f).ToList();

        var dir = Path.GetDirectoryName(path) ?? ".";
        var filePattern = Path.GetFileName(path);
        
        if (Directory.Exists(dir))
            return Directory.EnumerateFiles(dir, filePattern).OrderBy(f => f).ToList();

        return [];
    }

    private static void PrintResult(StepResult result)
    {
        AnsiConsole.WriteLine();
        
        if (result.Outcome == StepOutcome.Succeeded)
        {
            AnsiConsole.MarkupLine($"[green]✓ Success[/]");
            AnsiConsole.MarkupLine($"  Output: {result.OutputPath}");
            if (result.Message != null)
                AnsiConsole.MarkupLine($"  {result.Message}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ Failed[/]");
            AnsiConsole.MarkupLine($"  {result.Message}");
        }
    }
}
