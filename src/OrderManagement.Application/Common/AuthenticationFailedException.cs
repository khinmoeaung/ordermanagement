namespace OrderManagement.Application.Common;

/// <summary>Maps to 401. Deliberately not a DomainException.</summary>
public class AuthenticationFailedException(string message) : Exception(message);
