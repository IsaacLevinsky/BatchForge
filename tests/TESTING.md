# BatchForge Test Suite

This document describes the manual CLI test suite used to validate BatchForge functionality. These tests verify real-world behavior across different PDF types and edge cases.

## Test Environment

- **Platform:** Windows 11 (PowerShell) / Linux (Bash)
- **Runtime:** .NET 8.0
- **Run Command:** `dotnet run --project src/BatchForge.Cli -- <command>`

## Quick Smoke Test

Verify basic functionality in 30 seconds:

```powershell
dotnet run --project src/BatchForge.Cli -- pdf merge .\tests\pdfs -o .\tests\out\merged_text.pdf
dotnet run --project src/BatchForge.Cli -- pdf split .\tests\out\merged_text.pdf --burst -o .\tests\out\pages
dotnet run --project src/BatchForge.Cli -- pdf text .\tests\pdfs -o .\tests\out\text
```

If all three succeed, core functionality is working.

---

## Known Limitations

Before running tests, understand these documented constraints:

- **No OCR** — Scanned PDFs yield empty text output (this is expected)
- **Compression varies** — Some PDFs may not reduce in size (already optimized)
- **Image extraction depends on encoding** — Some embedded images may not extract

---

## Test Data Structure

```
tests/
├── pdfs/                    # Searchable text PDFs (Project Gutenberg books)
│   ├── Moby Dick; or The Whale _ Project Gutenberg.pdf
│   ├── Pride and prejudice _ Project Gutenberg.pdf
│   └── The Adventures of Sherlock Holmes _ Project Gutenberg.pdf
│
├── mixed/                   # PDFs with embedded images and text
│   ├── a11_missionreport.pdf    # Apollo 11 mission report (~11.5 MB)
│   └── Chou136.pdf              # Academic paper with figures (~1.9 MB)
│
├── scanned/                 # Image-only PDFs (no extractable text)
│   ├── page1.pdf
│   ├── page2.pdf
│   └── page3.pdf
│
└── out/                     # Test output directory (gitignored)
    ├── merged_text.pdf
    ├── pages/
    ├── ranges/
    ├── compressed/
    ├── text/
    ├── text_scanned/
    ├── images_scanned/
    ├── images_mixed/
    └── cancel_test/
```

---

## Test Categories

### 1. CLI Wiring Tests

**Purpose:** Verify command routing and help system work correctly.

```powershell
# Root help
dotnet run --project src/BatchForge.Cli -- --help

# PDF subcommand help
dotnet run --project src/BatchForge.Cli -- pdf --help

# Individual command help
dotnet run --project src/BatchForge.Cli -- pdf split --help
```

**Expected:** Clean help output with descriptions, options, and examples.

---

### 2. PDF Merge Tests

**Purpose:** Verify multiple PDFs can be combined into one.

**Test Data:** `tests/pdfs/` (3 Gutenberg books, ~7 MB total)

```powershell
# Dry run - preview merge
dotnet run --project src/BatchForge.Cli -- pdf merge .\tests\pdfs --dry-run

# Execute merge
dotnet run --project src/BatchForge.Cli -- pdf merge .\tests\pdfs -o .\tests\out\merged_text.pdf
```

**Expected Results:**
- Dry run shows file list and output path
- Execute creates single merged PDF
- File order is deterministic (alphabetical by filename, culture-invariant)

**Validates:**
- Directory scanning
- Multi-file PDF assembly
- Output path creation

---

### 3. PDF Split Tests

**Purpose:** Verify PDFs can be split by page ranges or burst into individual pages.

**Test Data:** `tests/out/merged_text.pdf` (output from merge test)

#### 3a. Burst Mode (one file per page)

```powershell
# Dry run
dotnet run --project src/BatchForge.Cli -- pdf split .\tests\out\merged_text.pdf --burst -o .\tests\out\pages --dry-run

# Execute
dotnet run --project src/BatchForge.Cli -- pdf split .\tests\out\merged_text.pdf --burst -o .\tests\out\pages
```

**Expected Results:**
- Creates one PDF per page (e.g., `merged_text_page001.pdf`, `merged_text_page002.pdf`, ...)
- All pages extracted

#### 3b. Page Range Mode

```powershell
# Dry run
dotnet run --project src/BatchForge.Cli -- pdf split .\tests\out\merged_text.pdf --pages 1-3,5 -o .\tests\out\ranges --dry-run

# Execute
dotnet run --project src/BatchForge.Cli -- pdf split .\tests\out\merged_text.pdf --pages 1-3,5 -o .\tests\out\ranges
```

**Expected Results:**
- Creates part files for specified ranges
- Supports: single pages (`5`), ranges (`1-3`), and combinations (`1-3,5,10-15`)

**Validates:**
- Parameter parsing (`--pages`, `--burst`)
- Page range syntax
- Output file naming

---

### 4. PDF Compress Tests

**Purpose:** Verify PDFs can be optimized for smaller file size.

