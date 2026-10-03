using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Task 6 fix round, Important 1: every mutating member serialises per game folder. Without it a
/// ledger read-modify-write that spans a download loses the other call's entry (the ledger is read BEFORE
/// the download and written after it).</summary>
public sealed class DependencyServiceConcurrencyTests
{
    private const string G = "/game_mini";
    private readonly MockFileSystem _fs = new();
    private readonly Dictionary<string, byte[]> _web = new();
    private int _inFlight, _maxInFlight;

    public DependencyServiceConcurrencyTests() => _fs.AddDirectory(G);

    /// <summary>Holds every download open for <see cref="Hold"/> (or until <see cref="Release"/>), so
    /// unserialised calls overlap deterministically.</summary>
    private sealed class SlowStub(DependencyServiceConcurrencyTests t) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref t._inFlight);
            lock (t) t._maxInFlight = Math.Max(t._maxInFlight, n);
            t.Started.TrySetResult();
            await Task.WhenAny(Task.Delay(t.Hold, ct), t.Release.Task);
            Interlocked.Decrement(ref t._inFlight);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(t._web[r.RequestUri!.ToString()]) };
        }
    }

    private TimeSpan Hold = TimeSpan.FromMilliseconds(300);
    private readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DependencyService Make() => new(_fs, new HttpClient(new SlowStub(this)));

    private PluginDependency Dep(string id, byte b)
    {
        var bytes = new[] { b };
        _web[$"https://cdn/{id}"] = bytes;
        return new PluginDependency(id, id, "1.0", $"https://cdn/{id}", Convert.ToHexString(SHA256.HashData(bytes)), 1, "file",
            new[] { new PluginDependencyFile(null, $"{id}.dll") }, "plugin");
    }

    private static readonly ISet<string> None = new HashSet<string>();

    [Fact]
    public async Task Two_concurrent_ensures_for_one_plugin_keep_both_ledger_entries()
    {
        var s = Make();
        var a = Dep("a", 1); var b = Dep("b", 2);

        await Task.WhenAll(
            Task.Run(() => s.EnsureAsync(G, "p", new[] { a }, None, default)),
            Task.Run(() => s.EnsureAsync(G, "p", new[] { b }, None, default)));

        var ids = new DependencyLedgerStore(_fs).Read(G, "p").Entries.Select(e => e.DependencyId).OrderBy(x => x);
        Assert.Equal(new[] { "a", "b" }, ids);
        Assert.Equal(1, _maxInFlight); // serialised: the second never downloaded while the first held the folder
    }

    [Fact]
    public async Task Remove_overlapping_an_ensure_is_not_undone_by_the_ensures_ledger_write()
    {
        var s = Make();
        var a = Dep("a", 1); var b = Dep("b", 2);
        Release.TrySetResult(); await s.EnsureAsync(G, "p", new[] { b }, None, default); // b installed
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Hold = TimeSpan.FromSeconds(30);
        var gated = new DependencyService(_fs, new HttpClient(new GateStub(this, hold)));

        var ensure = Task.Run(() => gated.EnsureAsync(G, "p", new[] { a }, None, default));
        await _gateStarted.Task;                                      // ensure has read the ledger and is downloading
        var remove = Task.Run(() => gated.Remove(G, "p", "b"));
        await Task.Delay(150);                                        // unserialised: Remove finishes here
        hold.TrySetResult();
        await Task.WhenAll(ensure, remove);

        var ids = new DependencyLedgerStore(_fs).Read(G, "p").Entries.Select(e => e.DependencyId);
        Assert.Equal(new[] { "a" }, ids);
        Assert.False(_fs.File.Exists($"{G}/stellar/deps/p/b.dll"));
    }

    private readonly TaskCompletionSource _gateStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class GateStub(DependencyServiceConcurrencyTests t, TaskCompletionSource hold) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            t._gateStarted.TrySetResult();
            await hold.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(t._web[r.RequestUri!.ToString()]) };
        }
    }

    [Fact]
    public async Task A_cancelled_wait_for_the_folder_throws_without_touching_anything()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var s = new DependencyService(_fs, new HttpClient(new GateStub(this, hold)));
        var first = Task.Run(() => s.EnsureAsync(G, "p", new[] { Dep("a", 1) }, None, default));
        await _gateStarted.Task;
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.EnsureAsync(G, "p", new[] { Dep("b", 2) }, None, cts.Token));

        hold.TrySetResult();
        await first;
        Assert.Equal(new[] { "a" }, new DependencyLedgerStore(_fs).Read(G, "p").Entries.Select(e => e.DependencyId));
    }
}
