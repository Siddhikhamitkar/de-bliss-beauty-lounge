namespace DeBliss.Application.Common.Exceptions;

/// <summary>
/// The caller is asking too often (for example another one-time code before the
/// resend cooldown has passed). The API maps this to 429 with a Retry-After header.
/// </summary>
public sealed class TooManyRequestsException(string message, TimeSpan retryAfter) : Exception(message)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
