namespace DeBliss.Application.Common.Exceptions;

/// <summary>
/// The request is well formed but cannot be carried out against the current
/// state of the data — a duplicate name, for example. The API maps this to 409.
/// </summary>
public sealed class ConflictException(string message) : Exception(message);
