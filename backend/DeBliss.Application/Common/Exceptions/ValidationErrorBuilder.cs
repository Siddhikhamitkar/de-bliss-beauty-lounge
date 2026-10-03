namespace DeBliss.Application.Common.Exceptions;

/// <summary>
/// Collects field-level validation messages and throws them as one
/// <see cref="ValidationFailedException"/>, so a caller reports every problem
/// with a request instead of only the first.
/// </summary>
public sealed class ValidationErrorBuilder
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool IsValid => _errors.Count == 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    public void ThrowIfInvalid()
    {
        if (_errors.Count > 0)
        {
            throw new ValidationFailedException(
                _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        }
    }
}
