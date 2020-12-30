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
            TryOpenFile,
            RetryOpenFile,
            Write,
            Idle
        }

        protected internal partial class LogFileInfo
        {
            private Stream _appendStream;

            public LogFileInfo(FileLoggerProcessor processor, ILogFileSettings fileSettings, IFileLoggerSettings settings)
            {
                BasePath = settings.BasePath ?? string.Empty;
                PathFormat = fileSettings.Path;
                FileAppender = settings.FileAppender ?? processor._fallbackFileAppender.Value;
                AccessMode = fileSettings.FileAccessMode ?? settings.FileAccessMode ?? LogFileAccessMode.Default;
                Encoding = fileSettings.FileEncoding ?? settings.FileEncoding ?? Encoding.UTF8;
                DateFormat = fileSettings.DateFormat ?? settings.DateFormat ?? "yyyyMMdd";
                CounterFormat = fileSettings.CounterFormat ?? settings.CounterFormat;
                MaxSize = fileSettings.MaxFileSize ?? settings.MaxFileSize ?? 0;

                Queue = processor.CreateLogFileQueue(fileSettings, settings);

                // important: closure must pick up the current token!
                CancellationToken forcedCompleteToken = processor._forcedCompleteTokenSource.Token;
                WriteFileTask = Task.Run(() => processor.WriteFileAsync(this, forcedCompleteToken));
            }

            public string BasePath { get; }
            public string PathFormat { get; }
            public IFileAppender FileAppender { get; }
            public LogFileAccessMode AccessMode { get; }
            public Encoding Encoding { get; }
            public string DateFormat { get; }
            public string CounterFormat { get; }
            public int MaxSize { get; }

            public Channel<FileLogEntry> Queue { get; }
            public Task WriteFileTask { get; }

            public int Counter { get; set; }
            public string CurrentPath { get; set; }

            public bool IsOpen => _appendStream != null;
            public long? Size => _appendStream?.Length;

            internal bool ShouldEnsurePreamble { get; private set; }

            internal void Open(IFileInfo fileInfo)
            {
                _appendStream = FileAppender.CreateAppendStream(fileInfo);

                ShouldEnsurePreamble = true;
            }

            internal async ValueTask EnsurePreambleAsync(CancellationToken cancellationToken)
            {
                if (_appendStream.Length == 0)
                    await WriteBytesAsync(Encoding.GetPreamble(), cancellationToken).ConfigureAwait(false);

                ShouldEnsurePreamble = false;
            }

            internal void Flush()
            {
                // FlushAsync is extremely slow currently
                // https://github.com/dotnet/corefx/issues/32837
                _appendStream.Flush();
            }

            internal void Close()
            {
                Stream appendStream = _appendStream;
                _appendStream = null;
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

                    if (logFile.IsOpen)
                        logFile.Close();
                }).ToArray();

                _logFiles.Clear();

                onQueuesCompleted?.Invoke();

                if (complete)
                    _status = Status.Completing;
            }

            try
            {
                var completionTimeoutTask = Task.Delay(Context.CompletionTimeout);
                if ((await Task.WhenAny(Task.WhenAll(completionTasks), completionTimeoutTask).ConfigureAwait(false)) == completionTimeoutTask)
                    Context.ReportDiagnosticEvent(new FileLoggerDiagnosticEvent.QueuesCompletionForced(this));

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

        protected virtual LogFileInfo CreateLogFile(ILogFileSettings fileSettings, IFileLoggerSettings settings)
        {
            return new LogFileInfo(this, fileSettings, settings);
        }

        protected virtual Channel<FileLogEntry> CreateLogFileQueue(ILogFileSettings fileSettings, IFileLoggerSettings settings)
        {
            var maxQueueSize = fileSettings.MaxQueueSize ?? settings.MaxQueueSize ?? 0;

            return
                maxQueueSize > 0 ?
                Channel.CreateBounded<FileLogEntry>(ConfigureChannelOptions(new BoundedChannelOptions(maxQueueSize)
                {
                    FullMode = BoundedChannelFullMode.DropWrite
                })) :
                Channel.CreateUnbounded<FileLogEntry>(ConfigureChannelOptions(new UnboundedChannelOptions()));

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

            if (!logFile.Queue.Writer.TryWrite(entry))
                Context.ReportDiagnosticEvent(new FileLoggerDiagnosticEvent.LogEntryDropped(this, logFile, entry));
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
            if (!logFile.IsOpen || logFile.CurrentPath != filePath)
            {
                IFileInfo fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(Path.Combine(logFile.BasePath, filePath));

                if (!fileInfo.Exists)
                    return true;

                if (fileInfo.IsDirectory)
                    return false;

                currentFileSize = fileInfo.Length;
            }
            else
                currentFileSize = logFile.Size.Value;

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
                            if (UpdateFilePath(logFile, entry, cancellationToken) && logFile.IsOpen)
                                logFile.Close();

                            if (!logFile.IsOpen)
                            {
                                // GetFileInfo behavior regarding invalid filenames is inconsistent across .NET runtimes (and operating systems?)
                                // e.g. PhysicalFileProvider returns NotFoundFileInfo in .NET 5 but throws an exception in previous versions on Windows
                                fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(Path.Combine(logFile.BasePath, logFile.CurrentPath));
                                state = WriteEntryState.TryOpenFile;
                            }
                            else
                                state =  WriteEntryState.Write;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            ReportFailure(logFile, entry, ex);

                            // discarding entry when file path is invalid
                            if (logFile.CurrentPath.IndexOfAny(s_invalidPathChars.Value) >= 0)
                                return;

                            state = WriteEntryState.Idle;
                        }
                        break;
                    case WriteEntryState.TryOpenFile:
                        try
                        {
                            logFile.Open(fileInfo);

                            state = WriteEntryState.Write;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            state = WriteEntryState.RetryOpenFile;
                        }
                        break;
                    case WriteEntryState.RetryOpenFile:
                        try
                        {
                            await logFile.FileAppender.EnsureDirAsync(fileInfo, cancellationToken).ConfigureAwait(false);
                            logFile.Open(fileInfo);

                            state = WriteEntryState.Write;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            ReportFailure(logFile, entry, ex);

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
                                if (logFile.ShouldEnsurePreamble)
                                    await logFile.EnsurePreambleAsync(cancellationToken).ConfigureAwait(false);

                                await logFile.WriteBytesAsync(logFile.Encoding.GetBytes(entry.Text), cancellationToken).ConfigureAwait(false);

                                if (logFile.AccessMode == LogFileAccessMode.KeepOpenAndAutoFlush)
                                    logFile.Flush();
                            }
                            finally
                            {
                                if (logFile.AccessMode == LogFileAccessMode.OpenTemporarily)
                                    logFile.Close();
                            }

                            return;
                        }
                        catch (Exception ex) when (!(ex is OperationCanceledException))
                        {
                            ReportFailure(logFile, entry, ex);

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

            void ReportFailure(LogFileInfo logFile, FileLogEntry entry, Exception exception)
            {
                Context.ReportDiagnosticEvent(new FileLoggerDiagnosticEvent.LogEntryWriteFailed(this, logFile, entry, exception));
            }
        }

        private async Task WriteFileAsync(LogFileInfo logFile, CancellationToken cancellationToken)
        {
            ChannelReader<FileLogEntry> queue = logFile.Queue.Reader;
            while (await queue.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                while (queue.TryRead(out FileLogEntry entry))
                    await WriteEntryAsync(logFile, entry, cancellationToken).ConfigureAwait(false);
        }
    }
}
