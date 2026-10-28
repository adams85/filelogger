using System;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File;

public interface IFileLogEntryTextBuilder
{
    void BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string? message, Exception? exception,
        IExternalScopeProvider? scopeProvider, DateTimeOffset timestamp);
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
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, message: null),
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
        sb.Append('[');

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
        Span<char> span = stackalloc char[11];
        if (eventId.Id.TryFormat(span, out int charsWritten, provider: CultureInfo.InvariantCulture))
        {
            sb.Append(span.Slice(0, charsWritten));
        }
        else
#endif
        {
            sb.Append(eventId.Id.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(']');
    }

    protected virtual void AppendTimestamp(StringBuilder sb, DateTimeOffset timestamp, bool useUtcTimestamp)
    {
        var dateTime = useUtcTimestamp ? timestamp.UtcDateTime : timestamp.LocalDateTime;

        sb.Append(" @ ");

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
        Span<char> span = stackalloc char[33];
        if (dateTime.TryFormat(span, out int charsWritten, "o", CultureInfo.InvariantCulture))
        {
            sb.Append(span.Slice(0, charsWritten)).AppendLine();
        }
        else
#endif
        {
            sb.AppendLine(dateTime.ToString("o", CultureInfo.InvariantCulture));
        }
    }

    protected virtual void AppendLogScope(StringBuilder sb, object? scope)
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

    public virtual void BuildEntryText(StringBuilder sb, in FileLogEntryTextBuildContext context)
    {
        AppendLogLevel(sb, context.LogLevel);

        AppendCategoryName(sb, context.CategoryName);

        AppendEventId(sb, context.EventId);

        AppendTimestamp(sb, context.Timestamp, context.UseUtcTimestamp);

        if (context.ScopeProvider is { } scopeProvider)
            AppendLogScopeInfo(sb, scopeProvider);

        if (context.Message is { Length: > 0 } message)
            AppendMessage(sb, message);

        if (context.Exception is { } exception)
            AppendException(sb, exception);
    }

    void IFileLogEntryTextBuilder.BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string? message, Exception? exception,
        IExternalScopeProvider? scopeProvider, DateTimeOffset timestamp)
    {
        var context = new FileLogEntryTextBuildContext(categoryName, logLevel, eventId, message, exception, scopeProvider, timestamp, useUtcTimestamp: false);
        BuildEntryText(sb, context);
    }
}

public abstract class StructuredFileLogEntryTextBuilder : IFileLogEntryTextBuilder
{
    public abstract void BuildEntryText<TState>(StringBuilder sb, in FileLogEntryTextBuildContext context, TState state);

    void IFileLogEntryTextBuilder.BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string? message, Exception? exception,
        IExternalScopeProvider? scopeProvider, DateTimeOffset timestamp)
    {
        var context = new FileLogEntryTextBuildContext(categoryName, logLevel, eventId, message, exception, scopeProvider, timestamp, useUtcTimestamp: false);
        BuildEntryText(sb, context, state: (object?)null);
    }
}

public readonly struct FileLogEntryTextBuildContext
{
    public FileLogEntryTextBuildContext(string categoryName, LogLevel logLevel, EventId eventId, string? message, Exception? exception,
        IExternalScopeProvider? scopeProvider, DateTimeOffset timestamp, bool useUtcTimestamp)
    {
        CategoryName = categoryName;
        LogLevel = logLevel;
        EventId = eventId;
        Message = message;
        Exception = exception;
        ScopeProvider = scopeProvider;
        Timestamp = timestamp;
        UseUtcTimestamp = useUtcTimestamp;
    }

    public string CategoryName { get; }
    public LogLevel LogLevel { get; }
    public EventId EventId { get; }
    public string? Message { get; }
    public Exception? Exception { get; }
    public IExternalScopeProvider? ScopeProvider { get; }
    public DateTimeOffset Timestamp { get; }
    public bool UseUtcTimestamp { get; }
}
