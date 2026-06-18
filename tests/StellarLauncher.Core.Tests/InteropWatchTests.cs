using System;
using System.IO.Abstractions.TestingHelpers;
using StellarLauncher.Core.Services;
using Xunit;

public class InteropWatchTests
{
    private const string Gm = "/gm";
    private const string InteropDir = "/gm/BepInEx/interop";
    private const string GameAssembly = "/gm/GameAssembly.dll";

    private static MockFileData At(DateTimeOffset when, string content = "x")
        => new(content) { LastWriteTime = when };

    [Fact]
    public void RegenExpected_when_interop_dir_missing()
    {
        var fs = new MockFileSystem();
        Assert.True(new InteropWatch(fs).RegenExpected(Gm));
    }

    [Fact]
    public void RegenExpected_when_interop_dir_empty()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(InteropDir);
        Assert.True(new InteropWatch(fs).RegenExpected(Gm));
    }

    [Fact]
    public void RegenExpected_when_game_assembly_newer_than_interop()
    {
        var old = DateTimeOffset.UtcNow.AddHours(-2);
        var fs = new MockFileSystem();
        fs.AddFile($"{InteropDir}/Foo.dll", At(old));
        fs.AddFile(GameAssembly, At(DateTimeOffset.UtcNow));   // game patched after last gen
        Assert.True(new InteropWatch(fs).RegenExpected(Gm));
    }

    [Fact]
    public void NoRegen_when_interop_newer_than_sources()
    {
        var old = DateTimeOffset.UtcNow.AddHours(-2);
        var fs = new MockFileSystem();
        fs.AddFile(GameAssembly, At(old));
        fs.AddFile($"{InteropDir}/Foo.dll", At(DateTimeOffset.UtcNow));
        fs.AddFile($"{InteropDir}/Bar.dll", At(DateTimeOffset.UtcNow));
        Assert.False(new InteropWatch(fs).RegenExpected(Gm));
    }

    [Fact]
    public void Snapshot_reports_count_and_newest_write()
    {
        var older = DateTimeOffset.UtcNow.AddMinutes(-5);
        var newest = DateTimeOffset.UtcNow.AddMinutes(-1);
        var fs = new MockFileSystem();
        fs.AddFile($"{InteropDir}/A.dll", At(older));
        fs.AddFile($"{InteropDir}/B.dll", At(newest));
        fs.AddFile($"{InteropDir}/notes.txt", At(DateTimeOffset.UtcNow));   // non-dll ignored

        var snap = new InteropWatch(fs).Snapshot(Gm);
        Assert.Equal(2, snap.Count);
        Assert.NotNull(snap.NewestWriteUtc);
        Assert.Equal(newest.UtcDateTime, snap.NewestWriteUtc!.Value.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Snapshot_is_empty_when_no_assemblies()
    {
        var fs = new MockFileSystem();
        var snap = new InteropWatch(fs).Snapshot(Gm);
        Assert.Equal(0, snap.Count);
        Assert.Null(snap.NewestWriteUtc);
    }
}
