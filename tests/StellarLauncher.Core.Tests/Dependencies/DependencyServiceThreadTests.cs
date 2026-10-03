using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using StellarLauncher.Core.Dependencies;
using StellarLauncher.Core.Model;
using Xunit;
namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Task 6 pre-review minor: even when the folder's gate is FREE (so WaitAsync completes
/// synchronously), no file work or hashing of a mutating member may run on the caller's (UI) thread.</summary>
public sealed class DependencyServiceThreadTests
{
    private const string G = "/game_mini";

    /// <summary>A single-threaded SynchronizationContext pump (the shape of a UI thread).</summary>
    private sealed class Pump : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback, object?)> _q = new();
        public int ThreadId { get; private set; }
        public Pump()
        {
            var ready = new ManualResetEventSlim();
            new Thread(() =>
            {
                SetSynchronizationContext(this); ThreadId = Environment.CurrentManagedThreadId; ready.Set();
                foreach (var (cb, st) in _q.GetConsumingEnumerable()) cb(st);
            }) { IsBackground = true }.Start();
            ready.Wait();
        }
        public override void Post(SendOrPostCallback d, object? state) => _q.Add((d, state));
        public Task Run(Func<Task> f)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ => { try { await f(); tcs.SetResult(); } catch (Exception e) { tcs.SetException(e); } }, null);
            return tcs.Task;
        }
        public void Dispose() => _q.CompleteAdding();
    }

    /// <summary>Records the managed thread of every IFile call.</summary>
    public class FileThreadProxy : DispatchProxy
    {
        public IFile Inner = null!;
        public ConcurrentBag<int> Threads = null!;
        protected override object? Invoke(MethodInfo? m, object?[]? args)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            try { return m!.Invoke(Inner, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
    }

    private sealed class RecordingFs : IFileSystem
    {
        private readonly IFileSystem _inner;
        public readonly ConcurrentBag<int> Threads = new();
        public RecordingFs(IFileSystem inner)
        {
            _inner = inner;
            var p = (FileThreadProxy)DispatchProxy.Create<IFile, FileThreadProxy>()!;
            p.Inner = inner.File; p.Threads = Threads; File = (IFile)p;
        }
        public IFile File { get; }
        public IDirectory Directory => _inner.Directory;
        public IFileInfoFactory FileInfo => _inner.FileInfo;
        public IFileStreamFactory FileStream => _inner.FileStream;
        public IPath Path => _inner.Path;
        public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;
        public IDriveInfoFactory DriveInfo => _inner.DriveInfo;
        public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;
        public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;
    }

    private sealed class Web(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    [Fact]
    public async Task Mutating_members_never_touch_files_on_the_callers_thread_even_with_a_free_gate()
    {
        var mock = new MockFileSystem(); mock.AddDirectory(G);
        var bytes = new byte[] { 1, 2, 3 };
        var dep = new PluginDependency("fx", "fx", "1.0", "https://cdn/fx", Convert.ToHexString(SHA256.HashData(bytes)), 3, "file",
            new[] { new PluginDependencyFile(null, "fx.dll") }, "game", ModdedOnly: true);
        var http = new HttpClient(new Web(bytes));
        await new DependencyService(mock, http).EnsureAsync(G, "p", new[] { dep }, new HashSet<string>(), default); // installed
        var fs = new RecordingFs(mock);
        var s = new DependencyService(fs, http);
        using var ui = new Pump();

        await ui.Run(() => s.EnsureAsync(G, "p", new[] { dep }, new HashSet<string>(), default));   // up to date: hashing only
        await ui.Run(() => s.ParkModdedOnlyAsync(G));
        await ui.Run(() => s.UnparkModdedOnlyAsync(G));
        await ui.Run(() => s.RemoveAsync(G, "p", "fx"));
        await ui.Run(() => s.RemoveAllAsync(G, "p"));

        Assert.NotEmpty(fs.Threads);
        Assert.DoesNotContain(ui.ThreadId, fs.Threads);
    }
}
