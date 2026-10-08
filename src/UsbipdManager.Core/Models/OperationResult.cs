namespace UsbipdManager.Core.Models;

public sealed record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok { get; } = new(true);

    public static OperationResult Fail(string error) => new(false, error);
}
