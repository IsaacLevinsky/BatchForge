using System.Diagnostics;
using System.IO.Compression;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Advanced;
using PdfSharpCore.Pdf.IO;
using BatchForge.Core.Pipeline;

namespace BatchForge.Core.Operations.Pdf;

/// <summary>
/// Extracts embedded images from PDF files.
/// Saves images to output directory with page/index naming.
/// Supports JPEG (DCTDecode) and PNG reconstruction (FlateDecode).
/// </summary>
public sealed class PdfExtractImagesStep : IPipelineStep
{
    public string StepId => "pdf.images";
    public string Description => "Extract embedded images from PDF";
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
        
        // Output is a directory for images
        return Path.Combine(outputDir, $"{baseName}_images");
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
            var baseName = Path.GetFileNameWithoutExtension(inputPath);

            // outputPath is the images directory - create it
            var imagesDir = outputPath;
            if (!Directory.Exists(imagesDir))
            {
                Directory.CreateDirectory(imagesDir);
            }

            using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.ReadOnly);
            var pageCount = document.PageCount;
            var extractedCount = 0;
            long totalOutputBytes = 0;

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = document.Pages[pageIndex];
                var pageImages = ExtractImagesFromPage(page, baseName, imagesDir, pageIndex + 1, ref extractedCount, options.Overwrite);
                
                foreach (var imagePath in pageImages)
                {
                    if (File.Exists(imagePath))
                    {
                        totalOutputBytes += new FileInfo(imagePath).Length;
                    }
                }

                progress?.Report(new StepProgress(
                    inputPath,
                    (double)(pageIndex + 1) / pageCount * 100,
                    $"Page {pageIndex + 1}/{pageCount} - {extractedCount} images"));
            }

            stopwatch.Stop();

            if (extractedCount == 0)
            {
                // Clean up empty directory
                if (Directory.Exists(imagesDir) && !Directory.EnumerateFileSystemEntries(imagesDir).Any())
                {
                    Directory.Delete(imagesDir);
                }

                return new StepResult
                {
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    Outcome = StepOutcome.Succeeded,
                    Duration = stopwatch.Elapsed,
                    InputBytes = inputBytes,
                    OutputBytes = 0,
                    Message = "No extractable images found"
                };
            }

            return new StepResult
            {
                InputPath = inputPath,
                OutputPath = imagesDir,
                Outcome = StepOutcome.Succeeded,
                Duration = stopwatch.Elapsed,
                InputBytes = inputBytes,
                OutputBytes = totalOutputBytes,
                Message = $"Extracted {extractedCount} images"
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
            return StepResult.Failed(inputPath, $"Image extraction failed: {ex.Message}", ex);
        }
    }

    private static List<string> ExtractImagesFromPage(
        PdfPage page,
        string baseName,
        string outputDir,
        int pageNumber,
        ref int imageCount,
        bool overwrite)
    {
        var extractedPaths = new List<string>();

        try
        {
            var resources = page.Resources;
            if (resources == null) return extractedPaths;

            var xObjects = resources.Elements.GetDictionary("/XObject");
            if (xObjects == null) return extractedPaths;

            foreach (var item in xObjects.Elements.Keys)
            {
                var xObject = xObjects.Elements.GetDictionary(item);
                if (xObject == null) continue;

                var subtype = xObject.Elements.GetString("/Subtype");
                if (subtype != "/Image") continue;

                imageCount++;
                var imagePath = ExtractImage(xObject, baseName, outputDir, pageNumber, imageCount, overwrite);
                if (imagePath != null)
                {
                    extractedPaths.Add(imagePath);
                }
            }
        }
        catch
        {
            // Some pages have malformed resources
        }

        return extractedPaths;
    }

    private static string? ExtractImage(
        PdfDictionary imageObject,
        string baseName,
        string outputDir,
        int pageNumber,
        int imageIndex,
        bool overwrite)
    {
        try
        {
            var filter = imageObject.Elements.GetString("/Filter");
            var stream = imageObject.Stream?.Value;

            if (stream == null || stream.Length == 0)
                return null;

            // Get image dimensions for PNG reconstruction
            var width = imageObject.Elements.GetInteger("/Width");
            var height = imageObject.Elements.GetInteger("/Height");
            var bitsPerComponent = imageObject.Elements.GetInteger("/BitsPerComponent");
            var colorSpace = imageObject.Elements.GetString("/ColorSpace");

            string extension;
            byte[] imageData;

            if (filter == "/DCTDecode")
            {
                // JPEG - stream is already valid JPEG
                extension = ".jpg";
                imageData = stream;
            }
            else if (filter == "/FlateDecode")
            {
                // Compressed raw pixels - decompress and create PNG
                extension = ".png";
                
                try
                {
                    var decompressedData = DecompressFlate(stream);
                    imageData = CreatePng(decompressedData, width, height, bitsPerComponent, colorSpace);
                }
                catch
                {
                    // Fallback: save raw decompressed data
                    extension = ".raw";
                    imageData = DecompressFlate(stream);
                }
            }
            else if (filter == "/JPXDecode")
            {
                // JPEG 2000
                extension = ".jp2";
                imageData = stream;
            }
            else if (filter == "/CCITTFaxDecode")
            {
                // CCITT fax encoding (typically TIFF-like)
                extension = ".tiff";
                imageData = stream;
            }
            else
            {
                // Unknown format, save raw
                extension = ".raw";
                imageData = stream;
            }

            var fileName = $"{baseName}_p{pageNumber:D3}_img{imageIndex:D3}{extension}";
            var outputPath = Path.Combine(outputDir, fileName);

            if (File.Exists(outputPath) && !overwrite)
                return null;

            File.WriteAllBytes(outputPath, imageData);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decompress FlateDecode (zlib/deflate) stream
    /// </summary>
    private static byte[] DecompressFlate(byte[] compressedData)
    {
        // Skip zlib header (2 bytes) if present
        int offset = 0;
        if (compressedData.Length > 2)
        {
            // Check for zlib header (0x78 0x9C, 0x78 0xDA, 0x78 0x01, etc.)
            if (compressedData[0] == 0x78)
            {
                offset = 2;
            }
        }

        using var inputStream = new MemoryStream(compressedData, offset, compressedData.Length - offset);
        using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        
        deflateStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    /// <summary>
    /// Create a valid PNG file from raw pixel data
    /// </summary>
    private static byte[] CreatePng(byte[] pixelData, int width, int height, int bitsPerComponent, string? colorSpace)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        // Determine color type and channels
        byte colorType;
        int channels;

        if (colorSpace == "/DeviceGray" || colorSpace == "/CalGray")
        {
            colorType = 0; // Grayscale
            channels = 1;
        }
        else if (colorSpace == "/DeviceRGB" || colorSpace == "/CalRGB")
        {
            colorType = 2; // RGB
            channels = 3;
        }
        else if (colorSpace == "/DeviceCMYK")
        {
            // Convert CMYK to RGB
            colorType = 2; // RGB
            channels = 3;
            pixelData = ConvertCmykToRgb(pixelData, width, height);
        }
        else
        {
            // Default to RGB
            colorType = 2;
            channels = 3;
        }

        var bitDepth = (byte)(bitsPerComponent > 0 ? bitsPerComponent : 8);

        // PNG signature
        writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR chunk
        WriteChunk(writer, "IHDR", WriteIhdr(width, height, bitDepth, colorType));

        // IDAT chunk (image data)
        var filteredData = AddPngFiltering(pixelData, width, height, channels, bitDepth);
        var compressedData = CompressZlib(filteredData);
        WriteChunk(writer, "IDAT", compressedData);

        // IEND chunk
        WriteChunk(writer, "IEND", []);

        return output.ToArray();
    }

    private static byte[] WriteIhdr(int width, int height, byte bitDepth, byte colorType)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Width and height (big-endian)
        bw.Write(ToBigEndian(width));
        bw.Write(ToBigEndian(height));
        bw.Write(bitDepth);      // Bit depth
        bw.Write(colorType);     // Color type
        bw.Write((byte)0);       // Compression method (deflate)
        bw.Write((byte)0);       // Filter method (adaptive)
        bw.Write((byte)0);       // Interlace method (none)

        return ms.ToArray();
    }

    private static void WriteChunk(BinaryWriter writer, string type, byte[] data)
    {
        // Length (big-endian)
        writer.Write(ToBigEndian(data.Length));

        // Type
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        writer.Write(typeBytes);

        // Data
        writer.Write(data);

        // CRC32
        var crcData = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcData, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcData, typeBytes.Length, data.Length);
        writer.Write(ToBigEndian((int)CalculateCrc32(crcData)));
    }

    private static byte[] AddPngFiltering(byte[] pixelData, int width, int height, int channels, int bitDepth)
    {
        var bytesPerPixel = channels * (bitDepth / 8);
        var rowBytes = width * bytesPerPixel;
        var filteredData = new byte[height * (rowBytes + 1)];

        for (int y = 0; y < height; y++)
        {
            var srcOffset = y * rowBytes;
            var dstOffset = y * (rowBytes + 1);

            // Filter type 0 (None) - simplest approach
            filteredData[dstOffset] = 0;

            // Copy row data
            if (srcOffset + rowBytes <= pixelData.Length)
            {
                Buffer.BlockCopy(pixelData, srcOffset, filteredData, dstOffset + 1, rowBytes);
            }
        }

        return filteredData;
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var output = new MemoryStream();
        
        // Zlib header
        output.WriteByte(0x78);
        output.WriteByte(0x9C);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        // Adler-32 checksum
        var adler = CalculateAdler32(data);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);

        return output.ToArray();
    }

    private static byte[] ConvertCmykToRgb(byte[] cmykData, int width, int height)
    {
        var rgbData = new byte[width * height * 3];
        var pixels = width * height;

        for (int i = 0; i < pixels; i++)
        {
            var cmykOffset = i * 4;
            var rgbOffset = i * 3;

            if (cmykOffset + 3 >= cmykData.Length) break;

            var c = cmykData[cmykOffset] / 255.0;
            var m = cmykData[cmykOffset + 1] / 255.0;
            var y = cmykData[cmykOffset + 2] / 255.0;
            var k = cmykData[cmykOffset + 3] / 255.0;

            rgbData[rgbOffset] = (byte)(255 * (1 - c) * (1 - k));
            rgbData[rgbOffset + 1] = (byte)(255 * (1 - m) * (1 - k));
            rgbData[rgbOffset + 2] = (byte)(255 * (1 - y) * (1 - k));
        }

        return rgbData;
    }

    private static byte[] ToBigEndian(int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private static uint CalculateCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320 * (crc & 1));
            }
        }
        return ~crc;
    }

    private static uint CalculateAdler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}
