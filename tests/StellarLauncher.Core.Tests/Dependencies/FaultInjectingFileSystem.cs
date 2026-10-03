using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;

namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Wraps a real <see cref="IFileSystem"/> and throws when one of <c>methodNames</c> on
/// <c>File</c> targets <c>failPath</c> — lets tests pin the transactional-rollback (C1/I1) and
/// skip-removal (I6) behaviour without hand-writing a full <see cref="IFile"/> fake. By default fires
/// once (first matching call only, e.g. "fail this file's write, then let a retry through"); pass
/// <c>once: false</c> to fail every matching call (e.g. "this file's write AND its later rollback-restore
/// both fail" — used to pin that the ORIGINAL exception, not the rollback's, is reported).</summary>
public sealed class FaultInjectingFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;

    public FaultInjectingFileSystem(IFileSystem inner, string failPath, params string[] methodNames)
        : this(inner, failPath, once: true, afterCalls: 0, methodNames) { }

    public FaultInjectingFileSystem(IFileSystem inner, string failPath, bool once, params string[] methodNames)
        : this(inner, failPath, once, afterCalls: 0, methodNames) { }

    /// <summary><paramref name="afterCalls"/> matching calls are let through untouched before the fault
    /// starts firing — e.g. "let this path's own write succeed, but fail its LATER rollback-restore".</summary>
    public FaultInjectingFileSystem(IFileSystem inner, string failPath, bool once, int afterCalls, params string[] methodNames)
    {
        _inner = inner;
        var proxy = (FaultProxy)DispatchProxy.Create<IFile, FaultProxy>()!;
        proxy.Inner = inner.File;
        proxy.FailPath = failPath;
        proxy.Once = once;
        proxy.AfterCalls = afterCalls;
        proxy.MethodNames = methodNames.Length > 0 ? methodNames : new[] { "Move", "WriteAllBytes" };
        File = (IFile)proxy;
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

    public class FaultProxy : DispatchProxy
    {
        public IFile Inner = null!;
        public string FailPath = "";
        public bool Once = true;
        public int AfterCalls;
        public string[] MethodNames = Array.Empty<string>();
        private bool _fired;
        private int _count;
        private int _matchCount;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if ((!Once || !_fired) && targetMethod is not null && MethodNames.Contains(targetMethod.Name) && args is { Length: >= 1 })
            {
                var path = targetMethod.Name == "Move" && args.Length >= 2 ? args[1] as string : args[0] as string;
                if (path == FailPath)
                {
                    if (_matchCount < AfterCalls) { _matchCount++; }
                    else
                    {
                        _fired = true;
                        _count++;
                        throw new IOException($"injected failure #{_count}: {targetMethod.Name} {FailPath}");
                    }
                }
            }
            return targetMethod!.Invoke(Inner, args);
        }
    }
}
