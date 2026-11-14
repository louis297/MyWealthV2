using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyWealthV2.Web.Infrastructure;

/// <summary>
/// Reads and writes every <see cref="DateTimeOffset"/> on the wire (fixes/utc-instants.md).
/// Responses are UTC with <c>Z</c>.
/// </summary>
public sealed class UtcInstantJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFF'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
}
