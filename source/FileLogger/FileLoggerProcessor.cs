using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerProcessor : IDisposable
    {
        Task Completion { get; }

        void Enqueue(FileLogEntry entry, ILogFileSettings fileSettings, IFileLoggerSettings settings);

        Task ResetAsync(Action onQueuesCompleted = null);
        Task CompleteAsync();
    }

    public class FileLoggerProcessor : IFileLoggerProcessor
    {
        private enum Status
        {
            Running,
            Completing,
            Completed,
        }

        protected class LogFileInfo
        {
            public string BasePath { get; set; }
            public string Path { get; set; }
            public IFileAppender FileAppender { get; set; }
            public Encoding FileEncoding { get; set; }
            public string DateFormat { get; set; }
            public string CounterFormat { get; set; }
            public int MaxFileSize { get; set; }

            public ActionBlock<FileLogEntry> Queue { get; set; }

            public int Counter { get; set; }

            public string GetFilePath(FileLogEntry entry, Func<LogFileInfo, FileLogEntry, string> getDate, Func<LogFileInfo, FileLogEntry, string> getCounter)
            {
                var formattedPath = Regex.Replace(Path, @"<(date|counter)>", match =>
                {
                    switch (match.Groups[1].Value)
                    {
                        case "date": return getDate(this, entry);
                        case "counter": return getCounter(this, entry);
                        default: throw new InvalidOperationException();
                    }
                });

                return System.IO.Path.Combine(BasePath, formattedPath);
            }
        }

        private readonly Lazy<PhysicalFileAppender> _fallbackFileAppender;
        private readonly Dictionary<string, LogFileInfo> _logFiles;
        private readonly TaskCompletionSource<object> _completeTaskCompletionSource;
        private readonly CancellationTokenRegistration _completeTokenRegistration;
        private CancellationTokenSource _forcedCompleteTokenSource;
        private Status _status;

        public FileLoggerProcessor(IFileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _fallbackFileAppender = new Lazy<PhysicalFileAppender>(() => new PhysicalFileAppender(Environment.CurrentDirectory));

            Context = context;

            _logFiles = new Dictionary<string, LogFileInfo>();

            _completeTaskCompletionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            _forcedCompleteTokenSource = new CancellationTokenSource();

            _completeTokenRegistration = context.CompleteToken.Register(Complete, useSynchronizationContext: false);
        }

        public void Dispose()
        {
            lock (_logFiles)
                if (_status != Status.Completed)
                {
                    _completeTokenRegistration.Dispose();

                    _forcedCompleteTokenSource.Cancel();
                    _forcedCompleteTokenSource.Dispose();

                    _completeTaskCompletionSource.TrySetResult(null);

                    if (_fallbackFileAppender.IsValueCreated)
                        _fallbackFileAppender.Value.Dispose();

                    DisposeCore();

                    _status = Status.Completed;
                }
        }

        protected virtual void DisposeCore() { }

        public IFileLoggerContext Context { get; }

        public Task Completion => _completeTaskCompletionSource.Task;

        private async Task ResetCoreAsync(Action onQueuesCompleted, bool complete)
        {
            CancellationTokenSource forcedCompleteTokenSource;
            Task[] completionTasks;

            lock (_logFiles)
            {
                if (_status != Status.Running)
                    return;

                forcedCompleteTokenSource = _forcedCompleteTokenSource;
                _forcedCompleteTokenSource = new CancellationTokenSource();

                completionTasks = _logFiles.Values.Select(lf =>
                {
                    lf.Queue.Complete();
                    return lf.Queue.Completion;
                }).ToArray();

                _logFiles.Clear();

                onQueuesCompleted?.Invoke();

                if (complete)
                    _status = Status.Completing;
            }

            try
            {
                await Task.WhenAny(Task.WhenAll(completionTasks), Task.Delay(Context.CompletionTimeout)).ConfigureAwait(false);

                forcedCompleteTokenSource.Cancel();
                forcedCompleteTokenSource.Dispose();
            }
            finally
            {
                if (complete)
                    Dispose();
            }
        }

        public Task ResetAsync(Action onQueuesCompleted = null)
        {
            return ResetCoreAsync(onQueuesCompleted, complete: false);
        }

        public Task CompleteAsync()
        {
            return ResetCoreAsync(null, complete: true);
        }

        private async void Complete()
        {
            await CompleteAsync();
        }

        protected virtual LogFileInfo CreateLogFile()
        {
            return new LogFileInfo();
        }

        protected virtual LogFileInfo CreateLogFile(ILogFileSettings fileSettings, IFileLoggerSettings settings)
        {
            LogFileInfo logFile = CreateLogFile();
            logFile.BasePath = settings.BasePath ?? string.Empty;
            logFile.Path = fileSettings.Path;
            logFile.FileAppender = settings.FileAppender ?? _fallbackFileAppender.Value;
            logFile.FileEncoding = fileSettings.FileEncoding ?? settings.FileEncoding ?? Encoding.UTF8;
            logFile.DateFormat = fileSettings.DateFormat ?? settings.DateFormat ?? "yyyyMMdd";
            logFile.CounterFormat = fileSettings.CounterFormat ?? settings.CounterFormat;
            logFile.MaxFileSize = fileSettings.MaxFileSize ?? settings.MaxFileSize ?? 0;

            // important: closure must pick up the current token!
            CancellationToken forcedCompleteToken = _forcedCompleteTokenSource.Token;
            logFile.Queue = new ActionBlock<FileLogEntry>(
                e => WriteEntryAsync(logFile, e, forcedCompleteToken),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1,
                    BoundedCapacity = fileSettings.MaxQueueSize ?? settings.MaxQueueSize ?? -1,
                });

            return logFile;
        }

        public void Enqueue(FileLogEntry entry, ILogFileSettings fileSettings, IFileLoggerSettings settings)
        {
            LogFileInfo logFile;

            lock (_logFiles)
            {
                if (_status == Status.Completed)
                    throw new ObjectDisposedException(nameof(FileLoggerProcessor));

                if (_status != Status.Running)
                    return;

                if (!_logFiles.TryGetValue(fileSettings.Path, out logFile))
                    _logFiles.Add(fileSettings.Path, logFile = CreateLogFile(fileSettings, settings));
            }

            logFile.Queue.Post(entry);
        }

        protected virtual string GetDate(LogFileInfo logFile, FileLogEntry entry)
        {
            return entry.Timestamp.ToLocalTime().ToString(logFile.DateFormat, CultureInfo.InvariantCulture);
        }

        protected virtual string GetCounter(LogFileInfo logFile, FileLogEntry entry)
        {
            return logFile.Counter.ToString(logFile.CounterFormat, CultureInfo.InvariantCulture);
        }

        protected virtual bool CheckFileSize(string filePath, LogFileInfo logFile, FileLogEntry entry)
        {
            IFileInfo fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(filePath);

            if (!fileInfo.Exists)
                return true;

            if (fileInfo.IsDirectory)
                return false;

            long expectedFileSize = fileInfo.Length + logFile.FileEncoding.GetByteCount(entry.Text);
            if (fileInfo.Length == 0)
                expectedFileSize += logFile.FileEncoding.GetPreamble().Length;

            return expectedFileSize <= logFile.MaxFileSize;
        }

        protected virtual string GetFilePath(LogFileInfo logFile, FileLogEntry entry)
        {
            string filePath = logFile.GetFilePath(entry, GetDate, GetCounter);

            if (logFile.MaxFileSize > 0)
                while (!CheckFileSize(filePath, logFile, entry))
                {
                    logFile.Counter++;
                    filePath = logFile.GetFilePath(entry, GetDate, GetCounter);
                }

            return filePath;
        }

        private async Task WriteEntryAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken forcedCompleteToken)
        {
            // discarding remaining entries on forced complete
            forcedCompleteToken.ThrowIfCancellationRequested();

            var filePath = GetFilePath(logFile, entry);
            IFileInfo fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(filePath);

            for (; ; )
            {
                try
                {
                    await logFile.FileAppender.AppendAllTextAsync(fileInfo, entry.Text, logFile.FileEncoding, forcedCompleteToken).ConfigureAwait(false);
                    return;
                }
                catch
                {
                    try
                    {
                        if (await logFile.FileAppender.EnsureDirAsync(fileInfo, forcedCompleteToken).ConfigureAwait(false))
                        {
                            await logFile.FileAppender.AppendAllTextAsync(fileInfo, entry.Text, logFile.FileEncoding, forcedCompleteToken).ConfigureAwait(false);
                            return;
                        }
                    }
                    catch
                    {
                        // discarding entry when file path is invalid
                        if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || filePath.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                            return;
                    }
                }

                // discarding failed entry on forced complete
                if (Context.WriteRetryDelay > TimeSpan.Zero)
                    await Task.Delay(Context.WriteRetryDelay, forcedCompleteToken).ConfigureAwait(false);
                else
                    forcedCompleteToken.ThrowIfCancellationRequested();
            }
        }
    }
}
