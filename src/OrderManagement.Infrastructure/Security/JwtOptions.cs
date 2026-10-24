using System.Text;

namespace OrderManagement.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;

    public static bool Validate(JwtOptions o) =>
        !string.IsNullOrWhiteSpace(o.Issuer)
        && !string.IsNullOrWhiteSpace(o.Audience)
        && !string.IsNullOrWhiteSpace(o.SigningKey)
        && Encoding.UTF8.GetByteCount(o.SigningKey) >= 32;
}
