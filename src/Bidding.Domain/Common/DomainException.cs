namespace Bidding.Domain.Common;

/// <summary>Thrown when an operation would break a business rule. The API maps it to HTTP 422.</summary>
public sealed class DomainException(string message) : Exception(message);