**Test Data:** `tests/mixed/` (2 PDFs, ~13 MB total)

```powershell
# Dry run
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests\mixed -o .\tests\out\compressed --parallel 4 --dry-run

# Execute
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests\mixed -o .\tests\out\compressed --parallel 4
```

**Expected Results:**
- Compression summary shows input/output sizes
- Some PDFs may not compress (already optimized) — this is expected
- No corruption of output files

#### 4a. Overwrite Safety Test

```powershell
# Run again without --overwrite (should skip)
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests\mixed -o .\tests\out\compressed --parallel 4

# Run with --overwrite (should succeed)
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests\mixed -o .\tests\out\compressed --parallel 4 --overwrite
```

**Expected Results:**
- Without `--overwrite`: Files skipped, no overwrites
- With `--overwrite`: Files replaced

**Validates:**
- Safe-by-default behavior
- Explicit overwrite flag
- Parallel execution

---

### 5. PDF Text Extraction Tests

**Purpose:** Verify text content can be extracted from PDFs.

#### 5a. Positive Test (Searchable PDFs)

**Test Data:** `tests/pdfs/` (Gutenberg books with embedded text)

```powershell
# Dry run
dotnet run --project src/BatchForge.Cli -- pdf text .\tests\pdfs -o .\tests\out\text --parallel 4 --dry-run

# Execute
dotnet run --project src/BatchForge.Cli -- pdf text .\tests\pdfs -o .\tests\out\text --parallel 4
```

**Expected Results:**
- Creates `.txt` file for each PDF
- Text files contain readable book content
- Page markers included (`--- Page 1 ---`)

#### 5b. Negative Test (Scanned PDFs)

**Test Data:** `tests/scanned/` (image-only PDFs, no OCR text)

```powershell
# Dry run
dotnet run --project src/BatchForge.Cli -- pdf text .\tests\scanned -o .\tests\out\text_scanned --dry-run

# Execute
dotnet run --project src/BatchForge.Cli -- pdf text .\tests\scanned -o .\tests\out\text_scanned
```

**Expected Results:**
- `Succeeded: 3` (not failed)
- `Output: 0 B` (no text extracted)
- No crash, no error
- May log "No extractable text found" per file (optional)

**Validates:**
- Graceful handling of image-only PDFs
- OCR is not included (documented limitation)
- Empty results are success, not failure

---

### 6. PDF Image Extraction Tests

**Purpose:** Verify embedded images can be extracted from PDFs.

#### 6a. Scanned PDFs

**Test Data:** `tests/scanned/`

```powershell
# Dry run
dotnet run --project src/BatchForge.Cli -- pdf images .\tests\scanned -o .\tests\out\images_scanned --dry-run

# Execute
dotnet run --project src/BatchForge.Cli -- pdf images .\tests\scanned -o .\tests\out\images_scanned
```

**Expected Results:**
- Creates `_images` folder for each PDF
- Extracts embedded images (if any)
- Graceful handling if no images found
- It is valid for output size to be `0 B` if no embedded images are present

#### 6b. Mixed Content PDFs

**Test Data:** `tests/mixed/` (documents with charts, photos, diagrams)

```powershell
# Dry run
dotnet run --project src/BatchForge.Cli -- pdf images .\tests\mixed -o .\tests\out\images_mixed --parallel 4 --dry-run

# Execute
dotnet run --project src/BatchForge.Cli -- pdf images .\tests\mixed -o .\tests\out\images_mixed --parallel 4
```

**Expected Results:**
- Images extracted as `.jpg`, `.png`, or other formats
- File naming: `{pdfname}_p{page}_img{index}.{ext}`
- Substantial output (Apollo 11 report has many images)

**Validates:**
- JPEG passthrough (DCTDecode)
- PNG reconstruction (FlateDecode)
- CMYK to RGB conversion
- Directory output handling

---

### 7. Recursive Safety Test

**Purpose:** Verify `--recursive` doesn't cause infinite loops or reprocess output.

```powershell
# This should safely handle output directories
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests -r -o .\tests\out\cancel_test --parallel 8 --dry-run
```

**Expected Results:**
- Files inside output directories are either excluded or safely skipped
- No recursion loop occurs
- No output files are reprocessed by default
- Only source files from `tests/pdfs/`, `tests/mixed/`, `tests/scanned/` are processed

**Validates:**
- Output directory exclusion or safe skip behavior
- Safe recursive processing

---

### 8. Cancellation Safety Test

**Purpose:** Verify Ctrl+C exits cleanly without corrupting files.

```powershell
# Start a long operation
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests -r -o .\tests\out\cancel_test --parallel 8

# Press Ctrl+C while running
```

**Expected Results:**
- Graceful shutdown message
- No partially written files
- No locked files
- Can re-run immediately

**Validates:**
- CancellationToken support
- Atomic file operations (temp → move)

---

## Test Results Summary

