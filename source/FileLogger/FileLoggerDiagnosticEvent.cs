using System;

namespace Karambolo.Extensions.Logging.File;

public interface IFileLoggerDiagnosticEvent
{
    object Source { get; }
    FormattableString FormattableMessage { get; }
    Exception? Exception { get; }
}

internal static class FileLoggerDiagnosticEvent
{
    internal readonly struct QueuesCompletionForced : IFileLoggerDiagnosticEvent
    {
        internal QueuesCompletionForced(object source)
        {
            Source = source;
        }

        public object Source { get; }
        public FormattableString FormattableMessage => $"Log file queues were not completed within the allowed time limit. Forcing completion.";
        public Exception? Exception => null;

        public override string ToString() => FormattableMessage.ToString();
    }

    internal readonly struct LogEntryDropped : IFileLoggerDiagnosticEvent
    {
        private readonly FileLoggerProcessor.LogFileInfo _logFile;
        private readonly FileLogEntry _logEntry;

        internal LogEntryDropped(object source, FileLoggerProcessor.LogFileInfo logFile, FileLogEntry logEntry)
        {
            Source = source;
            _logFile = logFile;
            _logEntry = logEntry;
        }

        public object Source { get; }
        public FormattableString FormattableMessage => $"Log entry '{_logEntry.Text}' created at {_logEntry.Timestamp} was dropped because the queue of log file \"{_logFile.PathFormat}\" was full.";
        public Exception? Exception => null;

        public override string ToString() => FormattableMessage.ToString();
    }

    internal readonly struct LogEntryWriteFailed : IFileLoggerDiagnosticEvent
    {
        private readonly FileLoggerProcessor.LogFileInfo _logFile;
        private readonly FileLogEntry _logEntry;

        internal LogEntryWriteFailed(object source, FileLoggerProcessor.LogFileInfo logFile, FileLogEntry logEntry, Exception exception)
        {
            Source = source;
            _logFile = logFile;
            _logEntry = logEntry;
            Exception = exception;
        }

        public object Source { get; }
        public Exception Exception { get; }

        public FormattableString FormattableMessage => $"Writing log entry '{_logEntry.Text}' created at {_logEntry.Timestamp} to log file \"{_logFile.PathFormat}\" failed. {Exception}";

        public override string ToString() => FormattableMessage.ToString();
    }
}
