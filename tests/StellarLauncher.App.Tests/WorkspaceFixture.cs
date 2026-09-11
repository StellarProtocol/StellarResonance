using System.Diagnostics;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels.Dashboard;
using StellarLauncher.App.ViewModels.Shell;
using StellarLauncher.App.ViewModels.Workspace;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Launch;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Platform;
using StellarLauncher.Core.Services;

/// <summary>Builds a shell with N clients on a MockFileSystem, a fake registry and an HTTP handler that serves
/// every plugin DLL as the same bytes (so sha256 checks pass with <see cref="DllSha"/>).</summary>
public sealed class WorkspaceFixture
{
    public static readonly byte[] DllBytes = Encoding.UTF8.GetBytes("dll-bytes");
    public static readonly string DllSha = Convert.ToHexString(SHA256.HashData(DllBytes)).ToLowerInvariant();

    public MockFileSystem Fs { get; } = new();
    public ConfigStore Store { get; }
    public ShellViewModel Shell { get; private set; } = null!;
    public List<PluginEntry> Registry { get; } = new();
    public WorkspaceTabFactories Tabs { get; set; }
    /// <summary>Framework manifest source; swap for <c>new ManifestVersions("99.0.0")</c> to simulate a too-old launcher.</summary>
    public IVersionService Manifests { get; set; } = new ManifestVersions();

    private sealed class Platform : IPlatformInfo { public bool IsWindows => false; public string AppDataDir => "/cfg"; }
    private sealed class Reg(List<PluginEntry> entries) : IPluginRegistryService
    { public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> u, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PluginEntry>>(entries.ToList()); }
    // minLauncher "0.0.0" by default: dev builds report AppInfo.LauncherVersion = "0.0.0", so the manifest must not demand more.
    public sealed class ManifestVersions(string minLauncher = "0.0.0") : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default) => Task.FromResult(new FrameworkManifest("2.8.0", "stable", new[]
        {
            new VersionManifest("2.8.0", "2026-09-09", "https://cdn/b.zip", "sha", minLauncher, new Changelog(new[] { "Meter value text style" }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>())),
            new VersionManifest("2.7.4", "2026-09-09", "https://cdn/a.zip", "sha", minLauncher, new Changelog(Array.Empty<string>(), Array.Empty<string>(), new[] { "Steam boot crash" }, Array.Empty<string>())),
        }));
    }
    public sealed class Detector : IGameDetector { public List<string> Found = new(); public IReadOnlyList<string> Detect() => Found; public string? DetectRunner() => "/p/GE-Proton10-26/proton"; public string? DetectUmu() => null; }
    private sealed class Review : IPreLaunchReview { public Task<bool> ReviewAsync(ClientProfile c, CancellationToken ct) => Task.FromResult(true); }
    private sealed class Orch : ILaunchOrchestrator { public Task<LaunchOutcome> LaunchAsync(ClientProfile c, IProgress<LaunchEvent> e, CancellationToken ct) { e.Report(new RunningEvent()); return Task.FromResult(new LaunchOutcome(LaunchOutcomeKind.Started, null)); } }
    private sealed class NoScan : IRunningProcessScanner { public IReadOnlyList<RunningProcess> Snapshot() => Array.Empty<RunningProcess>(); }
    private sealed class NoProc : IProcessFactory { public IGameProcess? Start(ProcessStartInfo p) => null; public IGameProcess? Attach(int pid) => null; }
    private sealed class DllHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(DllBytes) });
    }
    public sealed class AutoConfirm : IConfirm { public int Asked; public Task<bool> AskAsync(string t, string b, string ok) { Asked++; return Task.FromResult(true); } }

    public Detector GameDetector { get; } = new();
    public AutoConfirm Confirm { get; } = new();
    public WorkspaceServices Services { get; private set; } = null!;
    /// <summary>Ticks registered by view-models through the fake timer; a registration lives here until its handle is disposed.</summary>
    public List<Action> Ticks { get; } = new();
    private sealed class Disposer(Action d) : IDisposable { public void Dispose() => d(); }

    public static PluginEntry Entry(string id, string name, string min, params string[] versions)
        => new(id, name, $"{name} description", "StellarProtocol",
            versions.Select(v => new PluginVersion(v, null, $"Stellar.{name.Replace(" ", "")}.dll", $"https://cdn/{id}/{v}.dll", DllSha, min, null, null)).ToList());

    public WorkspaceFixture()
    {
        Store = new ConfigStore(Fs, new Platform());
        Tabs = new WorkspaceTabFactories(w => new OverviewViewModel(w), w => new TabPlaceholder("plugins"), w => new TabPlaceholder("settings"), w => new TabPlaceholder("logs"));
    }

    public ClientProfile AddClient(string id, string name, string gameMini, string channel = "stable", string? framework = "2.7.4", string accent = "#37c8e0")
    {
        if (framework is not null) Fs.AddFile($"{gameMini}/BepInEx/plugins/Stellar.Framework/.stellar-version", new MockFileData(framework));
        Fs.AddFile($"{gameMini}/doorstop_config.ini", new MockFileData("[General]\nenabled = true\n"));
        var cfg = Store.Load();
        var c = new ClientProfile { Id = id, Name = name, GameMiniDir = gameMini, Accent = accent, Channel = channel, Linux = new LinuxRuntime { Runner = "/p/GE-Proton10-26/proton", WinePrefix = gameMini[..gameMini.IndexOf("/drive_c")] } };
        cfg.Clients.Add(c); Store.Save(cfg);
        return c;
    }

    public void InstallPlugin(string gameMini, string id, string dll, string version, bool disabled = false)
    {
        var root = disabled ? "plugins-disabled" : "plugins";
        Fs.AddFile($"{gameMini}/stellar/{root}/{id}/{dll}", new MockFileData("x"));
        Fs.AddFile($"{gameMini}/stellar/{root}/{id}/.plugin-version", new MockFileData(version));
    }

    public ShellViewModel Start()
    {
        var sessions = new ClientSessions(Store, new Orch(), new NoScan(), new NoProc(), () => DateTimeOffset.UnixEpoch, a => a());
        var deps = new PluginInstallDeps(new Installer(Fs), new PluginInstaller(Fs), new HttpClient(new DllHandler()));
        var core = new DashboardServices(new ClientInventory(Fs, deps.Installer, deps.Plugins, new DoorstopToggle(Fs)),
            new RegistryCache(new Reg(Registry), () => Store.Load()), new FrameworkManifests(Manifests), new Review(), deps,
            new ClientCandidates(GameDetector, new Platform()));
        Services = new WorkspaceServices(core, new DoorstopToggle(Fs), Fs, new Platform(), GameDetector, new GameLocator(Fs), Confirm,
            Timer: (_, tick) => { Ticks.Add(tick); return new Disposer(() => Ticks.Remove(tick)); });
        Shell = new ShellViewModel(Store, sessions, new ShellPages(s => new DashboardViewModel(s, core), (s, cl) => new ClientWorkspaceViewModel(s, cl, Services, Tabs), s => new object(), s => new object()));
        Shell.Start();
        return Shell;
    }

    public async Task<ClientWorkspaceViewModel> OpenAsync(string clientId)
    {
        Shell.ShowClientById(clientId);
        var ws = (ClientWorkspaceViewModel)Shell.Current!;
        await ws.RefreshAsync();
        return ws;
    }
}
