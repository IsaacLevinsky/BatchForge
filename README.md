<p align="center">
  <img src="docs/logo.png" alt="BatchForge Logo" width="120"/>
</p>

<h1 align="center">BatchForge</h1>

<p align="center">
  <strong>High-Performance Batch File Processing</strong><br>
  CLI tool for PDF operations with parallel execution and safe operations mindset
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Build-Passing-brightgreen.svg" alt="Build"/>
  <img src="https://img.shields.io/badge/.NET-8.0-blue.svg" alt=".NET"/>
  <img src="https://img.shields.io/badge/Platform-Linux%20%7C%20Windows%20%7C%20macOS-lightgrey.svg" alt="Platform"/>
  <img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License"/>
</p>

---

## Why BatchForge?

Most batch processing tools are either:
- **GUI-only** — Can't be automated or scripted
- **Single-threaded** — Painfully slow on large datasets
- **Unsafe** — Overwrite files without warning, corrupt on failure

**BatchForge is different:**

```bash
# Preview what will happen (safe operations mindset)
batchforge pdf compress ./invoices --dry-run

# Then execute with confidence
batchforge pdf compress ./invoices --output ./compressed --parallel 8
```

### Why BatchForge vs. Random PDF Tools?

BatchForge is built for **professionals who process documents at scale**. Unlike browser-based tools or simple GUI apps, BatchForge offers:

- **Offline operation** — No uploads, no privacy concerns, no file size limits
- **Scriptable** — Integrate into CI/CD, cron jobs, or automation workflows
- **Predictable** — Dry-run shows exactly what will happen before execution
- **Safe** — Atomic operations, no overwrites without explicit flag, clean cancellation

### Key Features

| Feature | Description |
|---------|-------------|
| **Pipeline Architecture** | Internal planning → execution engine with bounded parallelism |
| **Dry-Run Mode** | Preview every operation before execution |
| **Safe by Default** | Won't overwrite without `--overwrite`, atomic file operations |
| **Cancellation Support** | Ctrl+C stops cleanly without corrupting outputs |
| **Cross-Platform** | Runs on Linux, Windows, and macOS |
| **Progress Reporting** | Real-time progress with ETA |

---

## Who This Tool Is For

BatchForge is designed for:

- Developers and power users comfortable with CLI tools
- Technical professionals processing large document sets
- Automation and scripting workflows
- Offline, air-gapped, or compliance-sensitive environments

BatchForge is **not** intended to replace:
- GUI PDF editors
- Document viewers
- Interactive page-level editing tools

If you need a visual editor, this tool is intentionally not that.

---

## Installation

### Download Binary (Recommended)

Download the latest release for your platform:

**Linux (x64)**
```bash
curl -L https://github.com/YOUR_USERNAME/BatchForge/releases/latest/download/batchforge-linux-x64.tar.gz -o batchforge.tar.gz
tar -xzf batchforge.tar.gz
chmod +x batchforge
sudo mv batchforge /usr/local/bin/

# Verify installation
batchforge --version
```

**Windows (x64)**
```powershell
# Download from GitHub Releases
Invoke-WebRequest -Uri "https://github.com/YOUR_USERNAME/BatchForge/releases/latest/download/batchforge-win-x64.zip" -OutFile batchforge.zip
Expand-Archive batchforge.zip -DestinationPath .
Move-Item batchforge.exe C:\Tools\  # Or add to PATH

# Verify installation
batchforge --version
```

**macOS (Apple Silicon)**
```bash
curl -L https://github.com/YOUR_USERNAME/BatchForge/releases/latest/download/batchforge-osx-arm64.tar.gz -o batchforge.tar.gz
tar -xzf batchforge.tar.gz
chmod +x batchforge
sudo mv batchforge /usr/local/bin/

# Verify installation
batchforge --version
```

