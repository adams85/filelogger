using System;
using System.Globalization;
using System.Text;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.Logging;

namespace CustomLogFormat;

internal sealed class SingleLineLogEntryTextBuilder : FileLogEntryTextBuilder
{
    public static readonly SingleLineLogEntryTextBuilder Default = new();

    protected override void AppendTimestamp(StringBuilder sb, DateTimeOffset timestamp, bool useUtcTimestamp)
    {
        var dateTime = useUtcTimestamp ? timestamp.UtcDateTime : timestamp.LocalDateTime;
        sb.Append(" @ ").Append(CultureInfo.InvariantCulture, $"{dateTime:o}");
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

        int length = sb.Length;
        sb.AppendLine(message);
        sb.Replace(Environment.NewLine, " ", length, message.Length);
    }
}
