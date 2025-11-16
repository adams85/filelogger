using System.Text.Encodings.Web;
using System.Text.Json;

namespace Karambolo.Extensions.Logging.File.Json;

public class JsonFileLogFormatOptions
{
    public static JsonFileLogFormatOptions ForJsonLines(JavaScriptEncoder? encoder = null)
    {
        return new JsonFileLogFormatOptions
        {
            EntrySeparator = "",
            JsonWriterOptions = new JsonWriterOptions { Indented = false, Encoder = encoder }
        };
    }

    public JsonWriterOptions? JsonWriterOptions { get; set; }
    public string? EntrySeparator { get; set; }
}
