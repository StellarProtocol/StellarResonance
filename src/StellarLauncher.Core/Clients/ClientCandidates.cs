using System;
using System.Collections.Generic;
using System.Linq;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

namespace StellarLauncher.Core.Clients;

public sealed record ClientCandidate(
    string GameMiniDir, string ProposedName, ClientLayout Layout,
    bool IsWinePrefix, string? WinePrefix, string? Runner);

/// <summary>Detected installs minus configured clients minus dismissed paths (spec § 10).</summary>
public sealed class ClientCandidates
{
    private readonly IGameDetector _detector;
    private readonly IPlatformInfo _platform;

    public ClientCandidates(IGameDetector detector, IPlatformInfo platform)
    {
        _detector = detector; _platform = platform;
    }

    public IReadOnlyList<ClientCandidate> Find(LauncherConfig cfg)
    {
        var cmp = Comparer();
        var configured = new HashSet<string>(cfg.Clients.Select(c => NormalizePath(c.GameMiniDir)), cmp);
        var dismissed = new HashSet<string>(cfg.Launcher.DismissedDetections.Select(NormalizePath), cmp);
        var taken = new HashSet<string>(cfg.Clients.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        string? runner = null; var runnerProbed = false;

        var result = new List<ClientCandidate>();
        foreach (var path in _detector.Detect())
        {
            var norm = NormalizePath(path);
            if (configured.Contains(norm) || dismissed.Contains(norm)) continue;

            var wine = _platform.IsWindows ? null : GameDetector.WinePrefixFor(path);
            if (wine is not null && !runnerProbed) { runner = _detector.DetectRunner(); runnerProbed = true; }

            result.Add(new ClientCandidate(path, UniqueName(ClientNaming.ProposeName(path), taken),
                ClientNaming.DetectLayout(path), wine is not null, wine, wine is not null ? runner : null));
        }
        return result;
    }

    public ClientProfile? ConfiguredFor(LauncherConfig cfg, string path) =>
        cfg.Clients.FirstOrDefault(c => SamePath(c.GameMiniDir, path, _platform.IsWindows));

    public ClientProfile ToProfile(ClientCandidate c, LauncherConfig cfg) => new()
    {
        Id = ClientIds.New(),
        Name = c.ProposedName,
        Accent = AccentPalette.Next(cfg.Clients.Select(x => x.Accent)),
        GameMiniDir = c.GameMiniDir,
        Linux = c.IsWinePrefix ? new LinuxRuntime { Runner = c.Runner, WinePrefix = c.WinePrefix } : null,
    };

    public static string NormalizePath(string p) => p.Replace('\\', '/').TrimEnd('/');

    public static bool SamePath(string a, string b, bool isWindows) =>
        string.Equals(NormalizePath(a), NormalizePath(b),
            isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private StringComparer Comparer() => _platform.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string UniqueName(string proposed, HashSet<string> taken)
    {
        var name = proposed;
        for (var n = 2; taken.Contains(name); n++) name = $"{proposed} {n}";
        taken.Add(name);
        return name;
    }
}
