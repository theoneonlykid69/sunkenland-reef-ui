[CmdletBinding()]
param(
    [string]$GameDir = $env:SUNKENLAND_DIR,
    [string]$GameManaged,
    [string]$BepInExDir,
    [string]$OutputDirectory,
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RequiredDirectory([string]$Path, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Path) -or !(Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label must name an existing directory."
    }
    return (Resolve-Path -LiteralPath $Path).ProviderPath
}

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceDir = Join-Path $repo 'src'
$project = Join-Path $sourceDir 'ReefUI.csproj'
$verifyScript = Join-Path $PSScriptRoot 'Verify-Release.ps1'
if (!(Test-Path -LiteralPath $project -PathType Leaf)) { throw "Project is missing: $project" }
if (!$SkipVerify -and !(Test-Path -LiteralPath $verifyScript -PathType Leaf)) {
    throw 'scripts/Verify-Release.ps1 is required. Use -SkipVerify only for an explicitly unverified local package.'
}
if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw 'Supply -GameDir or set SUNKENLAND_DIR to your Sunkenland installation.'
}
$resolvedGame = Resolve-RequiredDirectory $GameDir 'GameDir'
if ([string]::IsNullOrWhiteSpace($GameManaged)) { $GameManaged = Join-Path $resolvedGame 'Sunkenland_Data/Managed' }
if ([string]::IsNullOrWhiteSpace($BepInExDir)) { $BepInExDir = Join-Path $resolvedGame 'BepInEx' }
$resolvedManaged = Resolve-RequiredDirectory $GameManaged 'GameManaged'
$resolvedBepInEx = Resolve-RequiredDirectory $BepInExDir 'BepInExDir'

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if (!$versionNode) { throw 'The project must declare a release Version.' }
$version = $versionNode.InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project must declare a three-part release Version.' }
$archiveName = "Sunkenland.ReefUI-$version.zip"
$modPath = Join-Path $sourceDir 'bin/Release/netstandard2.1/Sunkenland.ReefUI.dll'

# These exact mappings are the complete release boundary. No directory
# recursion, game assemblies, loader DLLs, configuration or logs are copied.
$files = @(
    [pscustomobject]@{ Source = $modPath; Entry = 'BepInEx/plugins/ReefUI/Sunkenland.ReefUI.dll' },
    [pscustomobject]@{ Source = (Join-Path $repo 'Assets/reef-panel-v7.png'); Entry = 'BepInEx/plugins/ReefUI/Assets/reef-panel-v7.png' },
    [pscustomobject]@{ Source = (Join-Path $repo 'Assets/reef-surface-v7.png'); Entry = 'BepInEx/plugins/ReefUI/Assets/reef-surface-v7.png' },
    [pscustomobject]@{ Source = (Join-Path $repo 'Assets/ARTWORK.md'); Entry = 'BepInEx/plugins/ReefUI/Assets/ARTWORK.md' },
    [pscustomobject]@{ Source = (Join-Path $repo 'LICENSE'); Entry = 'LICENSE' },
    [pscustomobject]@{ Source = (Join-Path $repo 'NOTICE.md'); Entry = 'NOTICE.md' },
    [pscustomobject]@{ Source = (Join-Path $repo 'INSTALL.md'); Entry = 'INSTALL.md' }
)
foreach ($file in ($files | Select-Object -Skip 1)) {
    if (!(Test-Path -LiteralPath $file.Source -PathType Leaf)) { throw "Required release input is missing: $($file.Source)" }
}

$dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
$buildArgs = @('build', $project, '-c', 'Release', "-p:GameDir=$resolvedGame", "-p:GameManaged=$resolvedManaged", "-p:BepInExDir=$resolvedBepInEx")
& $dotnet.Source @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }
if (!(Test-Path -LiteralPath $modPath -PathType Leaf)) { throw 'The release DLL was not produced.' }
$identity = [System.Reflection.AssemblyName]::GetAssemblyName($modPath)
if ($identity.Name -ne 'Sunkenland.ReefUI' -or $identity.Version.ToString(3) -ne $version) {
    throw 'Built assembly identity does not match the release project.'
}
if (!$SkipVerify) {
    & $verifyScript -GameDir $resolvedGame -BepInExDir $resolvedBepInEx -ModPath $modPath -SourceDir $sourceDir
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repo 'dist' }
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
[void][System.IO.Directory]::CreateDirectory($outputRoot)
$archivePath = Join-Path $outputRoot $archiveName
$temporaryZip = Join-Path $outputRoot ('.' + $archiveName + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $stream = [System.IO.File]::Open($temporaryZip, [System.IO.FileMode]::CreateNew)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in $files) {
                [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $file.Source, $file.Entry, [System.IO.Compression.CompressionLevel]::Optimal)
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
    $check = [System.IO.Compression.ZipFile]::OpenRead($temporaryZip)
    try {
        $actual = @($check.Entries | ForEach-Object { $_.FullName } | Sort-Object)
        $expected = @($files.Entry | Sort-Object)
        if ($actual.Count -ne $expected.Count -or ($actual -join "`n") -cne ($expected -join "`n")) {
            throw 'The ZIP contains entries outside the exact release allowlist.'
        }
    }
    finally { $check.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $archivePath -Force
}
finally {
    # One exact file created above; no recursive cleanup is performed.
    if (Test-Path -LiteralPath $temporaryZip -PathType Leaf) { Remove-Item -LiteralPath $temporaryZip -Force }
}

[pscustomobject]@{
    Version = $version
    Archive = $archivePath
    Files = $files.Count
    Verified = !$SkipVerify
    SHA256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
}
