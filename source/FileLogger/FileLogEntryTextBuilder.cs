using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLogEntryTextBuilder
    {
        void BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception,
            FileLogScope logScope, DateTimeOffset timestamp);
    }

    public class FileLogEntryTextBuilder : IFileLogEntryTextBuilder
    {
        public static readonly FileLogEntryTextBuilder Instance = new FileLogEntryTextBuilder();

        readonly string _messagePadding;
        readonly string _newLineWithMessagePadding;

        protected FileLogEntryTextBuilder()
        {
            _messagePadding = new string(' ', MessagePaddingWidth);
            _newLineWithMessagePadding = Environment.NewLine + _messagePadding;
        }

        protected virtual int MessagePaddingWidth => 6;

        protected virtual string GetLogLevelString(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                    return "trce";
                case LogLevel.Debug:
                    return "dbug";
                case LogLevel.Information:
                    return "info";
                case LogLevel.Warning:
                    return "warn";
                case LogLevel.Error:
                    return "fail";
                case LogLevel.Critical:
                    return "crit";
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel));
            }
        }

        protected virtual void AppendLogLevel(StringBuilder sb, LogLevel logLevel)
        {
            sb.Append(GetLogLevelString(logLevel));
            sb.Append(": ");
        }

        protected virtual void AppendCategoryName(StringBuilder sb, string categoryName)
        {
            sb.Append(categoryName);
        }

        protected virtual void AppendEventId(StringBuilder sb, EventId eventId)
        {
            sb.Append('[');
            sb.Append(eventId);
            sb.Append(']');
        }

        protected virtual void AppendTimestamp(StringBuilder sb, DateTimeOffset timestamp)
        {
            sb.Append(" @ ");
            sb.AppendLine(timestamp.ToLocalTime().ToString("o", CultureInfo.InvariantCulture));
        }

        protected virtual void AppendLogScope(StringBuilder sb, FileLogScope logScope)
        {
            sb.Append("=> ");
            sb.Append(logScope);
        }

        protected virtual void AppendLogScopeInfo(StringBuilder sb, FileLogScope logScope)
        {
            sb.Append(_messagePadding);

            var list = new List<FileLogScope>();
            do { list.Add(logScope); }
            while ((logScope = logScope.Parent) != null);

            var n = list.Count;

            AppendLogScope(sb, list[n - 1]);
            for (var i = n - 2; i >= 0; i--)
            {
                sb.Append(' ');
                AppendLogScope(sb, list[i]);
            }

            sb.AppendLine();          
        }

        protected virtual void AppendMessage(StringBuilder sb, string message)
        {
            sb.Append(_messagePadding);

            var length = sb.Length;
            sb.AppendLine(message);
            sb.Replace(Environment.NewLine, _newLineWithMessagePadding, length, message.Length);
        }

        protected virtual void AppendException(StringBuilder sb, Exception exception)
        {
            sb.AppendLine(exception.ToString());
        }

        public virtual void BuildEntryText(StringBuilder sb, string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception,
            FileLogScope logScope, DateTimeOffset timestamp)
        {
            AppendLogLevel(sb, logLevel);

            AppendCategoryName(sb, categoryName);

            AppendEventId(sb, eventId);

            AppendTimestamp(sb, timestamp);

            if (logScope != null)
                AppendLogScopeInfo(sb, logScope);

            if (!string.IsNullOrEmpty(message))
                AppendMessage(sb, message);

            if (exception != null)
                AppendException(sb, exception);
        }
    }
}
