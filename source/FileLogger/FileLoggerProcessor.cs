using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File;

public interface IFileLoggerProcessor : IDisposable
{
    Task Completion { get; }

    /// <summary>
    /// In the case of <see cref="LogFileWriteStrategy.QueuedAsyncWrite"/> or <see cref="LogFileWriteStrategy.QueuedSyncWrite"/>, adds the log entry to the file's queue
    /// or drops it if the queue is full.<br/>
    /// In the case of <see cref="LogFileWriteStrategy.DirectSyncWrite"/>, writes the log entry directly to the file, bypassing the queue.
    /// Blocks the calling thread until the write operation completes.
    /// </summary>
    void Enqueue(in FileLogEntry entry, ILogFileSettings fileSettings, IFileLoggerSettings settings);

    Task ResetAsync(Action? onQueuesCompleted = null);
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

    protected internal partial class LogFileInfo
    {
        private Stream? _appendStream;

        public LogFileInfo(FileLoggerProcessor processor, ILogFileSettings fileSettings, IFileLoggerSettings settings)
        {
            Debug.Assert(fileSettings.Path is not null);

            BasePath = settings.BasePath ?? string.Empty;
            PathFormat = fileSettings.Path!;
            PathPlaceholderResolver =
                fileSettings.Path!.IndexOf('<') < 0 ? s_noopPathPlaceholderResolver
                : (fileSettings.PathPlaceholderResolver ?? settings.PathPlaceholderResolver) is not { } resolver ? s_defaultPathPlaceholderResolver
                : (placeholderName, inlineFormat, context) =>
                    resolver(placeholderName, inlineFormat, context) ?? s_defaultPathPlaceholderResolver(placeholderName, inlineFormat, context);
            FileAppender = settings.FileAppender ?? processor._fallbackFileAppender.Value;
            AccessMode = fileSettings.FileAccessMode ?? settings.FileAccessMode ?? LogFileAccessMode.Default;
            Encoding = fileSettings.FileEncoding ?? settings.FileEncoding ?? Encoding.UTF8;
            DateFormat = fileSettings.DateFormat ?? settings.DateFormat;
            CounterFormat = fileSettings.CounterFormat ?? settings.CounterFormat;
            MaxSize = fileSettings.MaxFileSize ?? settings.MaxFileSize ?? 0;
            WriteStrategy = fileSettings.WriteStrategy ?? settings.WriteStrategy ?? LogFileWriteStrategy.QueuedAsyncWrite;

            if (WriteStrategy != LogFileWriteStrategy.DirectSyncWrite)
            {
                Queue = processor.CreateLogFileQueue(fileSettings, settings);

                // important: closure must pick up the current processor state!
                Task currentResetTask = processor._resetTask;
                CancellationToken forcedCompleteToken = processor._forcedCompleteTokenSource.Token;
                WriteFileTask = Task.Run(() => processor.WriteFileAsync(this, currentResetTask, forcedCompleteToken));
            }
        }

        public string BasePath { get; }
        public string PathFormat { get; }
        public LogFilePathPlaceholderResolver PathPlaceholderResolver { get; }
        public IFileAppender FileAppender { get; }
        public LogFileAccessMode AccessMode { get; }
        public Encoding Encoding { get; }
        public string? DateFormat { get; }
        public string? CounterFormat { get; }
        public long MaxSize { get; }
        public LogFileWriteStrategy WriteStrategy { get; }

        public Channel<FileLogEntry>? Queue { get; }
        public Task? WriteFileTask { get; }

        internal bool IsSyncWriteInProgress { get; set; }
        internal int PendingSyncWriteCount { get; set; }

        public int Counter { get; set; }
        public string? CurrentPath { get; set; }

        [MemberNotNullWhen(true, [nameof(_appendStream), nameof(Size)])]
        public bool IsOpen => _appendStream is not null;
        public long? Size => _appendStream?.Length;

        internal bool ShouldEnsurePreamble { get; private set; }

        internal void Open(IFileInfo fileInfo)
        {
            _appendStream = FileAppender.CreateAppendStream(fileInfo, new FileAppenderStreamCreationOptions(
                disableBuffering: AccessMode != LogFileAccessMode.KeepOpen,
                useAsyncIO: WriteStrategy == LogFileWriteStrategy.QueuedAsyncWrite));

            ShouldEnsurePreamble = true;
        }

