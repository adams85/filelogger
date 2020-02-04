using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
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

    public partial class FileLoggerProcessor : IFileLoggerProcessor
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

            public Channel<FileLogEntry> Queue { get; set; }
            public Task WriteFileTask { get; set; }

            public int Counter { get; set; }
            public string CurrentPath { get; set; }

            public Stream AppendStream { get; private set; }
            public Func<LogFileInfo, CancellationToken, ValueTask> EnsurePreambleAsync { get; private set; }

            public void OpenAppendStream(IFileInfo fileInfo)
            {
                AppendStream = FileAppender.CreateAppendStream(fileInfo);

                // the compiler creates a cached delegate for non-capturing lambda expressions,
                // so frequent allocation in the case of LogFileAccessMode.OpenTemporarily can be avoided this way
                EnsurePreambleAsync = async (logFile, cancellationToken) =>
                {
                    if (logFile.AppendStream.Length == 0)
                        await WriteBytesAsync(logFile, logFile.Encoding.GetPreamble(), cancellationToken).ConfigureAwait(false);

                    logFile.EnsurePreambleAsync = (lf, ct) => default;
                };
            }

            public void CloseAppendStream()
            {
                Stream appendStream = AppendStream;
                AppendStream = null;
                appendStream.Dispose();
            }
        }

        private static readonly Lazy<char[]> s_invalidPathChars = new Lazy<char[]>(() => Path.GetInvalidPathChars()
            .Concat(Path.GetInvalidFileNameChars())
            .Except(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
            .ToArray());

        private readonly Lazy<PhysicalFileAppender> _fallbackFileAppender;
        private readonly Dictionary<ILogFileSettings, LogFileInfo> _logFiles;
        private readonly TaskCompletionSource<object> _completeTaskCompletionSource;
        private readonly CancellationTokenRegistration _completeTokenRegistration;
        private CancellationTokenSource _forcedCompleteTokenSource;
        private Status _status;

        public FileLoggerProcessor(FileLoggerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            Context = context;

            _fallbackFileAppender = new Lazy<PhysicalFileAppender>(() => new PhysicalFileAppender(Environment.CurrentDirectory));

            _logFiles = new Dictionary<ILogFileSettings, LogFileInfo>();

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
                    logFile.Queue.Writer.Complete();

                    await logFile.WriteFileTask.ConfigureAwait(false);

                    if (logFile.AppendStream != null)
                        logFile.CloseAppendStream();
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

            var maxQueueSize = fileSettings.MaxQueueSize ?? settings.MaxQueueSize ?? 0;
            logFile.Queue =
                maxQueueSize > 0 ?
                Channel.CreateBounded<FileLogEntry>(ConfigureChannelOptions(new BoundedChannelOptions(maxQueueSize)
                {
                    FullMode = BoundedChannelFullMode.DropWrite
                })) :
                Channel.CreateUnbounded<FileLogEntry>(ConfigureChannelOptions(new UnboundedChannelOptions()));

            // important: closure must pick up the current token!
            CancellationToken forcedCompleteToken = _forcedCompleteTokenSource.Token;
            logFile.WriteFileTask = Task.Run(() => WriteFileAsync(logFile, forcedCompleteToken));

            return logFile;

            static TOptions ConfigureChannelOptions<TOptions>(TOptions options) where TOptions : ChannelOptions
            {
                options.AllowSynchronousContinuations = false;
                options.SingleReader = true;
                options.SingleWriter = false;
                return options;
            }
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

                if (!_logFiles.TryGetValue(fileSettings, out logFile))
                    _logFiles.Add(fileSettings, logFile = CreateLogFile(fileSettings, settings));
            }

            logFile.Queue.Writer.TryWrite(entry);
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
            if (logFile.AppendStream == null || logFile.CurrentPath != filePath)
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

        protected virtual async ValueTask WriteEntryCoreAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken cancellationToken)
        {
            await logFile.EnsurePreambleAsync(logFile, cancellationToken).ConfigureAwait(false);

            await WriteBytesAsync(logFile, logFile.Encoding.GetBytes(entry.Text), cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask WriteEntryAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken cancellationToken)
        {
            WriteEntryState state = WriteEntryState.CheckFile;
            IFileInfo fileInfo = null;
            for (; ; )
                switch (state)
                {
                    case WriteEntryState.CheckFile:
                        try
                        {
                            if (UpdateFilePath(logFile, entry, cancellationToken) && logFile.AppendStream != null)
                                logFile.CloseAppendStream();

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
                            logFile.OpenAppendStream(fileInfo);

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
                                logFile.OpenAppendStream(fileInfo);
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
                                    // FlushAsync is extremely slow currently
                                    // https://github.com/dotnet/corefx/issues/32837
                                    logFile.AppendStream.Flush();
                            }
                            finally
                            {
                                if (logFile.AccessMode == LogFileAccessMode.OpenTemporarily)
                                    logFile.CloseAppendStream();
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

        private async Task WriteFileAsync(LogFileInfo logFile, CancellationToken cancellationToken)
        {
            try
            {
                for (; ; )
                {
                    FileLogEntry entry = await logFile.Queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                    await WriteEntryAsync(logFile, entry, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ChannelClosedException) { }
        }
    }
}
