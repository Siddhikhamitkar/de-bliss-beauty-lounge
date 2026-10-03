namespace DeBliss.Application.Common.Exceptions;

/// <summary>
/// Input failed one or more business validation rules. Carries the errors keyed
/// by the request field they belong to so the API can return them as an RFC 9457
/// validation problem and the UI can show them beside each field.
/// </summary>
public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
