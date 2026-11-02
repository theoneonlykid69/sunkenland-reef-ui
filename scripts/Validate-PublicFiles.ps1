param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$Root = [System.IO.Path]::GetFullPath($Root)
$allowed = @(
    '^\.(gitignore|gitattributes)$',
    '^(LICENSE|NOTICE\.md|THIRD_PARTY\.md|README\.md|INSTALL\.md|BUILDING\.md|CHANGELOG\.md|RELEASE-NOTES-0\.9\.1\.md)$',
    '^src/(BuilderPolish|BuildingOrganization|DesignAssets|DialogStyle|EventStyling|GlobalTheme|HudStyle|InventoryStyle|MenuStyle|OriginSelectionStyle|ReefUI|ScreenTreatments|SlotSurfaces)\.cs$',
    '^src/ReefUI\.csproj$',
    '^Assets/(reef-panel-v7\.png|reef-surface-v7\.png|ARTWORK\.md)$',
    '^scripts/(Build-Release|Verify-Release|Validate-PublicFiles)\.ps1$',
    '^docs/SCREENSHOTS\.md$',
    '^docs/images/(main-menu|settings|character-selection|inventory|building|tutorial|origins)\.png$',
    '^\.github/ISSUE_TEMPLATE/(bug_report|config)\.yml$'
)
$problems = [System.Collections.Generic.List[string]]::new()
$files = [System.Collections.Generic.List[string]]::new()
foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File -Force) {
    $relative = $file.FullName.Substring($Root.TrimEnd('\','/').Length + 1).Replace('\','/')
    if ($relative -match '^(\.git/|src/(bin|obj)/|dist/|artifacts/)') { continue }
    $accepted = $false
    foreach ($pattern in $allowed) { if ($relative -match $pattern) { $accepted = $true; break } }
    if (!$accepted) { $problems.Add("Unexpected public file: $relative"); continue }
    $files.Add($relative)
    if ($file.Extension -eq '.png') { continue }
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($pattern in @('C:[\\/]Users[\\/]','E:[\\/]SteamLibrary[\\/]',
        'gh[pousr]_[A-Za-z0-9]{20,}','github_pat_[A-Za-z0-9_]{20,}','sk-[A-Za-z0-9]{24,}',
        '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----')) {
        if ($text -match $pattern) { $problems.Add("Private path or credential pattern in $relative") }
    }
}
if (@($files | Where-Object { $_ -match '^src/.+\.cs$' }).Count -ne 13) { $problems.Add('Expected the 13 original mod source files.') }
foreach ($required in @('LICENSE','NOTICE.md','README.md','INSTALL.md','src/ReefUI.csproj','Assets/reef-panel-v7.png','Assets/reef-surface-v7.png')) {
    if (!$files.Contains($required)) { $problems.Add("Required public file missing: $required") }
}
if ($problems.Count) { throw ($problems -join "`n") }
[pscustomobject]@{ PublicFiles = $files.Count; UnexpectedFiles = 0; CredentialOrPrivatePathMatches = 0; SourceFiles = 13 }
