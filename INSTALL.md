# Install ReefUI 0.9.1

ReefUI's release ZIP contains the mod and its artwork. Install BepInEx separately first. The current baseline is Sunkenland Beta 0.9.35 on Windows x64, using Unity Mono and BepInEx 5.4.23.6.

## 1. Install BepInEx 5

1. Close Sunkenland normally.
2. Open Sunkenland's local installation folder from Steam. This is the folder containing `Sunkenland.exe` and `Sunkenland_Data`.
3. Download the **Windows x64 BepInEx 5.4.23.6** package from the [official BepInEx release page](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6). Use the Unity Mono loader for this installation.
4. Extract that package into the game folder, preserving its folder structure.
5. Start Sunkenland through Steam once, then close it. This creates BepInEx's configuration and log files.
6. Open `BepInEx/config/BepInEx.cfg` in a text editor. Under `[Chainloader]`, set:

```ini
HideManagerGameObject = true
```

Keep other existing settings unless you have a separate reason to change them. This loader setting is part of ReefUI's tested Sunkenland setup.

## 2. Install ReefUI

Download ReefUI's **mod-only release ZIP** from this repository's Releases page. With the game closed, extract its contents into the game folder, preserving the paths in the archive.

The resulting layout must include:

```text
Sunkenland/
├── Sunkenland.exe
├── Sunkenland_Data/
└── BepInEx/
    ├── config/
    │   └── BepInEx.cfg
    └── plugins/
        └── ReefUI/
            ├── Sunkenland.ReefUI.dll
            └── Assets/
                ├── reef-panel-v7.png
                └── reef-surface-v7.png
```

The `v7` artwork filenames are intentional; version 0.9.1 reuses those materials. Keep the `Assets` folder beside the DLL. A source-code archive is not the installable mod ZIP.

Start Sunkenland through Steam. There is no in-game toggle to enable: ReefUI applies automatically.

## 3. Check that it loaded

Open `BepInEx/LogOutput.log` after starting the game. Look for:

```text
Loading [Reef UI 0.9.1]
Registered 48 UI creation/open hooks; no recurring hierarchy scan.
Reef UI 0.9.1 loaded.
```

The lines may contain extra prefixes or text. The version should match the release you installed. Startup messages confirm loading; checking the affected screens and gameplay confirms how the mod behaves on your setup.

## Update or roll back

Close the game before replacing files. Keep a copy of the installed `BepInEx/plugins/ReefUI` folder if you want an easy rollback. Replace the DLL and artwork with the files from the chosen release, then restart through Steam.

Keep only one active copy of `Sunkenland.ReefUI.dll` under `BepInEx/plugins`. Store backups outside that folder so BepInEx does not try to load both versions. To roll back, restore the complete saved ReefUI folder while the game is closed.

## Remove ReefUI

Close the game, then move `BepInEx/plugins/ReefUI` out of the plugins folder or delete that ReefUI folder. This removes ReefUI's DLL and artwork. Other BepInEx plugins remain installed. Start the game again to return to its native UI.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No `BepInEx/LogOutput.log` appears | Check the loader package and that its files were extracted beside `Sunkenland.exe`. Start through Steam. |
| BepInEx loads, but ReefUI does not | Check the DLL path above and look for a plugin loading error in the log. |
| Game reports another instance is running | Close the existing game normally and wait for Steam to show it has stopped. Launch once through Steam; avoid starting a second executable alongside it. |
| Metal artwork is missing or surfaces look plain | Check that both PNGs are in the `Assets` folder beside the DLL. |
| The log shows an older ReefUI version | Close the game, check for duplicate DLLs, replace the installed release files, and restart. |
| The game was updated and a screen breaks | Report the new game version, affected screen, and relevant log messages. Compatibility is currently confirmed only for the baseline above. |
| Frames drop or UI elements overlap | Record the affected screen and compare with ReefUI removed after restarting. Include the result and relevant log section in an issue. |

The author has supplied interface screenshots from 0.9.0. Check the starting-background selection after installing 0.9.1. A successful startup message is not a performance benchmark.