        internal void WriteText(string text, Encoding encoding)
        {
            Debug.Assert(IsOpen);

            byte[] bytes = encoding.GetBytes(text);
            _appendStream!.Write(bytes, 0, bytes.Length);
        }

        internal void WriteBytes(byte[] bytes)
        {
            Debug.Assert(IsOpen);

            _appendStream!.Write(bytes, 0, bytes.Length);
        }

        internal void EnsurePreamble()
        {
            Debug.Assert(IsOpen);

            if (_appendStream!.Length == 0)
                WriteBytes(Encoding.GetPreamble());

            ShouldEnsurePreamble = false;
        }

        internal async ValueTask EnsurePreambleAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(IsOpen);

            if (_appendStream!.Length == 0)
                await WriteBytesAsync(Encoding.GetPreamble(), cancellationToken).ConfigureAwait(false);

            ShouldEnsurePreamble = false;
        }

        internal void Flush(bool flushToDisk)
        {
            Debug.Assert(IsOpen);

            if (!flushToDisk || _appendStream is not FileStream fileStream)
            {
                // NOTE: FileStream.Flush() is a no-op when FileStream buffering is turned off.
                _appendStream!.Flush();
            }
            else
            {
                fileStream.Flush(flushToDisk);
            }
        }

        internal async ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(IsOpen);

#if NET6_0_OR_GREATER
            // NOTE: FileStream.FlushAsync() is a no-op when FileStream buffering is turned off.
            await _appendStream!.FlushAsync(cancellationToken).ConfigureAwait(false);
#else
            cancellationToken.ThrowIfCancellationRequested();

            // NOTE: No point in using FlushAsync prior to .NET 6 as it does sync-over-async under the hood anyway,
            // it just adds significant overhead. See also: https://github.com/dotnet/corefx/issues/32837
            _appendStream!.Flush();
#endif
        }

        internal void Close()
        {
            Debug.Assert(IsOpen);

            Stream appendStream = _appendStream!;
            _appendStream = null;
            appendStream.Dispose();
        }
    }

    protected class LogFilePathFormatContext : ILogFilePathFormatContext
    {
        private readonly FileLoggerProcessor _processor;
        private readonly LogFileInfo _logFile;
        private readonly FileLogEntry _logEntry;

        public LogFilePathFormatContext(FileLoggerProcessor processor, LogFileInfo logFile, in FileLogEntry logEntry)
        {
            _processor = processor;
            _logFile = logFile;
            _logEntry = logEntry;
        }

        ref readonly FileLogEntry ILogFilePathFormatContext.LogEntry => ref _logEntry;

        string? ILogFilePathFormatContext.DateFormat => _logFile.DateFormat;
        string? ILogFilePathFormatContext.CounterFormat => _logFile.CounterFormat;
        int ILogFilePathFormatContext.Counter => _logFile.Counter;

        string ILogFilePathFormatContext.FormatDate(string? inlineFormat) => _processor.GetDate(inlineFormat, _logFile, _logEntry);
        string ILogFilePathFormatContext.FormatCounter(string? inlineFormat) => _processor.GetCounter(inlineFormat, _logFile, _logEntry);

        public string ResolvePlaceholder(Match match)
        {
            string placeholderName = match.Groups[1].Value;
            string inlineFormat = match.Groups[2].Value;

            return _logFile.PathPlaceholderResolver(placeholderName, inlineFormat.Length > 0 ? inlineFormat : null, this)
                ?? match.Groups[0].Value;
        }
    }

    private static readonly LogFilePathPlaceholderResolver s_noopPathPlaceholderResolver = delegate { return null; };

    private static readonly LogFilePathPlaceholderResolver s_defaultPathPlaceholderResolver = (placeholderName, inlineFormat, context) =>
    {
        return placeholderName switch
        {
            "date" => context.FormatDate(inlineFormat),
            "counter" => context.FormatCounter(inlineFormat),
            _ => null,
        };
    };

