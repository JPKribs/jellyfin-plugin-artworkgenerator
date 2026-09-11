#!/bin/bash

set -e

# Configuration
CONFIGURATION="${1:-Release}"
OUTPUT_DIR="dist"
PROJECT_DIR="Jellyfin.Plugin.EpisodePosterGenerator"
PROJECT_FILE="$PROJECT_DIR/Jellyfin.Plugin.EpisodePosterGenerator.csproj"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

# MARK: Log function with consistent formatting
log() {
    local level="$1"
    local message="$2"
    case "$level" in
        "INFO")  echo -e "${CYAN}[INFO]${NC} $message" ;;
        "WARN")  echo -e "${YELLOW}[WARN]${NC} $message" ;;
        "ERROR") echo -e "${RED}[ERROR]${NC} $message" ;;
        "SUCCESS") echo -e "${GREEN}[SUCCESS]${NC} $message" ;;
        *)       echo -e "$message" ;;
    esac
}

# MARK: Get version from build.yaml (single source of truth)
get_plugin_version() {
    local build_file="build.yaml"

    if [[ -f "$build_file" ]]; then
        # Extract version value, handling both quoted and unquoted formats
        local version=$(grep '^version:' "$build_file" | cut -d':' -f2 | tr -d ' "')
        if [[ -n "$version" ]]; then
            echo "$version"
            return
        fi
    fi

    # Four-part fallback: a 3-part version would normalize the assembly version
    # mismatched against the install URL and break the plugin image.
    echo "0.0.0.0"
}

# MARK: Get plugin info from build.yaml
get_plugin_info() {
    local build_file="build.yaml"
    local name="Episode Poster Generator"
    local guid="b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e"

    if [[ -f "$build_file" ]]; then
        # Take everything after the first colon and strip one layer of quotes, so a name with spaces survives.
        local extracted_name=$(sed -n 's/^name:[[:space:]]*//p' "$build_file" | head -n 1 | sed -e 's/[[:space:]]*$//' -e 's/^"\(.*\)"$/\1/')
        local extracted_guid=$(grep '^guid:' "$build_file" | cut -d':' -f2 | tr -d ' "')

        [[ -n "$extracted_name" ]] && name="$extracted_name"
        [[ -n "$extracted_guid" ]] && guid="$extracted_guid"
    fi

    echo "$name|$guid"
}

