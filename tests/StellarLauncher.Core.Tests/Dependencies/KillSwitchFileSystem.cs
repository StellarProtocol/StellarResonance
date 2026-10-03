using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;

namespace StellarLauncher.Core.Tests.Dependencies;

/// <summary>Simulates the launcher process dying mid-operation (final review I2): once a <c>File.Move</c> INTO
/// <c>killAfterMoveTo</c> has completed, every later mutating file call (move, delete, write, copy) throws,
/// so nothing after that instant — no ledger write, no rollback — ever reaches the disk. The inner file system
/// then holds exactly what a killed process would have left behind.</summary>
public sealed class KillSwitchFileSystem : IFileSystem
{
    private static readonly string[] Mutating = { "Move", "Delete", "WriteAllBytes", "WriteAllText", "Copy", "AppendAllText" };
    private readonly IFileSystem _inner;

    public KillSwitchFileSystem(IFileSystem inner, string killAfterMoveTo)
    {
        _inner = inner;
        var proxy = (Proxy)DispatchProxy.Create<IFile, Proxy>()!;
        proxy.Inner = inner.File;
        proxy.KillPath = killAfterMoveTo;
        File = (IFile)proxy;
    }

    public bool Dead => ((Proxy)File).Dead;
    public IFile File { get; }
    public IDirectory Directory => _inner.Directory;
    public IFileInfoFactory FileInfo => _inner.FileInfo;
    public IFileStreamFactory FileStream => _inner.FileStream;
    public IPath Path => _inner.Path;
    public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;
    public IDriveInfoFactory DriveInfo => _inner.DriveInfo;
    public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;
    public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;

    public class Proxy : DispatchProxy
    {
        public IFile Inner = null!;
        public string KillPath = "";
        public bool Dead;

        protected override object? Invoke(MethodInfo? m, object?[]? args)
        {
            if (Dead && m is not null && Mutating.Contains(m.Name)) throw new IOException("process killed");
            var result = m!.Invoke(Inner, args);
            if (m.Name == "Move" && args is { Length: >= 2 } && args[1] as string == KillPath) Dead = true;
            return result;
        }
    }
}
