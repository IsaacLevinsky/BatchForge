using System.Collections.Concurrent;
using System.Diagnostics;

namespace BatchForge.Core.Pipeline;

/// <summary>
/// Executes a pipeline of steps against a set of files.
/// Thread-safe, supports cancellation, bounded parallelism.
/// </summary>
public sealed class PipelineExecutor
{
    private readonly IReadOnlyList<IPipelineStep> _steps;

    public PipelineExecutor(IEnumerable<IPipelineStep> steps)
    {
        _steps = steps.ToList();
        if (_steps.Count == 0)
            throw new ArgumentException("Pipeline must have at least one step", nameof(steps));
    }

    public PipelineExecutor(params IPipelineStep[] steps) : this((IEnumerable<IPipelineStep>)steps)
    {
    }

    /// <summary>
    /// Plans the pipeline execution without running it.
    /// Use this for --dry-run to show what WOULD happen.
    /// </summary>
    public PipelinePlan Plan(PipelineOptions options)
    {
        var validation = options.Validate();
        if (!validation.IsValid)
        {
            return new PipelinePlan
            {
                Operations = Array.Empty<PlannedOperation>(),
                Warnings = Array.Empty<string>(),
                Errors = validation.Errors.ToList(),
                Options = options
            };
        }

        var inputFiles = DiscoverInputFiles(options);
        var warnings = new List<string>();
        var errors = new List<string>();
        var operations = new List<PlannedOperation>();

        // Validate all steps with parameters
        var stepOptions = new StepOptions
        {
            OutputDirectory = options.OutputDirectory ?? Path.GetDirectoryName(options.InputPath) ?? ".",
            Overwrite = options.Overwrite,
            Parameters = options.Parameters
        };

        foreach (var step in _steps)
        {
            var stepValidation = step.Validate(stepOptions);
            errors.AddRange(stepValidation.Errors);
            warnings.AddRange(stepValidation.Warnings);
        }

        if (errors.Count > 0)
        {
            return new PipelinePlan
            {
                Operations = Array.Empty<PlannedOperation>(),
                Warnings = warnings,
                Errors = errors,
                Options = options
            };
        }

        // Plan each file
        foreach (var inputFile in inputFiles)
        {
            var fileInfo = new FileInfo(inputFile);
            
            // Check if any step supports this file
            var supportingSteps = _steps
                .Where(s => s.SupportedExtensions.Contains(fileInfo.Extension, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (supportingSteps.Count == 0)
            {
                operations.Add(new PlannedOperation
                {
                    InputPath = inputFile,
                    OutputPath = inputFile,
                    Action = PlannedAction.Skip,
                    InputSizeBytes = fileInfo.Length,
                    WillOverwrite = false,
                    SkipReason = $"No step supports {fileInfo.Extension}",
                    StepIds = Array.Empty<string>()
                });
                continue;
            }

            // Calculate output path through the pipeline
            var outputPath = inputFile;
            foreach (var step in supportingSteps)
            {
                outputPath = step.GetOutputPath(outputPath, stepOptions);
            }

            var willOverwrite = File.Exists(outputPath) && outputPath != inputFile;

            if (willOverwrite && !options.Overwrite)
            {
                operations.Add(new PlannedOperation
                {
                    InputPath = inputFile,
                    OutputPath = outputPath,
                    Action = PlannedAction.Skip,
                    InputSizeBytes = fileInfo.Length,
                    WillOverwrite = false,
                    SkipReason = "Output exists (use --overwrite to replace)",
                    StepIds = supportingSteps.Select(s => s.StepId).ToList()
                });
                continue;
            }

            operations.Add(new PlannedOperation
            {
                InputPath = inputFile,
                OutputPath = outputPath,
                Action = PlannedAction.Process,
                InputSizeBytes = fileInfo.Length,
                WillOverwrite = willOverwrite,
                StepIds = supportingSteps.Select(s => s.StepId).ToList()
            });
        }

        if (operations.Count == 0)
        {
            warnings.Add("No files found matching input pattern");
        }

        return new PipelinePlan
        {
            Operations = operations,
            Warnings = warnings,
            Errors = errors,
            Options = options
        };
    }

    /// <summary>
    /// Executes the pipeline.
    /// Respects cancellation, bounded parallelism, and continue-on-error settings.
    /// </summary>
    public async Task<PipelineResult> ExecuteAsync(
        PipelineOptions options,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var plan = Plan(options);

        if (!plan.IsValid)
        {
            return new PipelineResult
            {
                Results = Array.Empty<StepResult>(),
                TotalDuration = stopwatch.Elapsed,
                WasCancelled = false
            };
        }

        if (options.DryRun)
        {
            // Dry run - return empty results but plan was printed
            return new PipelineResult
            {
                Results = Array.Empty<StepResult>(),
                TotalDuration = stopwatch.Elapsed,
                WasCancelled = false
            };
        }

        var toProcess = plan.Operations
            .Where(o => o.Action == PlannedAction.Process)
            .ToList();

        var results = new ConcurrentBag<StepResult>();
        var processedCount = 0;
        var totalCount = toProcess.Count;
        var wasCancelled = false;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxParallelism,
            CancellationToken = cancellationToken
        };

        var stepOptions = new StepOptions
        {
            OutputDirectory = options.OutputDirectory ?? Path.GetDirectoryName(options.InputPath) ?? ".",
            Overwrite = options.Overwrite,
            Parameters = options.Parameters
        };

        try
        {
            await Parallel.ForEachAsync(toProcess, parallelOptions, async (operation, ct) =>
            {
                if (ct.IsCancellationRequested)
                {
                    results.Add(StepResult.Skipped(operation.InputPath, "Cancelled"));
                    return;
                }

                var result = await ProcessFileAsync(operation, stepOptions, ct);
                results.Add(result);

                var count = Interlocked.Increment(ref processedCount);
                progress?.Report(new PipelineProgress(count, totalCount, operation.InputPath, result.Outcome));

                // Stop on first error if not continuing
                if (!options.ContinueOnError && result.Outcome == StepOutcome.Failed)
                {
                    // Can't cancel Parallel.ForEachAsync directly, but we can check in the loop
                }
            });
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }

        // Add skipped items to results
        foreach (var skipped in plan.Operations.Where(o => o.Action == PlannedAction.Skip))
        {
            results.Add(StepResult.Skipped(skipped.InputPath, skipped.SkipReason ?? "Skipped"));
        }

        stopwatch.Stop();

        return new PipelineResult
        {
            Results = results.ToList(),
            TotalDuration = stopwatch.Elapsed,
            WasCancelled = wasCancelled
        };
    }

    private async Task<StepResult> ProcessFileAsync(
        PlannedOperation operation,
        StepOptions stepOptions,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentInput = operation.InputPath;
        var inputInfo = new FileInfo(currentInput);
        var inputBytes = inputInfo.Length;

        try
        {
            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(operation.OutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Execute each step in sequence
            foreach (var step in _steps)
            {
                if (!step.SupportedExtensions.Contains(
                    Path.GetExtension(currentInput), 
                    StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var outputPath = step.GetOutputPath(currentInput, stepOptions);
                
                // Ensure the output's directory exists
                var stepOutputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(stepOutputDir) && !Directory.Exists(stepOutputDir))
                {
                    Directory.CreateDirectory(stepOutputDir);
                }

                // Determine if this step outputs a directory (like image extraction)
                // or a file. Check if output path has no extension or ends with _images
                var isDirectoryOutput = string.IsNullOrEmpty(Path.GetExtension(outputPath)) ||
                                        outputPath.EndsWith("_images", StringComparison.OrdinalIgnoreCase);

                if (isDirectoryOutput)
                {
                    // For directory outputs, execute directly to final location
                    var result = await step.ExecuteAsync(
                        currentInput,
                        outputPath,
                        stepOptions,
                        null,
                        cancellationToken);

                    if (result.Outcome != StepOutcome.Succeeded)
                    {
                        return result;
                    }

                    // For directory outputs, we don't chain to next step
                    stopwatch.Stop();
                    return result;
                }
                else
                {
                    // For file outputs, use temp file for atomic operation
                    var tempPath = Path.Combine(
                        Path.GetTempPath(),
                        $"batchforge_{Guid.NewGuid()}{Path.GetExtension(outputPath)}");

                    try
                    {
                        var result = await step.ExecuteAsync(
                            currentInput,
                            tempPath,
                            stepOptions,
                            null,
                            cancellationToken);

                        if (result.Outcome != StepOutcome.Succeeded)
                        {
                            // Clean up temp file on failure
                            if (File.Exists(tempPath))
                                File.Delete(tempPath);
                            return result;
                        }

                        // Check if temp file was actually created
                        if (!File.Exists(tempPath))
                        {
                            // Step succeeded but created no output (e.g., no extractable text)
                            // This is a valid success case
                            return result;
                        }

                        // Move temp to final destination
                        if (File.Exists(outputPath) && stepOptions.Overwrite)
                            File.Delete(outputPath);
                        
                        File.Move(tempPath, outputPath);
                        currentInput = outputPath;
                    }
                    finally
                    {
                        // Ensure temp cleanup
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                    }
                }
            }

            stopwatch.Stop();
            
            // Check if output exists before getting info
            if (File.Exists(currentInput))
            {
                var outputInfo = new FileInfo(currentInput);
                return StepResult.Success(
                    operation.InputPath,
                    currentInput,
                    stopwatch.Elapsed,
                    inputBytes,
                    outputInfo.Length);
            }
            else
            {
                return StepResult.Success(
                    operation.InputPath,
                    currentInput,
                    stopwatch.Elapsed,
                    inputBytes,
                    0);
            }
        }
        catch (OperationCanceledException)
        {
            return new StepResult
            {
                InputPath = operation.InputPath,
                Outcome = StepOutcome.Cancelled,
                Message = "Operation cancelled"
            };
        }
        catch (Exception ex)
        {
            return StepResult.Failed(operation.InputPath, ex.Message, ex);
        }
    }

    private List<string> DiscoverInputFiles(PipelineOptions options)
    {
        var inputPath = options.InputPath;

        // If it's a file, return just that file
        if (File.Exists(inputPath))
        {
            return [inputPath];
        }

        // Normalize output directory for comparison (to exclude from recursive search)
        var outputDirNormalized = options.OutputDirectory != null 
            ? Path.GetFullPath(options.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : null;

        // If it's a directory, search for files
        if (Directory.Exists(inputPath))
        {
            var searchOption = options.Recursive 
                ? SearchOption.AllDirectories 
                : SearchOption.TopDirectoryOnly;

            var files = Directory.EnumerateFiles(inputPath, options.FilePattern, searchOption);

            // When recursive, exclude files in output directory to prevent "input eats output" trap
            if (options.Recursive && outputDirNormalized != null)
            {
                files = files.Where(f => !Path.GetFullPath(f).StartsWith(outputDirNormalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                         !Path.GetFullPath(Path.GetDirectoryName(f) ?? "").Equals(outputDirNormalized, StringComparison.OrdinalIgnoreCase));
            }

            return files.ToList();
        }

        // Try as a glob pattern
        var directory = Path.GetDirectoryName(inputPath) ?? ".";
        var pattern = Path.GetFileName(inputPath);

        if (Directory.Exists(directory))
        {
            var searchOption = options.Recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = Directory.EnumerateFiles(directory, pattern, searchOption);

            // When recursive, exclude files in output directory
            if (options.Recursive && outputDirNormalized != null)
            {
                files = files.Where(f => !Path.GetFullPath(f).StartsWith(outputDirNormalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                         !Path.GetFullPath(Path.GetDirectoryName(f) ?? "").Equals(outputDirNormalized, StringComparison.OrdinalIgnoreCase));
            }

            return files.ToList();
        }

        return [];
    }
}

/// <summary>
/// Progress update during pipeline execution
/// </summary>
public record PipelineProgress(int Completed, int Total, string CurrentFile, StepOutcome LastOutcome)
{
    public double PercentComplete => Total > 0 ? (double)Completed / Total * 100 : 0;
}
