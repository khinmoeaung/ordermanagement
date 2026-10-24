namespace OrderManagement.Domain.Common;

internal static class Guard
{
    public static string NotBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{name} is required.");
        return value.Trim();
    }
}
