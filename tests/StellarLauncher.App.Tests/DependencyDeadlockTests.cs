using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using StellarLauncher.App.Services;
using StellarLauncher.App.ViewModels;
using StellarLauncher.Core.Clients;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Inventory;
using StellarLauncher.Core.Model;
using StellarLauncher.Core.Services;
using Xunit;

/// <summary>Task 6 fix round 2, Critical: the dependency gate must never be waited for SYNCHRONOUSLY on the UI
/// thread. If the holder is an EnsureAsync started from the UI context, its download continuations need that
/// thread — a blocking Wait() there deadlocks the launcher. Reproduced on a single-threaded context pump.</summary>
public class DependencyDeadlockTests
{
    private static readonly byte[] DllBytes = Encoding.UTF8.GetBytes("dll-bytes");
    private static readonly string DllSha = Convert.ToHexString(SHA256.HashData(DllBytes)).ToLowerInvariant();
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>Holds the dependency download open until released; the plugin DLL itself downloads at once.</summary>
    private sealed class HeldHandler(TaskCompletionSource hold, TaskCompletionSource started) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.RequestUri!.AbsolutePath.Contains("/deps/"))
            {
                started.TrySetResult();
                await hold.Task.WaitAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(DllBytes) };
        }
    }

    private sealed class EmptyRegistry : IPluginRegistryService
    {
        public Task<IReadOnlyList<PluginEntry>> FetchAllAsync(IEnumerable<Uri> urls, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PluginEntry>>(Array.Empty<PluginEntry>());
    }
    private sealed class Offline : IVersionService
    {
        public Task<FrameworkManifest> FetchAsync(Uri url, CancellationToken ct = default) => throw new HttpRequestException("offline");
    }

    private static PluginDependency Dep(string id) =>
        new(id, id, "1.0", $"https://cdn/deps/{id}", DllSha, DllBytes.Length, "file",
            new[] { new PluginDependencyFile(null, $"{id}.bin") }, "game", ModdedOnly: true, License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    /// <summary>Review fix round 2 (c): unlike WorkspaceFixture.ScriptedSteps (answers synchronously — no real
    /// suspension), this genuinely YIELDS while "waiting for the player" (a real async gap via a held
    /// TaskCompletionSource), so the deadlock test proves the single-threaded UI context keeps processing the
    /// OTHER queued work (keep, again) while a step dialog is still open — not just that a step which never
    /// actually suspends happens to complete.</summary>
    private sealed class YieldingSteps : IPluginSteps
    {
        public readonly List<object> Asked = new();
        private readonly TaskCompletionSource<InstallStepResult?> _installAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void AnswerInstall(InstallStepResult? result) => _installAnswer.TrySetResult(result);
        public async Task<InstallStepResult?> AskInstallAsync(InstallStep step)
        {
            Asked.Add(step);
            await Task.Yield();
            return await _installAnswer.Task;
        }
        public Task<bool?> AskReinstallAsync(ReinstallStep step) { Asked.Add(step); return Task.FromResult<bool?>(false); }
        public Task<RemoveChoice?> AskRemoveAsync(RemoveStep step) { Asked.Add(step); return Task.FromResult<RemoveChoice?>(RemoveChoice.PluginAndDependencies); }
    }

    [Fact]
    public async Task Launch_review_and_install_on_the_UI_context_never_deadlock_behind_a_UI_started_ensure()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/g");
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var http = new HttpClient(new HeldHandler(hold, started));
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), http, new DependencyService(fs, http));
        var inventory = new ClientInventory(fs, deps.Installer, deps.Plugins, new DoorstopToggle(fs));
        var review = new PreLaunchReviewService(new RegistryCache(new EmptyRegistry(), () => new LauncherConfig()),
            inventory, new Offline(), deps, _ => Task.FromResult(PreLaunchResult.Proceed));
        var plugin = new PluginEntry("p2", "P2", "d", null, new[]
        {
            new PluginVersion("1.0.0", null, "P2.dll", "https://cdn/p2.dll", DllSha, "0.1.0", null, null, Dependencies: new[] { Dep("b") }),
        });
        using var ui = new SingleThreadContext();

        // 1. An install-time ensure started FROM the UI context holds the folder's gate across its download.
        var ensure = ui.Run(() => deps.Dependencies.EnsureAsync("/g", "p1", new[] { Dep("a") }, new HashSet<string>(), CancellationToken.None));
        await started.Task.WaitAsync(Limit);
        // 2. Then, on the same UI context: a Vanilla launch (park), a Modded launch (unpark + sweep) and a
        //    Modded plugin install (unpark + ensure) — all of which take the same gate.
        var vanilla = ui.Run(() => review.ReviewAsync(new ClientProfile { Name = "v", Modded = false, GameMiniDir = "/g" }, CancellationToken.None));
        var modded = ui.Run(() => review.ReviewAsync(new ClientProfile { Name = "m", Modded = true, GameMiniDir = "/g" }, CancellationToken.None));
        var install = ui.Run(() => PluginDownloads.InstallAsync(deps, new ClientProfile { Modded = true, GameMiniDir = "/g" },
            plugin, plugin.Versions[0], null));
        await Task.Delay(200);
        hold.TrySetResult();

        var all = Task.WhenAll(ensure, vanilla, modded, install);
        var finished = await Task.WhenAny(all, Task.Delay(Limit));
        Assert.True(finished == all, "deadlock: the UI context was blocked waiting for the dependency gate");
        await all;
        Assert.True(fs.File.Exists("/g/stellar/plugins/p2/P2.dll"));
    }

    // v3: the install step and the two flag writes, started on the UI context while a UI-started ensure holds the
    // folder's gate, all finish — nothing waits synchronously on the gate.
    [Fact]
    public async Task Install_step_and_flag_writes_on_the_UI_context_never_deadlock_behind_a_UI_started_ensure()
    {
        var fs = new MockFileSystem(); fs.AddDirectory("/g");
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var http = new HttpClient(new HeldHandler(hold, started));
        var deps = new PluginInstallDeps(new Installer(fs), new PluginInstaller(fs), http, new DependencyService(fs, http));
        var plugin = new PluginEntry("p2", "P2", "d", null, new[]
        {
            new PluginVersion("1.0.0", null, "P2.dll", "https://cdn/p2.dll", DllSha, "0.1.0", null, null, Dependencies: new[] { Dep("b") with { Optional = true } }),
        });
        var steps = new YieldingSteps();
        var client = new ClientProfile { Modded = true, GameMiniDir = "/g" };
        using var ui = new SingleThreadContext();

        var ensure = ui.Run(() => deps.Dependencies.EnsureAsync("/g", "p1", new[] { Dep("a") }, new HashSet<string>(), CancellationToken.None));
        await started.Task.WaitAsync(Limit);
        var install = ui.Run(() => PluginInstallFlow.RunAsync(deps, steps, new PluginInstallRequest(client, plugin, plugin.Versions[0], false, null), () => { }, null));
        var keep = ui.Run(() => deps.Dependencies.SetKeptAsync("/g", "p1", true));
        var again = ui.Run(() => deps.Dependencies.RequestReinstallAsync("/g", "p1"));
        await Task.Delay(200);
        hold.TrySetResult();
        steps.AnswerInstall(new InstallStepResult(new HashSet<string>()));   // the "player" answers while the ensure is still unblocking

        var all = Task.WhenAll(ensure, install, keep, again);
        var finished = await Task.WhenAny(all, Task.Delay(Limit));
        Assert.True(finished == all, "deadlock: the UI context was blocked waiting for the dependency gate");
        await all;
        Assert.Single(steps.Asked);
        Assert.True(fs.File.Exists("/g/stellar/plugins/p2/P2.dll"));
    }
}
