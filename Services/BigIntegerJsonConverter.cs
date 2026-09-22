using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EpicCottonGame.Services;

/// <summary>Reads BigInteger values from JSON numbers or quoted numeric strings.</summary>
public sealed class BigIntegerJsonConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return BigInteger.Parse(reader.GetString()!);

        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException($"Expected a JSON number or numeric string for BigInteger, got {reader.TokenType}.");

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        string rawValue = document.RootElement.GetRawText();
        return BigInteger.Parse(rawValue);
    }

    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
        => writer.WriteRawValue(value.ToString());
}
