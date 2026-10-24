namespace OrderManagement.Domain.Entities;

public record OrderLine(Product Product, int Quantity);