#if NET7_0_OR_GREATER
    [GeneratedRegex(@"<([_a-zA-Z][_a-zA-Z0-9-]*)(?::\s*([^<>]*[^\s<>]))?>", RegexOptions.CultureInvariant, 5000)]
    private static partial Regex PathPlaceholderRegex();
#else
    private static readonly Regex s_pathPlaceholderRegexCached = new(@"<([_a-zA-Z][_a-zA-Z0-9-]*)(?::\s*([^<>]*[^\s<>]))?>", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
    private static Regex PathPlaceholderRegex() => s_pathPlaceholderRegexCached;
#endif

    private static char[] InvalidPathChars => LazyInitializer.EnsureInitialized(ref field, () => Path.GetInvalidPathChars()
        .Concat(Path.GetInvalidFileNameChars())
        .Except(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
        .ToArray())!;

    private readonly Lazy<PhysicalFileAppender> _fallbackFileAppender;
    private readonly Dictionary<ILogFileSettings, LogFileInfo> _logFiles;
    private readonly TaskCompletionSource<object?> _completeTaskCompletionSource;
    private readonly CancellationTokenRegistration _completeTokenRegistration;
    private CancellationTokenSource _forcedCompleteTokenSource;
    private Task _resetTask;
    private Status _status;

    public FileLoggerProcessor(FileLoggerContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));

        _fallbackFileAppender = new Lazy<PhysicalFileAppender>(() => new PhysicalFileAppender(Environment.CurrentDirectory));

        _logFiles = new Dictionary<ILogFileSettings, LogFileInfo>();

        _resetTask = Task.CompletedTask;

        _completeTaskCompletionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _forcedCompleteTokenSource = new CancellationTokenSource();

        _completeTokenRegistration = context.CompleteToken.Register(Complete, useSynchronizationContext: false);
    }

    public void Dispose()
    {
        lock (_logFiles)
        {
            if (_status != Status.Completed)
            {
                _completeTokenRegistration.Dispose();

                _forcedCompleteTokenSource.Cancel();
                _forcedCompleteTokenSource.Dispose();

                _completeTaskCompletionSource.TrySetResult(null);

                if (_fallbackFileAppender.IsValueCreated)
                    _fallbackFileAppender.Value.Dispose();

                _resetTask.Dispose(); // might allocate a WaitHandle if some files are written in DirectSyncWrite mode

                DisposeCore();

                _status = Status.Completed;
            }
        }
    }

    protected virtual void DisposeCore() { }

    public FileLoggerContext Context { get; }

    public Task Completion => _completeTaskCompletionSource.Task;

    private async Task<bool> WaitForQueuesToCompleteAsync(Task currentResetTask, IEnumerable<Task> queueCompletionTasks)
    {
        await currentResetTask.ConfigureAwait(false);

        currentResetTask = null!; // allow GC to collect the task

#if NET6_0_OR_GREATER
        try { await Task.WhenAll(queueCompletionTasks).WaitAsync(Context.CompletionTimeout).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
#else
        using (var delayCancellationTokenSource = new CancellationTokenSource())
        {
            var completionTimeoutTask = Task.Delay(Context.CompletionTimeout, delayCancellationTokenSource.Token);
            Task completedTask = await Task.WhenAny(Task.WhenAll(queueCompletionTasks), completionTimeoutTask).ConfigureAwait(false);
            if (!ReferenceEquals(completedTask, completionTimeoutTask))
                delayCancellationTokenSource.Cancel();
            else
                return false;
        }
#endif

        return true;
    }

    private async Task ResetCoreAsync(Action? onQueuesCompleted, bool complete)
    {
        CancellationTokenSource forcedCompleteTokenSource;
        Task<bool> completionTask;
        List<LogFileInfo>? logFilesWithSyncWrite = null;

        lock (_logFiles)
        {
            if (_status != Status.Running)
                return;

            forcedCompleteTokenSource = _forcedCompleteTokenSource;
            _forcedCompleteTokenSource = new CancellationTokenSource();

            var forcedCompleteToken = forcedCompleteTokenSource.Token;
            var queueCompletionTasks = new List<Task>(capacity: _logFiles.Count);

            foreach (var logFile in _logFiles.Values)
            {
                if (logFile.WriteStrategy != LogFileWriteStrategy.DirectSyncWrite)
                {
                    queueCompletionTasks.Add(new Func<LogFileInfo, CancellationToken, Task>(static async (logFile, forcedCompleteToken) =>
                    {
                        logFile.Queue!.Writer.Complete();

                        await logFile.WriteFileTask!.ConfigureAwait(false);

                        if (logFile.IsOpen)
                        {
                            if (logFile.AccessMode == LogFileAccessMode.KeepOpen)
                            {
                                if (logFile.WriteStrategy == LogFileWriteStrategy.QueuedAsyncWrite)
                                {
                                    await logFile.FlushAsync(forcedCompleteToken).ConfigureAwait(false);
                                }
                                else
                                {
                                    forcedCompleteToken.ThrowIfCancellationRequested();
                                    logFile.Flush(flushToDisk: false);
                                }
                            }

                            logFile.Close();
                        }
                    })(logFile, forcedCompleteToken));
                }
                else
                {
                    (logFilesWithSyncWrite ??= new(capacity: _logFiles.Count)).Add(logFile);
                }
            }

            if (logFilesWithSyncWrite is not null)
            {
                queueCompletionTasks.Add(Task.Factory.StartNew(static state =>
                {
                    var (logFilesWithSyncWrite, forcedCompleteToken) = ((List<LogFileInfo>, CancellationToken))state!;

                    foreach (var logFile in logFilesWithSyncWrite)
                    {
                        lock (logFile)
                        {
                            for (; ; )
                            {
                                if (forcedCompleteToken.IsCancellationRequested)
                                    return;

                                if (logFile.PendingSyncWriteCount <= 0)
                                    break;

                                Monitor.Wait(logFile);
                            }
                        }

                        if (logFile.IsOpen)
                        {
                            if (logFile.AccessMode == LogFileAccessMode.KeepOpen)
                            {
                                forcedCompleteToken.ThrowIfCancellationRequested();
                                logFile.Flush(flushToDisk: true);
                            }

                            logFile.Close();
                        }
                    }
                }, state: (logFilesWithSyncWrite, forcedCompleteToken), forcedCompleteToken, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }

            _logFiles.Clear();

            onQueuesCompleted?.Invoke();

            _resetTask = completionTask = WaitForQueuesToCompleteAsync(_resetTask, queueCompletionTasks);

            if (complete)
                _status = Status.Completing;
        }

        try
        {
            bool hasCompletionTimedOut = !await completionTask.ConfigureAwait(false);
            if (hasCompletionTimedOut)
                Context.GetDiagnosticEventReporter()?.Invoke(new FileLoggerDiagnosticEvent.QueuesCompletionForced(this));

            forcedCompleteTokenSource.Cancel();
            forcedCompleteTokenSource.Dispose();

            if (logFilesWithSyncWrite is not null)
            {
                // wake up all waiting threads so they can cancel writes and resume execution
                foreach (var logFile in logFilesWithSyncWrite)
                {
                    lock (logFile)
                        Monitor.PulseAll(logFile);
                }
            }
        }
        finally
        {
            if (complete)
                Dispose();
        }
    }

    public Task ResetAsync(Action? onQueuesCompleted = null)
    {
        return ResetCoreAsync(onQueuesCompleted, complete: false);
    }

    public Task CompleteAsync()
    {
        return ResetCoreAsync(onQueuesCompleted: null, complete: true);
    }

    private void Complete()
    {
        Task.Run(CompleteAsync);
    }

    protected virtual LogFileInfo CreateLogFile(ILogFileSettings fileSettings, IFileLoggerSettings settings)
    {
        return new LogFileInfo(this, fileSettings, settings);
    }

    protected virtual Channel<FileLogEntry> CreateLogFileQueue(ILogFileSettings fileSettings, IFileLoggerSettings settings)
    {
        int maxQueueSize = fileSettings.MaxQueueSize ?? settings.MaxQueueSize ?? 0;

        return maxQueueSize > 0
            ? Channel.CreateBounded<FileLogEntry>(ConfigureChannelOptions(new BoundedChannelOptions(maxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropWrite
            }))
            : Channel.CreateUnbounded<FileLogEntry>(ConfigureChannelOptions(new UnboundedChannelOptions()));

        static TOptions ConfigureChannelOptions<TOptions>(TOptions options) where TOptions : ChannelOptions
        {
            options.AllowSynchronousContinuations = false;
            options.SingleReader = true;
            options.SingleWriter = false;
            return options;
        }
    }

    public void Enqueue(in FileLogEntry entry, ILogFileSettings fileSettings, IFileLoggerSettings settings)
    {
        LogFileInfo logFile;

        Task currentResetTask;
        CancellationToken forcedCompleteToken;

        lock (_logFiles)
        {
            if (_status == Status.Completed)
                throw new ObjectDisposedException(nameof(FileLoggerProcessor));

            if (_status != Status.Running)
                return;

#if NET6_0_OR_GREATER
            ref LogFileInfo? logFileRef = ref CollectionsMarshal.GetValueRefOrAddDefault(_logFiles, fileSettings, out bool logFileExists);
            logFile = logFileExists ? logFileRef! : (logFileRef = CreateLogFile(fileSettings, settings));
#else
            if (!_logFiles.TryGetValue(fileSettings, out logFile))
                _logFiles.Add(fileSettings, logFile = CreateLogFile(fileSettings, settings));
#endif

            currentResetTask = _resetTask;
            forcedCompleteToken = _forcedCompleteTokenSource.Token;
        }

        if (logFile.WriteStrategy != LogFileWriteStrategy.DirectSyncWrite)
        {
            if (!logFile.Queue!.Writer.TryWrite(entry))
                Context.GetDiagnosticEventReporter()?.Invoke(new FileLoggerDiagnosticEvent.LogEntryDropped(this, logFile, entry));
        }
        else
        {
            try
            {
                lock (logFile)
                {
                    logFile.PendingSyncWriteCount++;

                    // wait until the current thread gets its turn to write, or forced completion is signaled
                    for (; ; )
                    {
                        if (forcedCompleteToken.IsCancellationRequested)
                            return;

                        if (!logFile.IsSyncWriteInProgress)
                            break;

                        Monitor.Wait(logFile);
                    }

                    logFile.IsSyncWriteInProgress = true;
                }

                if (!currentResetTask.IsCompleted)
                {
                    // if there's a pending reset, wait for it to complete first (to avoid potential concurrent access to the same files)
                    currentResetTask.Wait(forcedCompleteToken);
                }

                var writeEntryTask = WriteEntryAsync(logFile, entry, forcedCompleteToken);
                Debug.Assert(writeEntryTask.IsCompleted);
                writeEntryTask.GetAwaiter().GetResult(); // propagate potential cancellation or exception
            }
            catch (OperationCanceledException) when (forcedCompleteToken.IsCancellationRequested)
            {
                // swallow the exception (as it shouldn't be propagated)
            }
            finally
            {
                lock (logFile)
                {
                    logFile.IsSyncWriteInProgress = false;
                    logFile.PendingSyncWriteCount--;

                    // wake up a waiting thread (if any) so it can reacquire the lock and become the next writer
                    Monitor.Pulse(logFile);
                }
            }
        }
    }

    protected virtual string GetDate(string? inlineFormat, LogFileInfo logFile, in FileLogEntry entry)
    {
        return entry.Timestamp.ToLocalTime().ToString(inlineFormat ?? logFile.DateFormat ?? "yyyyMMdd", CultureInfo.InvariantCulture);
    }

    protected virtual string GetCounter(string? inlineFormat, LogFileInfo logFile, in FileLogEntry entry)
    {
        return logFile.Counter.ToString(inlineFormat ?? logFile.CounterFormat, CultureInfo.InvariantCulture);
    }

    protected virtual bool CheckFileSize(string filePath, LogFileInfo logFile, in FileLogEntry entry)
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
        {
            currentFileSize = logFile.Size.Value;
        }

        long expectedFileSize = currentFileSize > 0 ? currentFileSize : logFile.Encoding.GetPreamble().Length;
        expectedFileSize += logFile.Encoding.GetByteCount(entry.Text);

        return expectedFileSize <= logFile.MaxSize;
    }

    protected virtual string FormatFilePath(LogFileInfo logFile, in FileLogEntry entry)
    {
        return ReferenceEquals(logFile.PathPlaceholderResolver, s_noopPathPlaceholderResolver)
            ? logFile.PathFormat
            : PathPlaceholderRegex().Replace(logFile.PathFormat, new LogFilePathFormatContext(this, logFile, entry).ResolvePlaceholder);
    }

    protected virtual bool UpdateFilePath(LogFileInfo logFile, in FileLogEntry entry, CancellationToken cancellationToken)
    {
        string filePath = FormatFilePath(logFile, entry);

        if (logFile.MaxSize > 0)
        {
            // has something changed in the file path apart from the counter (e.g. date)?
            if (filePath != logFile.CurrentPath)
            {
                // if so, we need to reset the counter
                logFile.Counter = 0;
                filePath = FormatFilePath(logFile, entry);
            }

            while (!CheckFileSize(filePath, logFile, entry))
            {
                cancellationToken.ThrowIfCancellationRequested();

                logFile.Counter++;
                string newFilePath = FormatFilePath(logFile, entry);

                // guard against falling into an infinite loop
                if (filePath == newFilePath)
                {
                    logFile.Counter--;
                    break;
                }

                filePath = newFilePath;
            }
        }

        if (logFile.CurrentPath == filePath)
            return false;

        logFile.CurrentPath = filePath;
        return true;
    }

    protected virtual void HandleFilePathChange(LogFileInfo logFile, in FileLogEntry entry)
    {
        if (logFile.IsOpen)
            logFile.Close();
    }

    protected virtual async ValueTask WriteEntryAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken forcedCompleteToken)
    {
        const int checkFileState = 0;
        const int tryOpenFileState = 1;
        const int retryOpenFileState = 2;
        const int writeState = 3;
        const int idleState = 4;

        IFileInfo? fileInfo = null;

        switch (checkFileState)
        {
            case checkFileState:
                try
                {
                    if (UpdateFilePath(logFile, entry, forcedCompleteToken))
                    {
                        if (logFile.IsOpen && logFile.AccessMode == LogFileAccessMode.KeepOpen)
                        {
                            if (logFile.WriteStrategy == LogFileWriteStrategy.QueuedAsyncWrite)
                            {
                                await logFile.FlushAsync(forcedCompleteToken).ConfigureAwait(false);
                            }
                            else
                            {
                                forcedCompleteToken.ThrowIfCancellationRequested();
                                logFile.Flush(flushToDisk: logFile.WriteStrategy == LogFileWriteStrategy.DirectSyncWrite);
                            }
                        }

                        HandleFilePathChange(logFile, entry);
                    }

                    Debug.Assert(logFile.CurrentPath is not null);

                    if (!logFile.IsOpen)
                    {
                        // GetFileInfo behavior regarding invalid filenames is inconsistent across .NET runtimes (and operating systems?)
                        // e.g. PhysicalFileProvider returns NotFoundFileInfo in .NET 5 but throws an exception in previous versions on Windows
                        fileInfo = logFile.FileAppender.FileProvider.GetFileInfo(Path.Combine(logFile.BasePath, logFile.CurrentPath));
                        goto case tryOpenFileState;
                    }
                    else
                    {
                        goto case writeState;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !forcedCompleteToken.IsCancellationRequested)
                {
                    ReportFailure(logFile, entry, ex);

                    // discarding entry when file path is invalid
                    if (logFile.CurrentPath!.IndexOfAny(InvalidPathChars) >= 0)
                        return;

                    goto case idleState;
                }

            case tryOpenFileState:
                try
                {
                    logFile.Open(fileInfo);

                    goto case writeState;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !forcedCompleteToken.IsCancellationRequested)
                {
                    goto case retryOpenFileState;
                }

            case retryOpenFileState:
                try
                {
                    if (logFile.WriteStrategy == LogFileWriteStrategy.QueuedAsyncWrite)
                    {
                        await logFile.FileAppender.EnsureDirAsync(fileInfo, forcedCompleteToken).ConfigureAwait(false);
                    }
                    else
                    {
                        forcedCompleteToken.ThrowIfCancellationRequested();
                        logFile.FileAppender.EnsureDir(fileInfo);
                    }

                    logFile.Open(fileInfo);

                    goto case writeState;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !forcedCompleteToken.IsCancellationRequested)
                {
                    ReportFailure(logFile, entry, ex);

                    // discarding entry when file path is invalid
                    if (logFile.CurrentPath!.IndexOfAny(InvalidPathChars) >= 0)
                        return;

                    goto case idleState;
                }

            case writeState:
                try
                {
                    try
                    {
                        if (logFile.WriteStrategy == LogFileWriteStrategy.QueuedAsyncWrite)
                        {
                            if (logFile.ShouldEnsurePreamble)
                                await logFile.EnsurePreambleAsync(forcedCompleteToken).ConfigureAwait(false);

                            await logFile.WriteTextAsync(entry.Text, logFile.Encoding, forcedCompleteToken).ConfigureAwait(false);

                            if (logFile.AccessMode != LogFileAccessMode.KeepOpen)
                                await logFile.FlushAsync(forcedCompleteToken).ConfigureAwait(false);
                        }
                        else
                        {
                            forcedCompleteToken.ThrowIfCancellationRequested();

                            if (logFile.ShouldEnsurePreamble)
                                logFile.EnsurePreamble();

                            logFile.WriteText(entry.Text, logFile.Encoding);

                            if (logFile.AccessMode != LogFileAccessMode.KeepOpen)
                                logFile.Flush(flushToDisk: logFile.WriteStrategy == LogFileWriteStrategy.DirectSyncWrite);
                        }
                    }
                    finally
                    {
                        if (logFile.AccessMode == LogFileAccessMode.OpenTemporarily)
                            logFile.Close();
                    }

                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !forcedCompleteToken.IsCancellationRequested)
                {
                    ReportFailure(logFile, entry, ex);

                    goto case idleState;
                }

            case idleState:
                // discarding failed entry on forced complete
                if (logFile.WriteStrategy != LogFileWriteStrategy.DirectSyncWrite)
                {
                    if (Context.WriteRetryDelay > TimeSpan.Zero)
                        await Task.Delay(Context.WriteRetryDelay, forcedCompleteToken).ConfigureAwait(false);
                    else
                        forcedCompleteToken.ThrowIfCancellationRequested();
                }
                else
                {
                    if (Context.WriteRetryDelay > TimeSpan.Zero)
                    {
                        forcedCompleteToken.WaitHandle.WaitOne(Context.WriteRetryDelay);
                    }

                    forcedCompleteToken.ThrowIfCancellationRequested();
                }

                goto case checkFileState;
        }

        void ReportFailure(LogFileInfo logFile, in FileLogEntry entry, Exception exception)
        {
            Context.GetDiagnosticEventReporter()?.Invoke(new FileLoggerDiagnosticEvent.LogEntryWriteFailed(this, logFile, entry, exception));
        }
    }

    private async Task WriteFileAsync(LogFileInfo logFile, Task currentResetTask, CancellationToken forcedCompleteToken)
    {
        // if there's a pending reset, wait for it to complete first (to avoid potential concurrent access to the same files)

#if NET6_0_OR_GREATER
        currentResetTask = currentResetTask.WaitAsync(forcedCompleteToken);
        try { await currentResetTask.ConfigureAwait(false); }
        catch (OperationCanceledException) when (forcedCompleteToken.IsCancellationRequested) { throw; }
        catch { /* swallow the exception (as it would be redundant to propagate it) */ }
#else
        var cancellationTcs = new TaskCompletionSource<object>();
        using (forcedCompleteToken.Register(() => cancellationTcs.TrySetCanceled(forcedCompleteToken), useSynchronizationContext: false))
        {
            var completedTask = await Task.WhenAny(currentResetTask, cancellationTcs.Task).ConfigureAwait(false);
            if (!ReferenceEquals(completedTask, currentResetTask))
            {
                completedTask.GetAwaiter().GetResult(); // propagate cancellation
            }
        }
#endif

        currentResetTask = null!; // allow GC to collect the task

        // processing the queue

        ChannelReader<FileLogEntry> queue = logFile.Queue!.Reader;
        while (await queue.WaitToReadAsync(forcedCompleteToken).ConfigureAwait(false))
        {
            while (queue.TryRead(out FileLogEntry entry))
                await WriteEntryAsync(logFile, entry, forcedCompleteToken).ConfigureAwait(false);
        }
    }
}
