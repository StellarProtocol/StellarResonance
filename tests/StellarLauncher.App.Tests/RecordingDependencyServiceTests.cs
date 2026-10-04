using StellarLauncher.App.Services;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;

/// <summary>Task 6 follow-up: the in-memory record of the last install-time failure per dependency.</summary>
public class RecordingDependencyServiceTests
{
    private sealed class Scripted : IDependencyService
    {
        public DependencyState Next = DependencyState.Failed;
        public Task<IReadOnlyList<DependencyStatus>> EnsureAsync(string g, string p, IReadOnlyList<PluginDependency> deps, ISet<string> s, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DependencyStatus>>(deps.Select(d => new DependencyStatus(d.Id, Next, Next == DependencyState.Failed ? "HTTP 404" : null)).ToList());
        public IReadOnlyList<DependencyStatus> Status(string g, string p, IReadOnlyList<PluginDependency> deps, ISet<string> s) => Array.Empty<DependencyStatus>();
        public Task RemoveAsync(string g, string p, string d, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAllAsync(string g, string p, CancellationToken ct = default) => Task.CompletedTask;
        public Task ParkModdedOnlyAsync(string g, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnparkModdedOnlyAsync(string g, IReadOnlySet<string>? keepParked = null, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<string> LedgerPluginIds(string g) => Array.Empty<string>();

        public int KeptCalls, ReinstallCalls;
        public bool KeptAnswer;
        public Task SetKeptAsync(string g, string p, bool kept, CancellationToken ct = default) { KeptCalls++; return Task.CompletedTask; }
        public bool IsKept(string g, string p) => KeptAnswer;
        public Task RequestReinstallAsync(string g, string p, CancellationToken ct = default) { ReinstallCalls++; return Task.CompletedTask; }
    }

    private static readonly PluginDependency Fx = new("fx", "Fx", "1.0", "https://cdn/fx", new string('a', 64), 1, "file",
        new[] { new PluginDependencyFile(null, "fx.dll") }, "game", License: "MIT", LicenseUrl: "https://l", SourceUrl: "https://s");

    [Fact]
    public async Task Remembers_a_failure_until_a_later_outcome_or_a_changed_declaration()
    {
        var inner = new Scripted();
        var sut = new RecordingDependencyService(inner);

        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        Assert.Equal("HTTP 404", sut.LastProblem("/g", "p", Fx)?.Detail);
        Assert.Null(sut.LastProblem("/other", "p", Fx));
        Assert.Null(sut.LastProblem("/g", "p", Fx with { Version = "1.1" }));   // the plugin now declares another build

        inner.Next = DependencyState.Installed;
        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        Assert.Null(sut.LastProblem("/g", "p", Fx));
    }

    [Fact]
    public async Task Remove_and_RemoveAll_forget_failures()
    {
        var sut = new RecordingDependencyService(new Scripted());
        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        await sut.RemoveAsync("/g", "p", "fx");
        Assert.Null(sut.LastProblem("/g", "p", Fx));

        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        await sut.RemoveAllAsync("/g", "p");
        Assert.Null(sut.LastProblem("/g", "p", Fx));
    }

    [Fact]
    public async Task Remembers_an_install_time_Blocked_like_a_failure()
    {
        var inner = new Scripted { Next = DependencyState.Blocked };
        var sut = new RecordingDependencyService(inner);

        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);

        Assert.Equal(DependencyState.Blocked, sut.LastProblem("/g", "p", Fx)?.State);
        Assert.Null(sut.LastProblem("/g", "p", Fx with { Sha256 = new string('b', 64) }));
    }

    // v3: the app wires the recorder around the real service — the new members must reach it.
    [Fact]
    public async Task Forwards_the_kept_and_reinstall_members()
    {
        var inner = new Scripted { KeptAnswer = true };
        var sut = new RecordingDependencyService(inner);
        await sut.SetKeptAsync("/g", "p", true);
        await sut.RequestReinstallAsync("/g", "p");
        Assert.True(sut.IsKept("/g", "p"));
        Assert.Equal(1, inner.KeptCalls);
        Assert.Equal(1, inner.ReinstallCalls);
    }
}
