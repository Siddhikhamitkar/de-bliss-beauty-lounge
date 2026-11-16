using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using DeBliss.Application.Identity.Ports;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>Records every message instead of sending it, so tests can read the code a customer would receive.</summary>
internal sealed partial class CapturingOtpSender : ICustomerOtpSender
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _sent = new();

    public IReadOnlyList<(string Phone, string Message)> Sent => [.. _sent];

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        _sent.Enqueue((phoneNumber, message));
        return Task.CompletedTask;
    }

    /// <summary>The code in the newest message to <paramref name="canonicalPhone"/> (E.164), or null.</summary>
    public string? LastCodeFor(string canonicalPhone) =>
        _sent.Reverse()
            .Where(sent => sent.Phone == canonicalPhone)
            .Select(sent => CodePattern().Match(sent.Message))
            .Where(match => match.Success)
            .Select(match => match.Value)
            .FirstOrDefault();

    public int CountFor(string canonicalPhone) => _sent.Count(sent => sent.Phone == canonicalPhone);

    [GeneratedRegex(@"^\d{4,10}")]
    private static partial Regex CodePattern();
}
