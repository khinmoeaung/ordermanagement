namespace OrderManagement.Domain.Common;

public class NotFoundException(string entity, Guid id)
    : DomainException($"{entity} with id '{id}' was not found.");
