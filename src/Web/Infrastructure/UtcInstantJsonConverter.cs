using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyWealthV2.Web.Infrastructure;

/// <summary>
/// Reads and writes every <see cref="DateTimeOffset"/> on the wire (fixes/utc-instants.md).
/// A request instant must carry <c>Z</c> or an offset and is converted to UTC. Responses are UTC with <c>Z</c>.
/// </summary>
public sealed class UtcInstantJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !reader.TryGetDateTimeOffset(out var value)
            || !reader.TryGetDateTime(out var local)
            || local.Kind == DateTimeKind.Unspecified)
        {
            throw new InstantFormatException();
        }

        return value.ToUniversalTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
}

/// <summary>A request instant has no offset or is not an ISO 8601 date and time.</summary>
public sealed class InstantFormatException()
    : JsonException("Send an ISO 8601 date and time with Z or an offset, for example 2026-11-14T03:13:00Z.");
