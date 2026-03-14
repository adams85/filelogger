using System;
using System.Runtime.CompilerServices;
using Karambolo.Extensions.Logging.File.Properties;

#pragma warning disable CA1305 // Specify IFormatProvider

namespace Karambolo.Extensions.Logging.File;

public interface IFileLoggerDiagnosticEvent
{
    object Source { get; }
    FormattableString FormattableMessage { get; }
    Exception? Exception { get; }
}

internal static class FileLoggerDiagnosticEvent
{
    internal sealed class QueuesCompletionForced : IFileLoggerDiagnosticEvent
    {
        internal QueuesCompletionForced(FileLoggerProcessor source)
        {
            Source = source;
        }

        public object Source { get; }

        public FormattableString FormattableMessage =>
            FormattableStringFactory.Create(Resources.DiagnosticEvent_QueuesCompletionForced);

        public Exception? Exception => null;

        public override string ToString() => FormattableMessage.ToString();
    }

    internal sealed class LogEntryDropped : IFileLoggerDiagnosticEvent
    {
        public static LogEntryDropped Disposing(FileLoggerProcessor source, string logFile, in FileLogEntry logEntry) =>
            new LogEntryDropped(source, logFile, logEntry, () => Resources.DiagnosticEvent_LogEntryDropped_Disposing);

        public static LogEntryDropped Disposing(FileLoggerProcessor source, FileLoggerProcessor.LogFileInfo logFile, in FileLogEntry logEntry) =>
            new LogEntryDropped(source, logFile, logEntry, () => Resources.DiagnosticEvent_LogEntryDropped_Disposing);

        public static LogEntryDropped Failure(FileLoggerProcessor source, FileLoggerProcessor.LogFileInfo logFile, in FileLogEntry logEntry) =>
            new LogEntryDropped(source, logFile, logEntry, () => Resources.DiagnosticEvent_LogEntryDropped_Failure);

        public static LogEntryDropped QueueFull(FileLoggerProcessor source, FileLoggerProcessor.LogFileInfo logFile, in FileLogEntry logEntry) =>
            new LogEntryDropped(source, logFile, logEntry, () => Resources.DiagnosticEvent_LogEntryDropped_QueueFull);

        private readonly object _logFile; // FileLoggerProcessor.LogFileInfo | string
        private readonly FileLogEntry _logEntry;
        private readonly Func<string> _messageFormatAccessor;

        private LogEntryDropped(FileLoggerProcessor source, object logFile, in FileLogEntry logEntry, Func<string> messageFormatAccessor)
        {
            Source = source;
            _logEntry = logEntry;
            _logFile = logFile;
            _messageFormatAccessor = messageFormatAccessor;
        }

        public object Source { get; }

        public FormattableString FormattableMessage =>
            FormattableStringFactory.Create(_messageFormatAccessor(), _logEntry.Text, _logEntry.Timestamp,
                _logFile is FileLoggerProcessor.LogFileInfo logFile ? logFile.PathFormat : _logFile);

        public Exception? Exception => null;

        public override string ToString() => FormattableMessage.ToString();
    }

    internal sealed class LogEntryWriteFailed : IFileLoggerDiagnosticEvent
    {
        private readonly FileLoggerProcessor.LogFileInfo _logFile;
        private readonly FileLogEntry _logEntry;

        internal LogEntryWriteFailed(FileLoggerProcessor source, FileLoggerProcessor.LogFileInfo logFile, in FileLogEntry logEntry, Exception exception)
        {
            Source = source;
            _logFile = logFile;
            _logEntry = logEntry;
            Exception = exception;
        }

        public object Source { get; }

        public Exception Exception { get; }

        public FormattableString FormattableMessage =>
            FormattableStringFactory.Create(Resources.DiagnosticEvent_LogEntryWriteFailed, _logEntry.Text, _logEntry.Timestamp, _logFile.PathFormat, Exception);

        public override string ToString() => FormattableMessage.ToString();
    }
}
