using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace StellarLauncher.Core.Launch;

/// <summary>The slice of <see cref="Process"/> the launcher needs, so sessions are testable.</summary>
public interface IGameProcess
{
    int Id { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken ct);
    void Kill(bool entireProcessTree);
}

public interface IProcessFactory
{
    IGameProcess? Start(ProcessStartInfo psi);
    IGameProcess? Attach(int pid);
}

public sealed class SystemProcessFactory : IProcessFactory
{
    public IGameProcess? Start(ProcessStartInfo psi)
    {
        var p = Process.Start(psi);
        return p is null ? null : new SystemGameProcess(p);
    }

    public IGameProcess? Attach(int pid)
    {
        try { return new SystemGameProcess(Process.GetProcessById(pid)); }
        catch { return null; }
    }

    private sealed class SystemGameProcess : IGameProcess
    {
        private readonly Process _p;
        public SystemGameProcess(Process p) => _p = p;
        public int Id => _p.Id;
        public bool HasExited { get { try { return _p.HasExited; } catch { return true; } } }
        public int ExitCode { get { try { return _p.ExitCode; } catch { return -1; } } }
        public Task WaitForExitAsync(CancellationToken ct) => _p.WaitForExitAsync(ct);
        public void Kill(bool entireProcessTree) { try { _p.Kill(entireProcessTree); } catch { /* already gone */ } }
    }
}
