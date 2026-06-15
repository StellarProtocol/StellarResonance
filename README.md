<p align="center">
  <img src=".github/logo.png" alt="StellarResonance" width="160">
</p>

# StellarResonance Launcher

Cross-platform GUI launcher for the **StellarResonance** mod framework — the delivery tool
for [`StellarResonanceModSystem`](https://github.com/StellarProtocol/StellarResonanceModSystem),
in the spirit of XIVLauncher to Dalamud.

Detects the game, installs/updates the framework, toggles vanilla⇄modded, and launches the
game on Windows and Linux. Couples to the framework only through its published GitHub Release
artifacts (`version.json` + bundle zip).

> **Not affiliated with, endorsed by, or connected to** the game's publisher or developer. This
> launcher ships **no game code or assets**; it only detects an existing install and manages the
> open-source framework. Use at your own risk under the game's Terms of Service.

## Install

Grab the latest build from the [**Releases**](https://github.com/StellarProtocol/StellarResonance/releases/latest)
page. The launcher is **self-contained** — you do **not** need .NET installed. After first launch it
keeps itself up to date automatically.

### Windows

1. Download **`StellarLauncher-win-x64.zip`** from the latest release.
2. Right-click → **Extract All** to a folder you control (e.g. `C:\Tools\StellarResonance`). Don't run it
   from inside the zip.
3. Run **`StellarLauncher.App.exe`**.
   - If Windows SmartScreen warns ("Windows protected your PC"), click **More info → Run anyway** — the
     app is unsigned, not malicious.
4. In the launcher: let it **detect your game** (or point it at your `game_mini` folder), click **Install**
   to deploy the framework, then **Launch**.

### Linux

1. Download **`StellarLauncher-linux-x64.zip`** from the latest release.
2. Extract it and make the binary executable:

   ```bash
   unzip StellarLauncher-linux-x64.zip -d StellarResonance
   cd StellarResonance
   chmod +x StellarLauncher.App
   ./StellarLauncher.App
   ```

3. In the launcher: detect the game (or point it at your Wine/Proton prefix's
   `…/drive_c/Star/StarLauncher/game/release_<ver>/game_mini/`), **Install** the framework, then **Launch**.
   Set `WINEDLLOVERRIDES=winhttp=n,b` in your game launcher's per-game env vars if you launch the game
   outside this tool.

> The launcher only **manages an existing game install** — it ships no game code or assets, and does the
> BepInEx + framework setup for you so you don't have to do the [manual steps](https://github.com/StellarProtocol/StellarResonanceModSystem/blob/main/docs/getting-started.md).

## Build (from source)

```bash
dotnet build StellarLauncher.slnx -c Release
```

## License

[GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0). Free, open-source software; any
distributed or network-deployed derivative must also be released in full under AGPL-3.0.
