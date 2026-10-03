namespace DeBliss.Application.Common.Exceptions;

/// <summary>
/// A resource named by the caller does not exist. The API maps this to 404.
/// </summary>
public sealed class NotFoundException(string message) : Exception(message);