**macOS (Intel)**
```bash
curl -L https://github.com/YOUR_USERNAME/BatchForge/releases/latest/download/batchforge-osx-x64.tar.gz -o batchforge.tar.gz
tar -xzf batchforge.tar.gz
chmod +x batchforge
sudo mv batchforge /usr/local/bin/
```

### Verify Download (Optional)

```bash
# Download checksums
curl -L https://github.com/YOUR_USERNAME/BatchForge/releases/latest/download/SHA256SUMS.txt -o SHA256SUMS.txt

# Verify (Linux/macOS)
sha256sum -c SHA256SUMS.txt

# Verify (Windows PowerShell)
Get-FileHash batchforge-win-x64.zip -Algorithm SHA256
```

### Build from Source

```bash
git clone https://github.com/YOUR_USERNAME/BatchForge.git
cd BatchForge
dotnet build -c Release
dotnet run --project src/BatchForge.Cli -- --help
```

### .NET Tool (Planned)

A global .NET tool distribution is planned once the CLI interface is finalized.

---

## PDF Operations

### Merge Multiple PDFs
```bash
# Merge all PDFs in a directory
batchforge pdf merge ./documents --output combined.pdf

# Preview first
batchforge pdf merge ./documents --dry-run
```

### Split PDF by Pages
```bash
# Extract specific pages
batchforge pdf split report.pdf --pages 1-5,10,15-20

# Split into individual pages (burst mode)
batchforge pdf split report.pdf --burst --output ./pages
```

### Compress PDFs
```bash
# Compress all PDFs in a directory
batchforge pdf compress ./scans --output ./compressed

# Process subdirectories
batchforge pdf compress ./archive --recursive --parallel 8

# Overwrite originals (use with caution)
batchforge pdf compress ./invoices --overwrite
```

### Extract Text
```bash
# Extract text from PDF to .txt files
batchforge pdf text ./documents --output ./extracted

# Process recursively
batchforge pdf text ./archive --recursive
```

### Extract Images
```bash
# Extract embedded images from PDFs
batchforge pdf images ./brochures --output ./images
```

---

## Limitations

- **OCR is not included** — Scanned PDFs without embedded text will not yield text output
- **Compression varies** — Some PDFs may not compress significantly depending on content
- **Image extraction depends on format** — Embedded images in unsupported formats are saved as raw data
- **Single operation per invocation** — User-defined multi-step pipelines are planned for a future release

---

## Architecture

BatchForge uses a pipeline architecture that separates concerns:

```
┌─────────────────────────────────────────────────────────────────┐
│                           CLI Layer                              │
│  System.CommandLine + Spectre.Console                           │
└─────────────────────────────┬───────────────────────────────────┘
                              │
┌─────────────────────────────▼───────────────────────────────────┐
│                      Pipeline Engine                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │   Planner   │  │  Executor   │  │   Progress Reporter     │  │
│  │  (dry-run)  │  │ (parallel)  │  │   (real-time updates)   │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
└─────────────────────────────┬───────────────────────────────────┘
                              │
┌─────────────────────────────▼───────────────────────────────────┐
│                     Operations (Plugins)                         │
│  ┌────────┐  ┌────────┐  ┌──────────┐  ┌────────┐  ┌────────┐  │
│  │ Merge  │  │ Split  │  │ Compress │  │  Text  │  │ Images │  │
│  └────────┘  └────────┘  └──────────┘  └────────┘  └────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Design Principles

| Principle | Implementation |
|-----------|----------------|
| **Safe by default** | No overwrites without explicit flag |
| **Fail fast** | Validate all inputs before processing |
| **Atomic operations** | Write to temp, then move |
| **Bounded parallelism** | Configurable with `--parallel` |
| **Cancellation support** | Clean shutdown on Ctrl+C |
| **Progress visibility** | Real-time updates, ETA |

---

## Global Options

| Option | Short | Description |
|--------|-------|-------------|
| `--dry-run` | `-n` | Preview operations without executing |
| `--output` | `-o` | Output directory or file |
| `--overwrite` | `-f` | Overwrite existing files |
| `--recursive` | `-r` | Process subdirectories |
| `--parallel` | `-j` | Max parallel operations (default: CPU count) |
| `--help` | `-h` | Show help |

---

## Examples

### Batch Compress with Progress
```bash
$ batchforge pdf compress ./scans --output ./compressed --parallel 4

