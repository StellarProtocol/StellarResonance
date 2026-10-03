using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;

namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Wraps a real <see cref="IFileSystem"/> and throws once (first matching call only) when one of
/// <c>methodNames</c> on <c>File</c> targets <c>failPath</c> — lets tests pin the transactional-rollback
/// (C1) and skip-removal (I6) behaviour without hand-writing a full <see cref="IFile"/> fake.</summary>
public sealed class FaultInjectingFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;

    public FaultInjectingFileSystem(IFileSystem inner, string failPath, params string[] methodNames)
    {
        _inner = inner;
        var proxy = (FaultProxy)DispatchProxy.Create<IFile, FaultProxy>()!;
        proxy.Inner = inner.File;
        proxy.FailPath = failPath;
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
        public string[] MethodNames = Array.Empty<string>();
        private bool _fired;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (!_fired && targetMethod is not null && MethodNames.Contains(targetMethod.Name) && args is { Length: >= 1 })
            {
                var path = targetMethod.Name == "Move" && args.Length >= 2 ? args[1] as string : args[0] as string;
                if (path == FailPath)
                {
                    _fired = true;
                    throw new IOException($"injected failure: {targetMethod.Name} {FailPath}");
                }
            }
            return targetMethod!.Invoke(Inner, args);
        }
    }
}
