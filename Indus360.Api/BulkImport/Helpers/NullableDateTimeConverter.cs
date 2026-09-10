using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Helpers
{
    public class NullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str))
                    return null;
                
                if (DateTime.TryParse(str, out var date))
                    return date;
            }
            else if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            // Return null instead of throwing to prevent application crashes when a bad format is provided by the frontend.
            return null;
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteStringValue(value.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            else
                writer.WriteNullValue();
        }
    }
}
