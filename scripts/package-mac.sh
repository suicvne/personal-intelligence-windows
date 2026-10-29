#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
MAC_APP="$INTEL_ROOT/artifacts/Intel Prototype.app"
mkdir -p "$MAC_APP/Contents/MacOS"
dotnet publish src/Intel.Desktop -c Release -r osx-arm64 --self-contained true -p:PublishTrimmed=false -o "$MAC_APP/Contents/MacOS"
cat > "$MAC_APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Intel</string>
<key>CFBundleIdentifier</key><string>local.intel.windows-prototype</string>
<key>CFBundleName</key><string>Intel Prototype</string>
<key>CFBundleVersion</key><string>0.1.0</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --deep --sign - "$MAC_APP"
printf '%s\n' "$MAC_APP"
