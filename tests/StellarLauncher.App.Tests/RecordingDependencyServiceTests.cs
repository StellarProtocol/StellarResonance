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
        public void Remove(string g, string p, string d) { }
        public void RemoveAll(string g, string p) { }
        public void ParkModdedOnly(string g) { }
        public void UnparkModdedOnly(string g) { }
        public IReadOnlyList<string> LedgerPluginIds(string g) => Array.Empty<string>();
    }

    private static readonly PluginDependency Fx = new("fx", "Fx", "1.0", "https://cdn/fx", new string('a', 64), 1, "file",
        new[] { new PluginDependencyFile(null, "fx.dll") }, "game");

    [Fact]
    public async Task Remembers_a_failure_until_a_later_outcome_or_a_changed_declaration()
    {
        var inner = new Scripted();
        var sut = new RecordingDependencyService(inner);

        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        Assert.Equal("HTTP 404", sut.LastFailure("/g", "p", Fx)?.Detail);
        Assert.Null(sut.LastFailure("/other", "p", Fx));
        Assert.Null(sut.LastFailure("/g", "p", Fx with { Version = "1.1" }));   // the plugin now declares another build

        inner.Next = DependencyState.Installed;
        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        Assert.Null(sut.LastFailure("/g", "p", Fx));
    }

    [Fact]
    public async Task Remove_and_RemoveAll_forget_failures()
    {
        var sut = new RecordingDependencyService(new Scripted());
        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        sut.Remove("/g", "p", "fx");
        Assert.Null(sut.LastFailure("/g", "p", Fx));

        await sut.EnsureAsync("/g", "p", new[] { Fx }, new HashSet<string>(), default);
        sut.RemoveAll("/g", "p");
        Assert.Null(sut.LastFailure("/g", "p", Fx));
    }
}
