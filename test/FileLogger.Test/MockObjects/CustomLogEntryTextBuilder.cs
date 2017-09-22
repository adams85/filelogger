using System.Text;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File.Test
{
    public class CustomLogEntryTextBuilder : FileLogEntryTextBuilder
    {
        protected override int MessagePaddingWidth => 8;

        protected override void AppendLogLevel(StringBuilder sb, LogLevel logLevel)
        {
            sb.Append('[');
            sb.Append(GetLogLevelString(logLevel));
            sb.Append("]: ");
        }
    }
}
