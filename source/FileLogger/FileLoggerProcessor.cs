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
        void Enqueue(FileLogEntry entry, ILogFileSettings fileSettings, IFileLoggerSettings settings);
    }

    public class FileLoggerProcessor : IFileLoggerProcessor
    {
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
        private readonly CancellationTokenRegistration _completeTokenRegistration;
        private CancellationTokenSource _shutdownTokenSource;
        private bool _isDisposed;

        public FileLoggerProcessor(IFileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            _fallbackFileAppender = new Lazy<PhysicalFileAppender>(() => new PhysicalFileAppender(Environment.CurrentDirectory));

            Context = context;

            _logFiles = new Dictionary<string, LogFileInfo>();

            _shutdownTokenSource = new CancellationTokenSource();
            _completeTokenRegistration = context.CompleteToken.Register(Complete, useSynchronizationContext: false);
        }

        protected virtual void DisposeCore()
        {
            _completeTokenRegistration.Dispose();

            _shutdownTokenSource.Cancel();
            _shutdownTokenSource.Dispose();

            if (_fallbackFileAppender.IsValueCreated)
                _fallbackFileAppender.Value.Dispose();
        }

        public void Dispose()
        {
            lock (_logFiles)
                if (!_isDisposed)
                {
                    DisposeCore();
                    _isDisposed = true;
                }
        }

        protected IFileLoggerContext Context { get; }

        private void Complete()
        {
            CompleteAsync();
        }

        public Task CompleteAsync()
        {
            Task result = CompleteCoreAsync();
            Context.OnComplete(this, result);
            return result;
        }

        private async Task CompleteCoreAsync()
        {
            CancellationTokenSource shutdownTokenSource;
            Task[] completionTasks;

            lock (_logFiles)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FileLoggerProcessor));

                shutdownTokenSource = _shutdownTokenSource;
                _shutdownTokenSource = new CancellationTokenSource();

                completionTasks = _logFiles.Values.Select(lf =>
                {
                    lf.Queue.Complete();
                    return lf.Queue.Completion;
                }).ToArray();

                _logFiles.Clear();
            }

            await Task.WhenAny(Task.WhenAll(completionTasks), Task.Delay(Context.CompletionTimeout)).ConfigureAwait(false);

            shutdownTokenSource.Cancel();
            shutdownTokenSource.Dispose();
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
            CancellationToken shutdownToken = _shutdownTokenSource.Token;
            logFile.Queue = new ActionBlock<FileLogEntry>(
                e => WriteEntryAsync(logFile, e, shutdownToken),
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
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FileLoggerProcessor));

                if (Context.CompleteToken.IsCancellationRequested)
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
            if (fileInfo.Exists &&
                (fileInfo.IsDirectory || fileInfo.Length + logFile.FileEncoding.GetByteCount(entry.Text) > logFile.MaxFileSize))
            {
                logFile.Counter++;
                return false;
            }

            return true;
        }

        protected virtual string GetFilePath(LogFileInfo logFile, FileLogEntry entry)
        {
            string filePath = logFile.GetFilePath(entry, GetDate, GetCounter);

            if (logFile.MaxFileSize > 0 && !CheckFileSize(filePath, logFile, entry))
                filePath = logFile.GetFilePath(entry, GetDate, GetCounter);

            return filePath;
        }

        private async Task WriteEntryAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken shutdownToken)
        {
            // discarding remaining entries on shutdown
            shutdownToken.ThrowIfCancellationRequested();

            var filePath = GetFilePath(logFile, entry);
            IFileInfo fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(filePath);

            for (; ; )
            {
                try
                {
                    await logFile.FileAppender.AppendAllTextAsync(fileInfo, entry.Text, logFile.FileEncoding, shutdownToken).ConfigureAwait(false);
                    return;
                }
                catch
                {
                    try
                    {
                        if (await logFile.FileAppender.EnsureDirAsync(fileInfo, shutdownToken).ConfigureAwait(false))
                        {
                            await logFile.FileAppender.AppendAllTextAsync(fileInfo, entry.Text, logFile.FileEncoding, shutdownToken).ConfigureAwait(false);
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

                // discarding failed entry on shutdown
                if (Context.WriteRetryDelay > TimeSpan.Zero)
                    await Task.Delay(Context.WriteRetryDelay, shutdownToken).ConfigureAwait(false);
                else
                    shutdownToken.ThrowIfCancellationRequested();
            }
        }
    }
}
