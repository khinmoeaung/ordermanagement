namespace OrderManagement.Domain.Common;

public class ConflictException(string message) : DomainException(message);
