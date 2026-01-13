using BatchForge.Core.Pipeline;
namespace BatchForge.Core.Operations.Pdf;

/// <summary>
/// Factory for PDF operations.
/// Use this to get configured step instances.
/// </summary>
public static class PdfOperations
{
    public static PdfMergeStep Merge() => new();
    public static PdfSplitStep Split() => new();
    public static PdfCompressStep Compress() => new();
    public static PdfExtractTextStep ExtractText() => new();
    public static PdfExtractImagesStep ExtractImages() => new();

    /// <summary>
    /// All available PDF operations
    /// </summary>
    public static IReadOnlyList<IPipelineStep> All =>
    [
        new PdfMergeStep(),
        new PdfSplitStep(),
        new PdfCompressStep(),
        new PdfExtractTextStep(),
        new PdfExtractImagesStep()
    ];
}
