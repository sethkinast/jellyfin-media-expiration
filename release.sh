#!/bin/bash
# release.sh — Build, package, and stage a MediaExpiration plugin release.
#
# Usage: ./release.sh [version] [changelog]
#   version:   4-part version (e.g. 1.1.0.0). Defaults to <Version> in csproj.
#   changelog: Short description of changes. Defaults to "Release {version}."
#
# What it does:
#   1. Builds Release.
#   2. Zips the DLL + meta.json into build/mediaexpiration-{version}.zip.
#   3. Computes the MD5 checksum of the zip.
#   4. Prepends a version entry to manifest.json (newest first).
#
# What YOU do after:
#   1. Create the GitHub Release (tag v{version}), attach the zip from build/.
#   2. git add manifest.json && git commit && git push.
#   3. Jellyfin picks up the new version on its next repository refresh.

set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-$(grep -oPm1 '(?<=<Version>)[^<]+' Jellyfin.Plugin.MediaExpiration.csproj)}"
CHANGELOG="${2:-Release $VERSION.}"
TARGET_ABI="12.1.0.0"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
ZIP_NAME="mediaexpiration-${VERSION}.zip"
SOURCE_URL="https://github.com/sethkinast/jellyfin-media-expiration/releases/download/v${VERSION}/${ZIP_NAME}"

echo "=== Building MediaExpiration $VERSION ==="
NUGET_PACKAGES=/home/hatch/.nuget-packages /home/hatch/.dotnet10/dotnet build -c Release

echo "=== Packaging ==="
mkdir -p build
DLL="bin/Release/net10.0/Jellyfin.Plugin.MediaExpiration.dll"
META="bin/Release/net10.0/meta.json"
rm -f "build/${ZIP_NAME}"
(cd "$(dirname "$DLL")" && zip -j "$OLDPWD/build/${ZIP_NAME}" "$(basename "$DLL")" "$(basename "$META")")

echo "=== Checksum ==="
CHECKSUM="$(md5sum "build/${ZIP_NAME}" | awk '{print $1}')"
echo "MD5: $CHECKSUM"

echo "=== Updating manifest.json ==="
python3 - "$VERSION" "$CHANGELOG" "$TARGET_ABI" "$SOURCE_URL" "$CHECKSUM" "$TIMESTAMP" <<'PYEOF'
import json, sys
version, changelog, target_abi, source_url, checksum, timestamp = sys.argv[1:7]

with open('manifest.json') as f:
    manifest = json.load(f)

entry = {
    "version": version,
    "changelog": changelog,
    "targetAbi": target_abi,
    "sourceUrl": source_url,
    "checksum": checksum,
    "timestamp": timestamp,
}

# Remove any existing entry for this version (idempotent re-runs)
manifest[0]["versions"] = [v for v in manifest[0]["versions"] if v["version"] != version]
# Newest first
manifest[0]["versions"].insert(0, entry)

with open('manifest.json', 'w') as f:
    json.dump(manifest, f, indent=2)
    f.write('\n')

print(f"Added {version} to manifest.json")
PYEOF

echo ""
echo "=== Done ==="
echo "Zip: build/${ZIP_NAME}"
echo "Next: create GitHub Release v${VERSION}, attach the zip,"
echo "      then git add manifest.json && git commit && git push."
