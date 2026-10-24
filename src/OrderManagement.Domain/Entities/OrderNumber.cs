using System.Security.Cryptography;

namespace OrderManagement.Domain.Entities;

public static class OrderNumber
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int SuffixLength = 6;

    public static string Generate(DateTime utcNow)
    {
        var suffix = string.Create(SuffixLength, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
        return $"ORD-{utcNow:yyyyMMdd}-{suffix}";
    }
}
