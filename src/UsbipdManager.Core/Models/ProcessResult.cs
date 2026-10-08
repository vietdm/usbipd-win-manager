namespace UsbipdManager.Core.Models;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut = false)
{
    public bool Success => ExitCode == 0 && !TimedOut;
}
