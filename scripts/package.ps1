param([ValidateSet('osx-arm64','osx-x64','win-x64','win-arm64')][string]$Runtime = 'osx-arm64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$python = if ($IsWindows) { 'python' } else { 'python3' }
& $python (Join-Path $root 'scripts/prepare-d2.py') $Runtime
if ($LASTEXITCODE -ne 0) { throw 'D2 preparation failed' }
$out = Join-Path $root "build/$Runtime"
if (Test-Path $out) { throw "出力先が既にあります: $out" }
New-Item -ItemType Directory -Path $out | Out-Null
$publish = Join-Path $out 'publish'
dotnet publish (Join-Path $root 'src/ViMarkdown') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
Copy-Item (Join-Path $root 'README.md') $out
if ($Runtime.StartsWith('osx')) {
    $app = Join-Path $out 'ViMarkdown.app/Contents'
    New-Item -ItemType Directory -Path "$app/MacOS" -Force | Out-Null
    New-Item -ItemType Directory -Path "$app/Resources" -Force | Out-Null
    Copy-Item (Join-Path $root 'src/ViMarkdown/Assets/ViMarkdown.icns') "$app/Resources/ViMarkdown.icns"
    Move-Item "$publish/*" "$app/MacOS/"
    Remove-Item $publish
    Move-Item "$app/MacOS/Fonts" "$app/Resources/Fonts"
    Move-Item "$app/MacOS/Licenses" "$app/Resources/Licenses"
    $version = dotnet msbuild (Join-Path $root 'src/ViMarkdown/ViMarkdown.csproj') -getProperty:Version -nologo
    if ($LASTEXITCODE -ne 0) { throw 'version lookup failed' }
    $bundleVersion = $version.Split('-')[0]
    @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>CFBundleExecutable</key><string>ViMarkdown</string><key>CFBundleIconFile</key><string>ViMarkdown.icns</string><key>CFBundleIdentifier</key><string>local.vimarkdown.app</string><key>CFBundleName</key><string>ViMarkdown</string><key>CFBundleVersion</key><string>$bundleVersion</string><key>CFBundlePackageType</key><string>APPL</string><key>NSHighResolutionCapable</key><true/></dict></plist>
"@ | Set-Content "$app/Info.plist" -Encoding utf8
    chmod +x "$app/MacOS/ViMarkdown"
    chmod +x "$app/MacOS/Tools/D2/d2"
    codesign --force --sign - "$app/MacOS/Tools/D2/d2"
    if ($LASTEXITCODE -ne 0) { throw 'D2 codesign failed' }
    codesign --force --deep --sign - (Join-Path $out 'ViMarkdown.app')
    if ($LASTEXITCODE -ne 0) { throw 'codesign failed' }
    ditto -c -k --sequesterRsrc --keepParent $out "$out.zip"
    if ($LASTEXITCODE -ne 0) { throw 'archive failed' }
} else {
    @'
$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs/ViMarkdown'
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$PSScriptRoot/publish/*" $target -Recurse -Force
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'ViMarkdown.lnk'))
$link.TargetPath = Join-Path $target 'ViMarkdown.exe'
$link.Save()
'@ | Set-Content (Join-Path $out 'Install.ps1') -Encoding utf8
    Compress-Archive -Path "$out/*" -DestinationPath "$out.zip"
}
Write-Host "Package: $out.zip"
