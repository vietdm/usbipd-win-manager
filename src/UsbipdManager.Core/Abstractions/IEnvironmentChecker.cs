using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Abstractions;

public interface IEnvironmentChecker
{
    /// <summary>Read-only checks (section 6.2 of docs/PLAN.md). Every result is written to the log.</summary>
    Task<EnvironmentReport> CheckAsync(CancellationToken cancellationToken = default);
}
