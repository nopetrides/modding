param(
    [string]$ValheimManaged = "",
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'Hand_Drawn_Map.csproj'
$packageDir = Join-Path $PSScriptRoot 'Thunderstore'
$manifest = Get-Content -LiteralPath (Join-Path $packageDir 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.name -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Invalid package name.' }
if ($manifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
if ($manifest.description.Length -gt 250) { throw 'Package description exceeds 250 characters.' }
foreach ($dependency in $manifest.dependencies) {
    if ($dependency -notmatch '^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$') { throw "Invalid dependency: $dependency" }
}

$buildArgs = @('build', $projectPath, '--configuration', 'Release', '-consoleloggerparameters:ErrorsOnly')
if ($NoRestore) { $buildArgs += '--no-restore' }
if ($ValheimManaged) { $buildArgs += "-p:ValheimManaged=$ValheimManaged" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$dllPath = Join-Path $packageDir 'Hand_Drawn_Map.dll'
$version = [Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString(3)
if ($version -ne $manifest.version_number) { throw 'DLL and manifest versions differ.' }
$dllText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($dllPath))
if ($dllText.Contains('SurveyKey')) { throw 'Debug survey input is present in the Release DLL.' }
if (-not (Get-Content -LiteralPath (Join-Path $packageDir 'README.md') -Raw).Contains("v $version")) {
    throw 'Public README version differs.'
}

Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Image]::FromFile((Join-Path $packageDir 'icon.png'))
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256 -or $icon.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw 'Package icon must be a 256x256 PNG.'
    }
} finally { $icon.Dispose() }

$files = [ordered]@{
    'manifest.json' = Join-Path $packageDir 'manifest.json'
    'README.md' = Join-Path $packageDir 'README.md'
    'icon.png' = Join-Path $packageDir 'icon.png'
    'LICENSE' = Join-Path $PSScriptRoot '../../LICENSE'
    'Hand_Drawn_Map.dll' = $dllPath
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $packageDir ("{0}_Thunderstore_{1}.zip" -f $manifest.name, $version.Replace('.', ''))
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $files.Keys) {
        $path = $files[$name]
        if ((Get-Item -LiteralPath $path).Length -eq 0) { throw "Empty package file: $name" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $path, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $stream.Dispose() }

$check = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($check.Entries.Count -ne $files.Count) { throw 'Unexpected package entries.' }
    foreach ($name in $files.Keys) {
        $entry = $check.GetEntry($name)
        if ($null -eq $entry -or $entry.Length -eq 0) { throw "Missing package entry: $name" }
        $entryStream = $entry.Open()
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($hash.ComputeHash($entryStream)).Replace('-', '') }
        finally { $hash.Dispose(); $entryStream.Dispose() }
        if ($entryHash -ne (Get-FileHash -LiteralPath $files[$name] -Algorithm SHA256).Hash) {
            throw "Package content mismatch: $name"
        }
    }
} finally { $check.Dispose() }
Write-Output "Validated Release package: $zipPath"
