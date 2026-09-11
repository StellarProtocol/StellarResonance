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
4. In the launcher: it lists every game install it found — tick the ones you want as **clients**, click
   **Add**, then **Launch** any of them. Each client keeps its own framework, plugins and settings and they
   can run at the same time. (Not found? **Browse…** to your `game_mini` folder.)

### Linux

1. Download **`StellarLauncher-linux-x64.zip`** from the latest release.
2. Extract it and make the binary executable:

   ```bash
   unzip StellarLauncher-linux-x64.zip -d StellarResonance
   cd StellarResonance
   chmod +x StellarLauncher.App
   ./StellarLauncher.App
   ```

   Optionally run **`./install.sh`** to add a desktop entry — this puts "Stellar Launcher" in your
   application menu/dock with its icon (Linux executables can't embed an icon like a Windows `.exe`,
   so a bare binary file shows a generic icon in the file manager). Undo with `./uninstall.sh`.

3. In the launcher: it lists every game install it found (Wine/Proton prefixes under `/opt/game`, Heroic,
   Steam) — tick the ones you want as **clients**, click **Add**, then **Launch** any of them. Each client
   keeps its own framework, plugins and settings and they can run at the same time. (Not found? **Browse…**
   to the prefix's `…/drive_c/Star/StarLauncher/game/release_<ver>/game_mini/`.)
   Set `WINEDLLOVERRIDES=winhttp=n,b` in your game launcher's per-game env vars if you launch the game
   outside this tool.

> The launcher only **manages an existing game install** — it ships no game code or assets, and does the
> BepInEx + framework setup for you so you don't have to do the [manual steps](https://github.com/StellarProtocol/StellarResonanceModSystem/blob/main/docs/getting-started.md).

## Build (from source)

```bash
dotnet build StellarLauncher.slnx -c Release
```

## Releasing (maintainers)

Fully CI/CD — the launcher has no game dependency, so `ci.yml` builds it and `release.yml` ships it:

```bash
gh workflow run release.yml -R StellarProtocol/StellarResonance -f version=2.0.0 -f channel=testing
```

2.0.0 changes the settings file to v2 (one client per game install; the old file is imported one-way and kept
as `settings.v1.json` beside it) — publish to `testing` first, then to `stable` once the testers confirm.

The dispatch `version` is the single source of truth — it's stamped into the assembly (the in-app
`LauncherVersion` reads it back), so no code edit is needed to bump the version. CI builds Win + Linux,
uploads the zips + `launcher.json` to the `stellar` MinIO bucket, and creates the GitHub release. Use
`-f channel=testing` to publish to `launcher-testing.json` without affecting stable users. See the
DevKit's `docs/release-process.md` for how this fits with the framework + plugin releases.

## License

[GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0). Free, open-source software; any
distributed or network-deployed derivative must also be released in full under AGPL-3.0.
