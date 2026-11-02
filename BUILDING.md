# Building Reef UI

Use your own Sunkenland Mono installation and an installed BepInEx 5.4.23.6 loader. The repository does not include game, Unity, BepInEx, HarmonyX or Mono.Cecil assemblies.

Install a .NET SDK that supports `netstandard2.1` and use PowerShell 5.1 or later for the release script. Source files live in `src/`; the two authored PNGs live in `Assets/`.

## Build and package

Run from the repository root. Replace the example path with your Steam installation:

```powershell
$gameDir = 'D:\Games\SteamLibrary\steamapps\common\Sunkenland'
./scripts/Build-Release.ps1 -GameDir $gameDir
```

The script builds Release, runs the static release verifier and writes `dist/Sunkenland.ReefUI-0.9.1.zip`. It does not start or modify the game or install the mod. The ZIP includes exactly:

```text
BepInEx/plugins/ReefUI/Sunkenland.ReefUI.dll
BepInEx/plugins/ReefUI/Assets/reef-panel-v7.png
BepInEx/plugins/ReefUI/Assets/reef-surface-v7.png
BepInEx/plugins/ReefUI/Assets/ARTWORK.md
LICENSE
NOTICE.md
INSTALL.md
```

Build-output references, game files, loader files, configuration, logs, PDBs and dependency metadata are excluded by an explicit archive-entry allowlist. A previous ZIP of the same version is replaced only after the new ZIP passes that check.

For an explicitly unverified local package, add `-SkipVerify`. A skipped verifier is reported as `Verified = False`; it is not equivalent to a verified release. Static verification does not measure in-game appearance or FPS.

## Build without packaging

```powershell
dotnet build ./src/ReefUI.csproj -c Release "-p:GameDir=$gameDir"
```

Or supply the installation through the environment:

```powershell
$env:SUNKENLAND_DIR = $gameDir
dotnet build ./src/ReefUI.csproj -c Release
```

The project reads game references from `GameDir/Sunkenland_Data/Managed` and loader references from `GameDir/BepInEx/core`. It copies only the mod's two PNGs alongside its build output. All assembly references have `Private=false`. Deterministic compilation, disabled debug symbols and a source path map keep local source paths out of the release's debug metadata.

For a nonstandard installation, override the paths explicitly:

```powershell
dotnet build ./src/ReefUI.csproj -c Release `
  "-p:GameManaged=D:\LocalReferences\SunkenlandManaged" `
  "-p:BepInExDir=D:\LocalReferences\BepInEx"
```

`GameManaged` points directly to the managed-assembly directory. `BepInExDir` points to the directory containing `core`, rather than to `core` itself. Keep all references from a compatible installation.

The release script also accepts `-GameManaged`, `-BepInExDir` and `-OutputDirectory`. Its `-GameDir` is still required for verification against that game's native assembly. `SUNKENLAND_DIR` supplies the default when `-GameDir` is omitted.

## Run verification separately

```powershell
./scripts/Verify-Release.ps1 `
  -GameDir $gameDir `
  -BepInExDir (Join-Path $gameDir 'BepInEx') `
  -SourceDir ./src `
  -ModPath ./src/bin/Release/netstandard2.1/Sunkenland.ReefUI.dll
```

The verifier reads local assembly metadata without launching the game. It requires the `Mono.Cecil.dll` supplied by the installed BepInEx loader. Follow [INSTALL.md](INSTALL.md) to install the completed package.
