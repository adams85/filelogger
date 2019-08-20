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

        private enum WriteEntryState
        {
            CheckFile,
            TryCreateStream,
            RetryCreateStream,
            Write,
            Idle
        }

        protected class LogFileInfo
        {
            public string BasePath { get; set; }
            public string PathFormat { get; set; }
            public IFileAppender FileAppender { get; set; }
            public LogFileAccessMode AccessMode { get; set; }
            public Encoding Encoding { get; set; }
            public string DateFormat { get; set; }
            public string CounterFormat { get; set; }
            public int MaxSize { get; set; }

            public ActionBlock<FileLogEntry> Queue { get; set; }

            public int Counter { get; set; }
            public string CurrentPath { get; set; }
            public Stream AppendStream { get; set; }

            public async Task CloseAppendStreamAsync(CancellationToken cancellationToken)
            {
                Stream writeStream = AppendStream;
                AppendStream = null;
                try { await writeStream.FlushAsync(cancellationToken).ConfigureAwait(false); }
                finally { writeStream.Dispose(); }
            }
        }

        private static readonly Lazy<char[]> s_invalidPathChars = new Lazy<char[]>(() => Path.GetInvalidPathChars()
            .Concat(Path.GetInvalidFileNameChars())
            .Except(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
            .ToArray());

        private readonly Lazy<PhysicalFileAppender> _fallbackFileAppender;
        private readonly Dictionary<string, LogFileInfo> _logFiles;
        private readonly TaskCompletionSource<object> _completeTaskCompletionSource;
        private readonly CancellationTokenRegistration _completeTokenRegistration;
        private CancellationTokenSource _forcedCompleteTokenSource;
        private Status _status;

        public FileLoggerProcessor(FileLoggerContext context)
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

        public FileLoggerContext Context { get; }

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

                completionTasks = _logFiles.Values.Select(async logFile =>
                {
                    logFile.Queue.Complete();

                    await logFile.Queue.Completion.ConfigureAwait(false);

                    if (logFile.AppendStream != null)
                        await logFile.CloseAppendStreamAsync(forcedCompleteTokenSource.Token).ConfigureAwait(false);
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
            await CompleteAsync().ConfigureAwait(false);
        }

        protected virtual LogFileInfo CreateLogFile()
        {
            return new LogFileInfo();
        }

        protected virtual LogFileInfo CreateLogFile(ILogFileSettings fileSettings, IFileLoggerSettings settings)
        {
            LogFileInfo logFile = CreateLogFile();
            logFile.BasePath = settings.BasePath ?? string.Empty;
            logFile.PathFormat = fileSettings.Path;
            logFile.FileAppender = settings.FileAppender ?? _fallbackFileAppender.Value;
            logFile.AccessMode = fileSettings.FileAccessMode ?? settings.FileAccessMode ?? LogFileAccessMode.Default;
            logFile.Encoding = fileSettings.FileEncoding ?? settings.FileEncoding ?? Encoding.UTF8;
            logFile.DateFormat = fileSettings.DateFormat ?? settings.DateFormat ?? "yyyyMMdd";
            logFile.CounterFormat = fileSettings.CounterFormat ?? settings.CounterFormat;
            logFile.MaxSize = fileSettings.MaxFileSize ?? settings.MaxFileSize ?? 0;

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

        protected virtual string GetDate(string inlineFormat, LogFileInfo logFile, FileLogEntry entry)
        {
            return entry.Timestamp.ToLocalTime().ToString(inlineFormat ?? logFile.DateFormat, CultureInfo.InvariantCulture);
        }

        protected virtual string GetCounter(string inlineFormat, LogFileInfo logFile, FileLogEntry entry)
        {
            return logFile.Counter.ToString(inlineFormat ?? logFile.CounterFormat, CultureInfo.InvariantCulture);
        }

        protected virtual bool CheckFileSize(string filePath, LogFileInfo logFile, FileLogEntry entry)
        {
            long currentFileSize;
            if (logFile.CurrentPath != filePath || logFile.AppendStream == null)
            {
                IFileInfo fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(Path.Combine(logFile.BasePath, filePath));

                if (!fileInfo.Exists)
                    return true;

                if (fileInfo.IsDirectory)
                    return false;

                currentFileSize = fileInfo.Length;
            }
            else
                currentFileSize = logFile.AppendStream.Length;

            long expectedFileSize = currentFileSize > 0 ? currentFileSize : logFile.Encoding.GetPreamble().Length;
            expectedFileSize += logFile.Encoding.GetByteCount(entry.Text);

            return expectedFileSize <= logFile.MaxSize;
        }

        protected virtual string FormatFilePath(LogFileInfo logFile, FileLogEntry entry)
        {
            return Regex.Replace(logFile.PathFormat, @"<(date|counter)(?::([^<>]+))?>", match =>
            {
                var inlineFormat = match.Groups[2].Value;

                switch (match.Groups[1].Value)
                {
                    case "date": return GetDate(inlineFormat.Length > 0 ? inlineFormat : null, logFile, entry);
                    case "counter": return GetCounter(inlineFormat.Length > 0 ? inlineFormat : null, logFile, entry);
                    default: throw new InvalidOperationException();
                }
            });
        }

        protected virtual bool UpdateFilePath(LogFileInfo logFile, FileLogEntry entry, CancellationToken cancellationToken)
        {
            string filePath = FormatFilePath(logFile, entry);

            if (logFile.MaxSize > 0)
                while (!CheckFileSize(filePath, logFile, entry))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    logFile.Counter++;
                    var newFilePath = FormatFilePath(logFile, entry);

                    if (filePath == newFilePath)
                        break;

                    filePath = newFilePath;
                }


            if (logFile.CurrentPath == filePath)
                return false;

            logFile.CurrentPath = filePath;
            return true;
        }

        protected virtual async Task WriteEntryCoreAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken cancellationToken)
        {
            if (logFile.AppendStream.Length == 0)
            {
                var preamble = logFile.Encoding.GetPreamble();
                await logFile.AppendStream.WriteAsync(preamble, 0, preamble.Length, cancellationToken).ConfigureAwait(false);
            }

            var data = logFile.Encoding.GetBytes(entry.Text);
            await logFile.AppendStream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteEntryAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken cancellationToken)
        {
            // discarding remaining entries on forced complete
            cancellationToken.ThrowIfCancellationRequested();

            WriteEntryState state = WriteEntryState.CheckFile;
            IFileInfo fileInfo = null;
            for (; ; )
                switch (state)
                {
                    case WriteEntryState.CheckFile:
                        try
                        {
                            if (UpdateFilePath(logFile, entry, cancellationToken) && logFile.AppendStream != null)
                                await logFile.CloseAppendStreamAsync(cancellationToken).ConfigureAwait(false);

                            state = logFile.AppendStream == null ? WriteEntryState.TryCreateStream : WriteEntryState.Write;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            state = WriteEntryState.Idle;
                        }
                        break;
                    case WriteEntryState.TryCreateStream:
                        try
                        {
                            fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(Path.Combine(logFile.BasePath, logFile.CurrentPath));
                            logFile.AppendStream = logFile.FileAppender.CreateAppendStream(fileInfo);

                            state = WriteEntryState.Write;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            state = WriteEntryState.RetryCreateStream;
                        }
                        break;
                    case WriteEntryState.RetryCreateStream:
                        try
                        {
                            if (await logFile.FileAppender.EnsureDirAsync(fileInfo, cancellationToken).ConfigureAwait(false))
                            {
                                logFile.AppendStream = logFile.FileAppender.CreateAppendStream(fileInfo);
                                state = WriteEntryState.Write;
                            }
                            else
                                state = WriteEntryState.Idle;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            // discarding entry when file path is invalid
                            if (logFile.CurrentPath.IndexOfAny(s_invalidPathChars.Value) >= 0)
                                return;

                            state = WriteEntryState.Idle;
                        }
                        break;
                    case WriteEntryState.Write:
                        try
                        {
                            try
                            {
                                await WriteEntryCoreAsync(logFile, entry, cancellationToken).ConfigureAwait(false);

                                if (logFile.AccessMode == LogFileAccessMode.KeepOpenAndAutoFlush)
                                    await logFile.AppendStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                if (logFile.AccessMode == LogFileAccessMode.OpenTemporarily)
                                    await logFile.CloseAppendStreamAsync(cancellationToken).ConfigureAwait(false);
                            }

                            return;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            state = WriteEntryState.Idle;
                        }
                        break;
                    case WriteEntryState.Idle:
                        // discarding failed entry on forced complete
                        if (Context.WriteRetryDelay > TimeSpan.Zero)
                            await Task.Delay(Context.WriteRetryDelay, cancellationToken).ConfigureAwait(false);
                        else
                            cancellationToken.ThrowIfCancellationRequested();

                        state = WriteEntryState.CheckFile;
                        break;
                }
        }
    }
}
