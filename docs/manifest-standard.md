# Stellar manifest standard

The launcher reads three kinds of JSON manifest from the `stellar` bucket. This document is the
**contract** every producer (the launcher's own release workflow, the StellarResonanceModSystem
release workflow, and the plugin registry) must follow.

All version strings are dotted semver-ish (`MAJOR.MINOR.PATCH`, optional leading `v`). Comparison
is numeric per component (`System.Version`). Dates are `YYYY-MM-DD` (UTC).

Channels: a `…-testing.json` sibling mirrors each manifest for the `testing` channel. The launcher
only ever reads manifests for the channel selected in Settings; versions never cross channels.

---

## 1. Launcher self-update — `launcher.json`

Unchanged single-version pointer (the launcher always self-updates to the newest; no picker).
See `.github/workflows/release.yml`. Out of scope for version selection.

---

## 2. Framework (modsystem) — `version.json` / `version-testing.json`

Carries the **full release history**, newest first, so the launcher can offer a version picker.

```json
{
  "latest": "1.4.0",
  "channel": "stable",
  "versions": [
    {
      "version": "1.4.0",
      "date": "2026-06-10",
      "bundleUrl": "https://minio.revette.io/stellar/framework/StellarResonance-1.4.0.zip",
      "sha256": "<hex sha256 of the bundle zip>",
      "minLauncherVersion": "1.0.0",
      "changelog": {
        "added":   ["…"],
        "changed": ["…"],
        "fixed":   ["…"],
        "removed": ["…"]
      }
    },
    { "version": "1.3.0", "...": "…" }
  ]
}
```

- `latest` — the version the launcher pre-selects in the picker. Must equal `versions[0].version`.
- `versions[]` — **every** published release, newest first. Never drop entries (history must be
  stable so a user can roll back). Each entry is immutable once published.
- `bundleUrl` — **version-specific** (the version appears in the object name). This replaces the old
  fixed-name "current-only" scheme: history requires every release's bundle to remain downloadable.
- `minLauncherVersion` — the launcher refuses to install a release that needs a newer launcher,
  prompting a self-update first.
- `changelog` — any of the four arrays may be empty/omitted.

## 3. Plugins — `plugins.json` (+ extra user repos)

Each plugin carries its own version history, and **each version declares the modsystem range it
runs on** so the launcher can gate it against the installed/selected framework.

```json
{
  "plugins": [
    {
      "id": "com.example.party-overlay",
      "name": "Party Overlay",
      "description": "Raid party roster overlay.",
      "author": "example",
      "versions": [
        {
          "version": "2.1.0",
          "date": "2026-06-09",
          "dll": "Stellar.PartyOverlay.dll",
          "dllUrl": "https://minio.revette.io/stellar/plugins/party-overlay/Stellar.PartyOverlay-2.1.0.dll",
          "sha256": "<hex sha256 of the dll>",
          "minModSystemVersion": "1.4.0",
          "maxModSystemVersion": null,
          "changelog": { "added": ["…"], "changed": [], "fixed": ["…"], "removed": [] }
        },
        { "version": "2.0.0", "...": "…" }
      ]
    }
  ]
}
```

- `versions[]` — newest first; `versions[0]` is the default selection.
- `dll` — the **canonical** on-disk filename (the plugin's assembly name, e.g. `Stellar.PartyOverlay.dll`).
  The launcher installs the download under this name (not the version-suffixed `dllUrl` name) so it
  overwrites any prior copy and the framework — which loads every `*.dll` under `stellar/plugins` by
  assembly name — loads exactly one. The launcher also uses it to detect/adopt existing installs.
- `dllUrl` — version-specific object name (keeps old versions downloadable).
- `minModSystemVersion` — **required**. The lowest framework version this plugin build runs on.
- `maxModSystemVersion` — optional (`null` = no upper bound). Set it when a later framework release
  breaks the plugin, so the launcher steers users to a newer plugin build instead.

### Compatibility rule

A plugin version `P` is **compatible** with installed framework version `F` iff:

```
F >= P.minModSystemVersion  AND  (P.maxModSystemVersion is null  OR  F <= P.maxModSystemVersion)
```

Launcher behaviour:
- Compatible versions are selectable.
- A version needing a newer framework than installed is shown but disabled, with
  "requires StellarResonance ≥ {min}" — the user updates the framework first.
- A version capped below the installed framework shows "needs StellarResonance ≤ {max}".

---

## Version selection UX (launcher)

For both the framework and each plugin:
1. A version dropdown, defaulting to `latest`/newest.
2. A changelog panel for the **selected** version.
3. A confirm step before any change, which restates the version + changelog and warns when the
   action is a **downgrade** (older than installed) or **incompatible**.
