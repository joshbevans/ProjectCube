using System.Text.Json;
using System.Text.Json.Serialization;
using CubeProject.Core.Models;

namespace CubeProject.Core.IO;

public class RgbColorJsonConverter : JsonConverter<RgbColor>
{
    public override RgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Accept both [r,g,b] array and {"R":r,"G":g,"B":b} object
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read(); byte r = reader.GetByte();
            reader.Read(); byte g = reader.GetByte();
            reader.Read(); byte b = reader.GetByte();
            reader.Read(); // EndArray
            return new RgbColor(r, g, b);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            byte r = 0, g = 0, b = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var prop = reader.GetString()!.ToUpperInvariant();
                reader.Read();
                if (prop == "R") r = reader.GetByte();
                else if (prop == "G") g = reader.GetByte();
                else if (prop == "B") b = reader.GetByte();
            }
            return new RgbColor(r, g, b);
        }

        throw new JsonException("Expected array [r,g,b] or object {R,G,B}");
    }

    public override void Write(Utf8JsonWriter writer, RgbColor value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.R);
        writer.WriteNumberValue(value.G);
        writer.WriteNumberValue(value.B);
        writer.WriteEndArray();
    }
}
