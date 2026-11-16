namespace DeBliss.Application.Identity.Ports;

/// <summary>
/// Delivers a one-time code (or an account notice) to a mobile number. The
/// Application layer never knows which provider sits behind it: a logging sender
/// in Development, an SMS gateway in production.
/// </summary>
public interface ICustomerOtpSender
{
    /// <param name="phoneNumber">E.164 number.</param>
    /// <param name="message">The full text to deliver.</param>
    Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken);
}