# MARK: Validate embedded resources exist
validate_resources() {
    log "INFO" "Validating embedded resources exist"
    
    local missing_files=()
    local config_files=(
        "$PROJECT_DIR/Configuration/epg_posters.html"
        "$PROJECT_DIR/Configuration/epg_posters.js"
        "$PROJECT_DIR/Configuration/epg_settings.html"
        "$PROJECT_DIR/Configuration/epg_settings.js"
        "$PROJECT_DIR/Configuration/epg_logos.html"
        "$PROJECT_DIR/Configuration/epg_logos.js"
        "$PROJECT_DIR/Configuration/epg_profiles.html"
        "$PROJECT_DIR/Configuration/epg_profiles.js"
        "$PROJECT_DIR/Configuration/epg_shared.css"
    )
    
    for file in "${config_files[@]}"; do
        if [[ ! -f "$file" ]]; then
            missing_files+=("$file")
        else
            log "SUCCESS" "Found: $file"
        fi
    done
    
    if [[ ${#missing_files[@]} -gt 0 ]]; then
        log "ERROR" "Missing embedded resource files:"
        for file in "${missing_files[@]}"; do
            log "ERROR" "  - $file"
        done
        log "ERROR" "These files are required for the configuration page to work"
        return 1
    fi
    
    log "SUCCESS" "All embedded resources found"
    return 0
}

# MARK: Main build process
main() {
    log "INFO" "Starting Episode Poster Generator Plugin build"
    
    # Get version and plugin info once at the start
    log "INFO" "Reading version from build.yaml"
    VERSION=$(get_plugin_version)
    log "SUCCESS" "Version: $VERSION"
    
    log "INFO" "Reading plugin info from build.yaml"
    PLUGIN_INFO=$(get_plugin_info)
    PLUGIN_NAME=$(echo "$PLUGIN_INFO" | cut -d'|' -f1)
    PLUGIN_GUID=$(echo "$PLUGIN_INFO" | cut -d'|' -f2)
    log "SUCCESS" "Plugin: $PLUGIN_NAME"
    log "SUCCESS" "GUID: $PLUGIN_GUID"
    
    log "INFO" "Build configuration: $CONFIGURATION"
    log "INFO" "Project file: $PROJECT_FILE"
    
    # Check if project file exists
    if [[ ! -f "$PROJECT_FILE" ]]; then
        log "ERROR" "Project file not found: $PROJECT_FILE"
        exit 1
    fi
    
    # Validate embedded resources
    if ! validate_resources; then
        exit 1
    fi
    
    # Clean previous builds
    if [[ "$2" == "--clean" ]] || [[ -d "$OUTPUT_DIR" ]]; then
        log "INFO" "Cleaning previous builds"
        rm -rf "$OUTPUT_DIR"
        log "INFO" "Running dotnet clean"
        dotnet clean "$PROJECT_FILE" --configuration "$CONFIGURATION" --verbosity quiet
    fi
    
    # Create output directory
    log "INFO" "Creating output directory: $OUTPUT_DIR"
    mkdir -p "$OUTPUT_DIR"
    
    # Restore packages
    log "INFO" "Restoring NuGet packages"
    if ! dotnet restore "$PROJECT_FILE" --verbosity minimal; then
        log "ERROR" "Package restore failed"
        exit 1
    fi
    log "SUCCESS" "Package restore completed"
    
    # Build the project with version from build.yaml
    log "INFO" "Building project with configuration: $CONFIGURATION, version: $VERSION"
    if ! dotnet build "$PROJECT_FILE" --configuration "$CONFIGURATION" --no-restore --verbosity minimal -p:Version="$VERSION"; then
        log "ERROR" "Build failed"
        exit 1
    fi
    
    # Find the built DLL
    local dll_path="$PROJECT_DIR/bin/$CONFIGURATION/net10.0/Jellyfin.Plugin.EpisodePosterGenerator.dll"
    if [[ ! -f "$dll_path" ]]; then
        log "ERROR" "Could not find built DLL at: $dll_path"
        exit 1
    fi
    
    log "SUCCESS" "Build completed: $dll_path"
    
    # Create ZIP package
    local zip_name="jellyfin-plugin-episodepostergenerator-$VERSION.zip"
    local zip_path="$OUTPUT_DIR/$zip_name"
    
    log "INFO" "Creating package: $zip_name"
    
    # Create temporary directory for packaging
    local temp_dir="$OUTPUT_DIR/temp"
    log "INFO" "Creating temporary directory: $temp_dir"
    mkdir -p "$temp_dir"
    
    # Copy DLL to temp directory
    log "INFO" "Copying DLL to package directory"
    cp "$dll_path" "$temp_dir/"

    # CRITICAL: meta.json needs "name". Jellyfin 12 groups discovered plugins by
    # name, so every nameless manifest collapses into one empty-name entry and all
    # but one of those plugins silently fails to load.
    # Bundle the plugin image AND a meta.json inside the package, exactly like
    # the official Jellyfin plugins (their release zips ship a meta.json too).
    # We go one step further and set "imagePath" + ship Logo.png, so Jellyfin
    # serves the installed-plugin icon (/Plugins/{id}/{ver}/Image) straight from
    # disk with ZERO dependency on the server downloading imageUrl at install.
    # Jellyfin's ReconcileManifest preserves a non-empty bundled imagePath.
    local logo_src="$PROJECT_DIR/Assets/Logo.png"
    if [[ -f "$logo_src" ]]; then
        log "INFO" "Bundling plugin image: Logo.png"
        cp "$logo_src" "$temp_dir/Logo.png"

        local target_abi=$(grep '^targetAbi:' build.yaml | cut -d':' -f2 | tr -d ' "')
        [[ -z "$target_abi" ]] && target_abi="12.0.0.0"
        local meta_timestamp=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

        # Omit name/description/etc on purpose: ReconcileManifest lets a non-empty
        # bundled value override the repo entry, so we only ship fields that must
        # be correct (guid must match or the plugin is marked Malfunctioned).
        log "INFO" "Generating bundled meta.json with imagePath"
        cat > "$temp_dir/meta.json" <<EOF
{
    "guid": "$PLUGIN_GUID",
    "name": "$PLUGIN_NAME",
    "version": "$VERSION",
    "targetAbi": "$target_abi",
    "timestamp": "$meta_timestamp",
    "status": "Active",
    "autoUpdate": true,
    "imagePath": "Logo.png",
    "assemblies": [
        "Jellyfin.Plugin.EpisodePosterGenerator.dll"
    ]
}
EOF
    else
        log "WARN" "Plugin image not found at $logo_src - installed icon will rely on imageUrl download"
    fi

    # Create ZIP with all files in temp directory
    log "INFO" "Creating ZIP archive"
    if command -v zip >/dev/null 2>&1; then
        (cd "$temp_dir" && zip -qr "../$zip_name" .)
    elif command -v python3 >/dev/null 2>&1; then
        python3 -c "
import zipfile
import os
with zipfile.ZipFile('$zip_path', 'w') as zf:
    for root, dirs, files in os.walk('$temp_dir'):
        for file in files:
            file_path = os.path.join(root, file)
            arcname = os.path.relpath(file_path, '$temp_dir')
            zf.write(file_path, arcname)
"
    else
        log "ERROR" "No zip utility found (zip or python3 required)"
        exit 1
    fi
    
    # Clean up temp directory
    log "INFO" "Cleaning up temporary directory"
    rm -rf "$temp_dir"
    
    if [[ ! -f "$zip_path" ]]; then
        log "ERROR" "Failed to create ZIP package"
        exit 1
    fi
    
    log "SUCCESS" "Package created: $zip_path"
    
    # Calculate MD5 checksum
    log "INFO" "Calculating MD5 checksum"
    local md5_hash
    if command -v md5sum >/dev/null 2>&1; then
        md5_hash=$(md5sum "$zip_path" | cut -d' ' -f1)
    elif command -v md5 >/dev/null 2>&1; then
        md5_hash=$(md5 -q "$zip_path")
    else
        log "WARN" "MD5 utility not found, skipping checksum"
        md5_hash="N/A"
    fi
    
    if [[ "$md5_hash" != "N/A" ]]; then
        local checksum_file="$OUTPUT_DIR/$zip_name.md5"
        echo "$md5_hash" > "$checksum_file"
        log "SUCCESS" "Checksum file created: $checksum_file"
    fi
    
    # Get file size
    local file_size
    if command -v du >/dev/null 2>&1; then
        file_size=$(du -h "$zip_path" | cut -f1)
    else
        file_size="Unknown"
    fi
    
    # Display final results
    echo
    log "SUCCESS" "Build Summary:"
    echo "  Plugin Name: $PLUGIN_NAME"
    echo "  Version: $VERSION" 
    echo "  GUID: $PLUGIN_GUID"
    echo "  Package: $zip_path"
    echo "  Size: $file_size"
    echo "  MD5: $md5_hash"
    [[ "$md5_hash" != "N/A" ]] && echo "  Checksum: $checksum_file"
    
    echo
    log "SUCCESS" "Build completed successfully!"
    log "INFO" "Package ready for Jellyfin plugin installation"
}

# Run main function with all arguments
main "$@"