Processing ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ 100% invoice_2024.pdf

Pipeline Execution Summary
──────────────────────────
  Total:     47
  Succeeded: 47
  Failed:    0
  Skipped:   0
  Duration:  12.34s
  Input:     892.4 MB
  Output:    234.1 MB
  Savings:   73.8%
```

### Dry Run Preview
```bash
$ batchforge pdf compress ./invoices --dry-run

Pipeline Plan Summary
─────────────────────
  Total files:    23
  Will process:   23
  Will skip:      0
  Will overwrite: 0
  Input size:     45.2 MB

  → invoice_001.pdf
    └─> ./invoices/invoice_001_compressed.pdf
  → invoice_002.pdf
    └─> ./invoices/invoice_002_compressed.pdf
  ... and 21 more files
```

---

## Performance

Benchmarks on AMD Ryzen 9 5900X, 64GB RAM, NVMe SSD:

| Operation | Files | Size | Time | Throughput |
|-----------|-------|------|------|------------|
| Compress | 100 | 1.2 GB | 18s | 66 MB/s |
| Merge | 50 | 500 MB | 4s | 125 MB/s |
| Extract Text | 100 | 800 MB | 12s | 66 MB/s |

*Parallel execution with `--parallel 8`. Results vary depending on PDF structure, compression characteristics, and I/O performance.*

---

## Extending BatchForge

Implement `IPipelineStep` to add custom operations:

```csharp
public class MyCustomStep : IPipelineStep
{
    public string StepId => "custom.mystep";
    public string Description => "My custom operation";
    public IReadOnlyList<string> SupportedExtensions => [".pdf"];

    public ValidationResult Validate(StepOptions options) 
        => ValidationResult.Valid();

    public string GetOutputPath(string inputPath, StepOptions options)
        => Path.ChangeExtension(inputPath, "_processed.pdf");

    public async Task<StepResult> ExecuteAsync(
        string inputPath, string outputPath, StepOptions options,
        IProgress<StepProgress>? progress, CancellationToken ct)
    {
        // Your logic here
        return StepResult.Success(inputPath, outputPath, duration);
    }
}
```

---

## Roadmap

- [x] PDF merge, split, compress, text extraction
- [x] Image extraction with PNG reconstruction
- [x] Parallel execution with bounded concurrency
- [x] Dry-run planning mode
- [x] Cross-platform support
- [ ] Image operations (resize, convert, optimize)
- [ ] Document conversion (DOCX ↔ PDF)
- [ ] Pipeline chaining with config files
- [ ] GPU acceleration module (commercial)

---

## Commercial Support

For enterprise deployments, custom operations, and high-performance processing:

**MCMLV1, LLC** offers:

- **Custom Pipeline Development** — Build operations specific to your document workflows
- **Document Pipeline Integration** — Connect BatchForge to your existing systems
- **Compliance-Ready Deployments** — Air-gapped, offline, audit-friendly configurations
- **Priority Support** — Direct access to engineering for issue resolution
- **GPU Acceleration Module** — 10-50x faster processing for high-volume workloads

🌐 [mcmlv1.com](https://mcmlv1.com)  
📧 contact@mcmlv1.com

---

## License

MIT License - see [LICENSE](LICENSE) for details.

Copyright © 2026 MCMLV1, LLC

---

<p align="center">
  <strong>MCMLV1, LLC</strong><br>
  <a href="https://mcmlv1.com">mcmlv1.com</a><br>
  <em>Professional Software Solutions</em>
</p>
