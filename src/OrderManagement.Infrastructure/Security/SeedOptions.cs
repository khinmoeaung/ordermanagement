namespace OrderManagement.Infrastructure.Security;

public class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminEmail { get; set; } = "";
    public string AdminPassword { get; set; } = "";

    public static bool Validate(SeedOptions o) =>
        !string.IsNullOrWhiteSpace(o.AdminEmail) && !string.IsNullOrWhiteSpace(o.AdminPassword);
}
