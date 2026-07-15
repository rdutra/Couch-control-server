namespace CouchControl.Core.Models;

public sealed record OperationResult
{
    private OperationResult(
        bool succeeded,
        string message,
        string? errorCode = null,
        OperationResult? rollbackResult = null,
        bool isPartialSuccess = false,
        string? outcome = null,
        IReadOnlyList<string>? details = null)
    {
        Succeeded = succeeded;
        Message = message;
        ErrorCode = errorCode;
        RollbackResult = rollbackResult;
        IsPartialSuccess = isPartialSuccess;
        Outcome = outcome;
        Details = details ?? Array.Empty<string>();
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public string? ErrorCode { get; }

    public OperationResult? RollbackResult { get; }

    public bool IsPartialSuccess { get; }

    public string? Outcome { get; }

    public IReadOnlyList<string> Details { get; }

    public static OperationResult Success(
        string message = "The operation completed successfully.",
        string? outcome = null,
        IReadOnlyList<string>? details = null) =>
        new(true, message, outcome: outcome, details: details);

    public static OperationResult PartialSuccess(
        string message,
        string? outcome = null,
        IReadOnlyList<string>? details = null) =>
        new(true, message, isPartialSuccess: true, outcome: outcome, details: details);

    public static OperationResult Failure(
        string message,
        string? errorCode = null,
        OperationResult? rollbackResult = null,
        string? outcome = null,
        IReadOnlyList<string>? details = null) =>
        new(false, message, errorCode, rollbackResult, outcome: outcome, details: details);
}