| Test | Status | Notes |
|------|--------|-------|
| CLI help/routing | ✅ Pass | Professional output |
| PDF merge | ✅ Pass | Auto-creates output directory |
| PDF split (burst) | ✅ Pass | One file per page |
| PDF split (ranges) | ✅ Pass | Flexible page selection |
| PDF compress | ✅ Pass | Parallel execution works |
| Overwrite safety | ✅ Pass | Skips without flag |
| Text extraction (positive) | ✅ Pass | Readable output |
| Text extraction (negative) | ✅ Pass | Graceful "no text" handling |
| Image extraction (scanned) | ✅ Pass | Handles empty gracefully |
| Image extraction (mixed) | ✅ Pass | Extracts embedded images |
| Recursive safety | ✅ Pass | No infinite loops |
| Cancellation | ✅ Pass | Clean shutdown |

---

## Running the Full Test Suite

Execute all tests in order:

```powershell
cd C:\Users\isaac\Projects\BatchForge

# 1. CLI wiring
dotnet run --project src/BatchForge.Cli -- --help
dotnet run --project src/BatchForge.Cli -- pdf --help

# 2. Merge
dotnet run --project src/BatchForge.Cli -- pdf merge .\tests\pdfs -o .\tests\out\merged_text.pdf

# 3. Split (burst)
dotnet run --project src/BatchForge.Cli -- pdf split .\tests\out\merged_text.pdf --burst -o .\tests\out\pages

# 4. Split (ranges)
dotnet run --project src/BatchForge.Cli -- pdf split .\tests\out\merged_text.pdf --pages 1-3,5 -o .\tests\out\ranges

# 5. Compress
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests\mixed -o .\tests\out\compressed --parallel 4

# 6. Text (positive)
dotnet run --project src/BatchForge.Cli -- pdf text .\tests\pdfs -o .\tests\out\text --parallel 4

# 7. Text (negative - scanned)
dotnet run --project src/BatchForge.Cli -- pdf text .\tests\scanned -o .\tests\out\text_scanned

# 8. Images (scanned)
dotnet run --project src/BatchForge.Cli -- pdf images .\tests\scanned -o .\tests\out\images_scanned

# 9. Images (mixed)
dotnet run --project src/BatchForge.Cli -- pdf images .\tests\mixed -o .\tests\out\images_mixed --parallel 4

# 10. Recursive safety
dotnet run --project src/BatchForge.Cli -- pdf compress .\tests -r -o .\tests\out\cancel_test --parallel 8 --dry-run
```

---

## Adding Test Data

### Searchable PDFs (Project Gutenberg)

Project Gutenberg does not consistently provide native PDF downloads.  
To ensure deterministic, searchable-text PDFs for testing, the following method is used:

1. Download the **HTML ZIP** version of a book from https://www.gutenberg.org/
2. Extract the ZIP file
3. Open the main `.html` file in a modern browser (Edge, Chrome, Firefox)
4. Right-click → **Print…**
5. Select **Microsoft Print to PDF** (or equivalent system PDF printer)
6. Save the resulting PDF into `tests/pdfs/`

This produces:
- Fully searchable text PDFs
- Consistent pagination
- No OCR involvement
- Deterministic, reproducible test inputs

This approach avoids inconsistencies across Gutenberg mirrors and ensures reliable text extraction tests.

### Mixed Content PDFs

Good sources (public domain):
- NASA technical reports: https://ntrs.nasa.gov/
- Academic papers from arXiv: https://arxiv.org/
- Government publications: https://www.gpo.gov/

Keep total size reasonable (~15 MB max for the repo).

### Scanned PDFs

Create by:
1. Open any image (PNG/JPG) in a browser or image viewer
2. Print to PDF using system PDF printer
3. This creates a PDF with embedded image but no searchable text

Alternatively, use any known image-only PDF (receipts, old scanned documents).

---

## Repository Guidelines

### What to Commit

- `tests/pdfs/` — Gutenberg PDFs (small, public domain, ~7 MB)
- `tests/mixed/` — One small mixed PDF for CI (~2 MB max)
- `tests/scanned/` — Small image-only PDFs (~200 KB)
- `tests/TESTING.md` — This file

### What to Gitignore

```gitignore
# Test outputs
tests/out/
```

### Why This Matters

- **Reproducibility:** Anyone cloning the repo can run the same tests
- **CI/CD Ready:** Automated testing possible with committed test data
- **No Surprises:** Expected behavior is documented and verifiable

---

## Linux Test Commands

Same tests work on Linux with path adjustments:

```bash
cd ~/Projects/BatchForge

# Merge
dotnet run --project src/BatchForge.Cli -- pdf merge ./tests/pdfs -o ./tests/out/merged_text.pdf

# Split (burst)
dotnet run --project src/BatchForge.Cli -- pdf split ./tests/out/merged_text.pdf --burst -o ./tests/out/pages

# Text extraction
dotnet run --project src/BatchForge.Cli -- pdf text ./tests/pdfs -o ./tests/out/text --parallel 4
```

All commands use forward slashes on Linux/macOS.
