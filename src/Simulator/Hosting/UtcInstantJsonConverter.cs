using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyWealthV2.Simulator.Hosting;

/// <summary>Every instant on the wire is UTC with a <c>Z</c> (simulator spec §8.1).</summary>
public sealed class UtcInstantJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
}
