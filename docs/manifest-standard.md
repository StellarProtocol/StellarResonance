# Stellar manifest standard

The launcher reads three kinds of JSON manifest from the `stellar` bucket. This document is the
**contract** every producer (the launcher's own release workflow, the StellarResonanceModSystem
release workflow, and the plugin registry) must follow.

All version strings are dotted semver-ish (`MAJOR.MINOR.PATCH`, optional leading `v`). Comparison
is numeric per component (`System.Version`). Dates are `YYYY-MM-DD` (UTC).

Channels: each producer emits a `…-testing.json` sibling for the `testing` channel alongside the
stable file. The launcher only ever reads the file for the channel selected in Settings. For the
**launcher** and **framework**, the testing file is a parallel history. For **plugins**, the testing
file is a **superset**: `plugins-testing.json` carries *every* version of *every* plugin (stable +
testing), while `plugins.json` carries only the stable versions — so a single plugin can offer a
beta and a proven release at once on the testing channel. How the two plugin files are *produced*
from source manifests is in § 3.1.

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
      "bundleUrl": "https://cdn.revette.io/framework/StellarResonance-1.4.0.zip",
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
          "dllUrl": "https://cdn.revette.io/plugins/party-overlay/Stellar.PartyOverlay-2.1.0.dll",
          "sha256": "<hex sha256 of the dll>",
          "minModSystemVersion": "1.4.0",
          "maxModSystemVersion": null,
          "sourceRepository": "https://github.com/StellarProtocol/StellarPartyOverlayPlugin.git",
          "sourceCommit": "<full 40-char sha CI built>",
          "sourceTag": "v2.1.0",
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
- `sourceRepository` / `sourceCommit` — **provenance** for curated plugins built by CI from a pinned
  public repo (DIP17 model): the repo and the exact commit the binary was built from. `sourceCommit`
  is authoritative.
- `sourceTag` — optional, **display-only** provenance. CI verifies the tag resolves to `sourceCommit`
  but never builds from a tag alone (tags are mutable; the pinned commit is not).

### 3.1 Source registry (how `plugins.json` is produced)

The published files above are **built by CI** (`StellarResonancePlugins/tools/build-registry.py`) from
**source manifests** in that repo — they are not hand-edited. The registry holds **manifests only**; each
plugin's binary is built from its own pinned public repo in an isolated container. The source is a
**two-file-per-plugin** model so one plugin can be live on stable **and** testing simultaneously:

**`plugins/<id>/manifest.json`** — canonical record (shared fields + one version):

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | ✓ | unique plugin id; URL-safe (no `/` or `..`) |
| `name` | string | ✓ | display name |
| `description` | string | ✓ | one-line summary |
| `author` | string | ✓ | author handle |
| `dll` | string | ✓ | canonical assembly filename, e.g. `Stellar.X.dll` (the install name) |
| `repository` | string (git URL) | ✓¹ | public repo CI clones + builds |
| `commit` | string (40-hex) | ✓² | **authoritative** pinned commit CI builds + attests |
| `tag` | string | — | display-only provenance; CI verifies `tag` → `commit`, never builds from it |
| `projectPath` | string | — | path within the repo to build (default `"."`) |
| `version` | string (semver) | ✓ | this build's version |
| `minModSystemVersion` | string (semver) | ✓ | lowest framework version this build runs on |
| `maxModSystemVersion` | string \| null | — | upper bound; `null`/omitted = none |
| `capPriorVersionsAt` | string (semver) | — | retro-cap already-published versions' `maxModSystemVersion` at this framework version |
| `channel` | `"stable"` \| `"testing"` | — | channel of **this** version (default `"stable"`; `"testing"` = the plugin is testing-**only**) |
| `date` | string `YYYY-MM-DD` | — | release date (UTC) |
| `changelog` | object | — | `{ added?, changed?, fixed?, removed? }` — arrays of strings |

¹ Required by the curated registry (CI refuses a manifest without a pinned public repo). ² Required whenever `repository` is set.

**`plugins/<id>/manifest.testing.json`** — *optional* sibling: a second, **testing-channel** build. It
**inherits** the shared fields from `manifest.json` and may set **only** the version-specific fields
below; any other key is rejected (so shared metadata lives in one place and cannot drift):

| Field | Type | Required | Notes |
|---|---|---|---|
| `version` | string (semver) | ✓ | the testing build's version, e.g. `1.2.0-beta` |
| `commit` | string (40-hex) | ✓ | the testing build's pinned commit |
| `minModSystemVersion` | string (semver) | ✓ | framework floor for this build |
| `tag` | string | — | display-only; CI verifies `tag` → `commit` |
| `date` | string `YYYY-MM-DD` | — | release date |
| `maxModSystemVersion` | string \| null | — | upper bound |
| `capPriorVersionsAt` | string (semver) | — | retro-cap prior published versions |
| `changelog` | object | — | as above |

Inherited from `manifest.json` (do **not** repeat): `id`, `name`, `description`, `author`, `dll`,
`repository`, `projectPath`.

`build-registry.py` merges each plugin's current version(s) into the **published history** (newest
first, old versions never dropped — rollback), splits by channel (stable → `plugins.json`; all →
`plugins-testing.json`), and uploads each DLL under a version-specific key. `capPriorVersionsAt`
retro-caps older published versions' `maxModSystemVersion` (when still null) at the named framework
version. See `StellarResonancePlugins/CONTRIBUTING.md` for the contributor flow and lifecycle scripts.

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
