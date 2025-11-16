using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File.Test.Helpers;

public static class FileProviderExtensions
{
    private static string ReadAllText(this IFileInfo fileInfo, Encoding? encoding, out Encoding detectedEncoding)
    {
        using (Stream stream = fileInfo.CreateReadStream())
        using (StreamReader reader = encoding is not null ? new StreamReader(stream, encoding) : new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
        {
            string result = reader.ReadToEnd();
            detectedEncoding = reader.CurrentEncoding;
            return result;
        }
    }

    public static string ReadAllText(this IFileInfo fileInfo, Encoding? encoding = null)
    {
        return ReadAllText(fileInfo, encoding ?? Encoding.UTF8, out _);
    }

    public static string ReadAllText(this IFileInfo fileInfo, out Encoding detectedEncoding)
    {
        return ReadAllText(fileInfo, encoding: null, out detectedEncoding);
    }
}
