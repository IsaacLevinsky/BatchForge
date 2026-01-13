#!/bin/bash
#
# BatchForge Release Build Script
# Builds self-contained single-file executables for all platforms,
# creates distribution packages, and generates SHA256 checksums.
#
# Usage: ./scripts/release.sh [version]
# Example: ./scripts/release.sh 1.0.0
#
# Requires: .NET 8 SDK, tar, zip (optional for Windows builds)
#

set -e

VERSION="${1:-1.0.0}"
PROJECT_PATH="src/BatchForge.Cli/BatchForge.Cli.csproj"
DIST_DIR="dist"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

echo ""
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}  BatchForge Release Build v${VERSION}${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""

# Clean dist directory
if [ -d "$DIST_DIR" ]; then
    echo -e "${YELLOW}Cleaning $DIST_DIR...${NC}"
    rm -rf "$DIST_DIR"
fi
mkdir -p "$DIST_DIR"

# Function to build a platform
build_platform() {
    local RID=$1
    local OUTPUT=$2
    local ARCHIVE=$3
    local TYPE=$4
    
    echo ""
    echo -e "${GREEN}Building for $RID...${NC}"
    
    # Publish
    dotnet publish "$PROJECT_PATH" \
        -c Release \
        -r "$RID" \
        --self-contained true \
        /p:PublishSingleFile=true \
        /p:IncludeNativeLibrariesForSelfExtract=true \
        /p:Version="$VERSION" \
        /p:AssemblyVersion="$VERSION" \
        -o "$DIST_DIR/$RID"
    
    # Find the executable
    local EXE_PATH="$DIST_DIR/$RID/$OUTPUT"
    
    # Handle different executable names
    if [ ! -f "$EXE_PATH" ]; then
        if [ -f "$DIST_DIR/$RID/BatchForge.Cli" ]; then
            mv "$DIST_DIR/$RID/BatchForge.Cli" "$EXE_PATH"
        elif [ -f "$DIST_DIR/$RID/BatchForge.Cli.exe" ]; then
            mv "$DIST_DIR/$RID/BatchForge.Cli.exe" "$EXE_PATH"
        fi
    fi
    
    # Make executable (for non-Windows)
    if [[ "$RID" != win-* ]]; then
        chmod +x "$EXE_PATH"
    fi
    
    # Create archive
    echo -e "${YELLOW}Creating $ARCHIVE...${NC}"
    
    pushd "$DIST_DIR/$RID" > /dev/null
    
    if [ "$TYPE" = "zip" ]; then
        zip -q "../$ARCHIVE" "$OUTPUT"
    else
        tar -czf "../$ARCHIVE" "$OUTPUT"
    fi
    
    popd > /dev/null
    
    # Clean up publish directory
    rm -rf "$DIST_DIR/$RID"
    
    echo -e "${GREEN}  Created: $ARCHIVE${NC}"
}

# Build all platforms
build_platform "linux-x64"   "batchforge"     "batchforge-linux-x64.tar.gz"   "tar"
build_platform "win-x64"     "batchforge.exe" "batchforge-win-x64.zip"        "zip"

# macOS builds (comment out if you don't need them)
build_platform "osx-arm64"   "batchforge"     "batchforge-osx-arm64.tar.gz"   "tar"
build_platform "osx-x64"     "batchforge"     "batchforge-osx-x64.tar.gz"     "tar"

# Generate SHA256 checksums
echo ""
echo -e "${YELLOW}Generating SHA256 checksums...${NC}"

CHECKSUM_FILE="$DIST_DIR/SHA256SUMS.txt"
> "$CHECKSUM_FILE"

for file in "$DIST_DIR"/*.{zip,tar.gz} 2>/dev/null; do
    if [ -f "$file" ]; then
        HASH=$(sha256sum "$file" | cut -d' ' -f1)
        NAME=$(basename "$file")
        echo "$HASH  $NAME" >> "$CHECKSUM_FILE"
        echo -e "  $NAME"
        echo -e "  \033[90m$HASH\033[0m"
    fi
done

echo ""
echo -e "${CYAN}========================================${NC}"
echo -e "${CYAN}  Build Complete!${NC}"
echo -e "${CYAN}========================================${NC}"
echo ""
echo -e "${GREEN}Distribution files in: $DIST_DIR/${NC}"
echo ""

ls -lh "$DIST_DIR" | tail -n +2 | while read line; do
    echo "  $line"
done

echo ""
echo -e "${YELLOW}Upload these files to GitHub Releases:${NC}"
for file in "$DIST_DIR"/*.{zip,tar.gz,txt} 2>/dev/null; do
    if [ -f "$file" ]; then
        echo "  - $(basename "$file")"
    fi
done
echo ""
