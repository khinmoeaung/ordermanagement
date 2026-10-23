using System.Text.Json;

namespace OrderManagement.Application.Assistant;

/// <summary>Strict readers for model-supplied tool input. Every violation throws <see cref="ToolInputException"/>.</summary>
public static class ToolInput
{
    public static void RequireObject(JsonElement input, params string[] allowedProperties)
    {
        if (input.ValueKind != JsonValueKind.Object)
            throw new ToolInputException("input must be a JSON object.");

        foreach (JsonProperty property in input.EnumerateObject())
            if (!allowedProperties.Contains(property.Name, StringComparer.Ordinal))
                throw new ToolInputException($"unknown property '{property.Name}'.");
    }

    public static string RequiredString(JsonElement input, string name)
    {
        if (!input.TryGetProperty(name, out JsonElement value))
            throw new ToolInputException($"'{name}' is required.");
        if (value.ValueKind != JsonValueKind.String)
            throw new ToolInputException($"'{name}' must be a string.");
        return value.GetString()!;
    }

    public static TEnum? OptionalEnum<TEnum>(JsonElement input, string name) where TEnum : struct, Enum
    {
        if (!input.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new ToolInputException($"'{name}' must be a string.");

        string text = value.GetString()!;
        if (!Enum.GetNames<TEnum>().Contains(text, StringComparer.Ordinal))
            throw new ToolInputException($"'{name}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
        return Enum.Parse<TEnum>(text);
    }

    public static JsonElement Schema(object schema) => JsonSerializer.SerializeToElement(schema);
}
