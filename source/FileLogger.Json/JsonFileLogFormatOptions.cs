using System.Text.Json;

namespace Karambolo.Extensions.Logging.File.Json
{
    public class JsonFileLogFormatOptions
    {
        public JsonWriterOptions? JsonWriterOptions { get; set; }
        public string EntrySeparator { get; set; }
    }
}
