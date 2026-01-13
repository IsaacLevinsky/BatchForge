using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using BatchForge.Cli.Commands;
using Spectre.Console;

namespace BatchForge.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("BatchForge - High-performance batch file processing")
        {
            Description = "Process files in bulk with pipeline-based operations.\n\n" +
                          "Examples:\n" +
                          "  batchforge pdf merge ./documents --output combined.pdf\n" +
                          "  batchforge pdf compress ./invoices --output ./compressed\n" +
                          "  batchforge pdf split report.pdf --pages 1-5,10-15\n" +
                          "  batchforge pdf text ./manuals --recursive\n\n" +
                          "Use --dry-run to preview operations without executing.\n\n" +
                          "© 2026 MCMLV1, LLC - https://mcmlv1.com\n" +
                          "Commercial support and GPU acceleration available."
        };

        // Add PDF commands
        rootCommand.AddCommand(PdfCommands.Create());

        // Add global options
        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .UseExceptionHandler((ex, context) =>
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                context.ExitCode = 1;
            })
            .Build();

        return await parser.InvokeAsync(args);
    }
}
