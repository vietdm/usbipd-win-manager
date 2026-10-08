using System.Diagnostics;
using UsbipdManager.Core.Abstractions;

namespace UsbipdManager.Core.Processes;

/// <summary>A started long-running process. <see cref="Exited"/> is raised once, whether the process ends on its own or is disposed.</summary>
internal sealed class ManagedProcess : IManagedProcess
{
    private static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly Process _process;
    private int _exitRaised;
    private int _disposed;

    public ManagedProcess(Process process)
    {
        _process = process;
        _process.Exited += OnProcessExited;
    }

    public int Id { get; private set; }

    public bool HasExited
    {
        get
        {
            if (Volatile.Read(ref _exitRaised) == 1 || Volatile.Read(ref _disposed) == 1)
            {
                return true;
            }

            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public event EventHandler? Exited;

    public void Start()
    {
        _process.Start();
        Id = _process.Id;

        // The output is not used, but the pipes must be drained so the child never blocks on a full buffer.
        // Stdin stays open on purpose: some tools (wsl.exe) end when their input reaches EOF.
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                ProcessRunner.KillProcessTree(_process);
                _process.WaitForExit(KillWaitTimeout);
            }
        }
        catch (InvalidOperationException)
        {
        }

        RaiseExited();
        _process.Exited -= OnProcessExited;
        _process.Dispose();
    }

    private void OnProcessExited(object? sender, EventArgs e) => RaiseExited();

    private void RaiseExited()
    {
        if (Interlocked.Exchange(ref _exitRaised, 1) == 0)
        {
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }
}
