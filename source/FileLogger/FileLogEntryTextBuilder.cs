using System;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File;

public interface IFileLogEntryTextBuilder
{
    void BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception,
        IExternalScopeProvider scopeProvider, DateTimeOffset timestamp);
}

public class FileLogEntryTextBuilder : IFileLogEntryTextBuilder
{
    public static readonly FileLogEntryTextBuilder Instance = new();

    private readonly string _messagePadding;
    private readonly string _newLineWithMessagePadding;

    protected FileLogEntryTextBuilder()
    {
        _messagePadding = new string(' ', MessagePaddingWidth);
        _newLineWithMessagePadding = Environment.NewLine + _messagePadding;
    }

    protected virtual int MessagePaddingWidth => 6;

    protected virtual string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null),
        };
    }

    protected virtual void AppendLogLevel(StringBuilder sb, LogLevel logLevel)
    {
        sb.Append(GetLogLevelString(logLevel)).Append(": ");
    }

    protected virtual void AppendCategoryName(StringBuilder sb, string categoryName)
    {
        sb.Append(categoryName);
    }

    protected virtual void AppendEventId(StringBuilder sb, EventId eventId)
    {
        sb.Append('[').Append(eventId).Append(']');
    }

    protected virtual void AppendTimestamp(StringBuilder sb, DateTimeOffset timestamp)
    {
        sb.Append(" @ ").AppendLine(timestamp.ToLocalTime().ToString("o", CultureInfo.InvariantCulture));
    }

    protected virtual void AppendLogScope(StringBuilder sb, object scope)
    {
        sb.Append("=> ").Append(scope);
    }

    protected virtual void AppendLogScopeInfo(StringBuilder sb, IExternalScopeProvider scopeProvider)
    {
        int initialLength = sb.Length;

        scopeProvider.ForEachScope((scope, state) =>
        {
            (StringBuilder builder, int length) = state;

            bool first = length == builder.Length;
            if (!first)
                builder.Append(' ');

            AppendLogScope(builder, scope);
        }, (sb, initialLength));

        if (sb.Length > initialLength)
        {
            sb.Insert(initialLength, _messagePadding);
            sb.AppendLine();
        }
    }

    protected virtual void AppendMessage(StringBuilder sb, string message)
    {
        sb.Append(_messagePadding);

        int length = sb.Length;
        sb.AppendLine(message);
        sb.Replace(Environment.NewLine, _newLineWithMessagePadding, length, message.Length);
    }

    protected virtual void AppendException(StringBuilder sb, Exception exception)
    {
        sb.AppendLine(exception.ToString());
    }

    public virtual void BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception,
        IExternalScopeProvider scopeProvider, DateTimeOffset timestamp)
    {
        AppendLogLevel(sb, logLevel);

        AppendCategoryName(sb, categoryName);

        AppendEventId(sb, eventId);

        AppendTimestamp(sb, timestamp);

        if (scopeProvider is not null)
            AppendLogScopeInfo(sb, scopeProvider);

        if (!string.IsNullOrEmpty(message))
            AppendMessage(sb, message);

        if (exception is not null)
            AppendException(sb, exception);
    }
}

public abstract class StructuredFileLogEntryTextBuilder : IFileLogEntryTextBuilder
{
    public abstract void BuildEntryText<TState>(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, TState state, Exception exception,
        IExternalScopeProvider scopeProvider, DateTimeOffset timestamp);

    void IFileLogEntryTextBuilder.BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception,
        IExternalScopeProvider scopeProvider, DateTimeOffset timestamp)
    {
        BuildEntryText<object>(sb, categoryName, logLevel, eventId, message, null, exception, scopeProvider, timestamp);
    }
}
