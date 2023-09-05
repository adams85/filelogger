using System;
using System.Globalization;
using System.Text;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Logging;

namespace CustomLogFormat
{
    internal class SingleLineLogEntryTextBuilder : FileLogEntryTextBuilder
    {
        public static readonly SingleLineLogEntryTextBuilder Default = new SingleLineLogEntryTextBuilder();

        protected override void AppendTimestamp(StringBuilder sb, DateTimeOffset timestamp)
        {
            sb.Append(" @ ").Append(timestamp.ToLocalTime().ToString("o", CultureInfo.InvariantCulture));
        }

        protected override void AppendLogScopeInfo(StringBuilder sb, IExternalScopeProvider scopeProvider)
        {
            scopeProvider.ForEachScope((scope, builder) =>
            {
                builder.Append(' ');

                AppendLogScope(builder, scope);
            }, sb);
        }

        protected override void AppendMessage(StringBuilder sb, string message)
        {
            sb.Append(" => ");

            var length = sb.Length;
            sb.AppendLine(message);
            sb.Replace(Environment.NewLine, " ", length, message.Length);
        }
    }
}
