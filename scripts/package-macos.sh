#!/bin/sh
set -eu
runtime=${1:-osx-arm64}
case "$runtime" in osx-arm64|osx-x64) ;; *) echo 'osx-arm64 または osx-x64 を指定してください' >&2; exit 1;; esac
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
out=${2:-"$root/build/$runtime"}
case "$out" in /*) ;; *) out="$root/$out";; esac
[ ! -e "$out" ] || { echo "出力先が既にあります: $out" >&2; exit 1; }
[ ! -e "$out.zip" ] || { echo "出力先が既にあります: $out.zip" >&2; exit 1; }
python3 "$root/scripts/prepare-d2.py" "$runtime"
app="$out/ViMarkdown.app/Contents"
mkdir -p "$app/MacOS" "$app/Resources"
dotnet publish "$root/src/ViMarkdown" -c Release -r "$runtime" --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false -o "$app/MacOS"
mkdir -p "$app/Resources"
mv "$app/MacOS/Fonts" "$app/Resources/Fonts"
mv "$app/MacOS/Licenses" "$app/Resources/Licenses"
cp "$root/src/ViMarkdown/Assets/ViMarkdown.icns" "$app/Resources/ViMarkdown.icns"
version=$(dotnet msbuild "$root/src/ViMarkdown/ViMarkdown.csproj" -getProperty:Version -nologo)
bundle_version=${version%%-*}
cat > "$app/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>CFBundleExecutable</key><string>ViMarkdown</string><key>CFBundleIconFile</key><string>ViMarkdown.icns</string><key>CFBundleIdentifier</key><string>local.vimarkdown.app</string><key>CFBundleName</key><string>ViMarkdown</string><key>CFBundleVersion</key><string>$bundle_version</string><key>CFBundlePackageType</key><string>APPL</string><key>NSHighResolutionCapable</key><true/></dict></plist>
PLIST
chmod +x "$app/MacOS/ViMarkdown"
chmod +x "$app/MacOS/Tools/D2/d2"
codesign --force --sign - "$app/MacOS/Tools/D2/d2"
codesign --force --deep --sign - "$out/ViMarkdown.app"
cp "$root/README.md" "$out/README.md"
ditto -c -k --sequesterRsrc --keepParent "$out" "$out.zip"
echo "Package: $out.zip"
