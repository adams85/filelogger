using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.FileProviders;

namespace Karambolo.Extensions.Logging.File
{
    public interface IFileLoggerProcessor : IDisposable
    {
        void Enqueue(string fileName, FileLogEntry entry);
    }

    public class FileLoggerProcessor : IFileLoggerProcessor
    {
        protected class LogFileInfo
        {
            public string BasePath { get; set; }
            public string FileName { get; set; }
            public string Extension { get; set; }

            public string GetFilePath(string postfix)
            {
                return Path.Combine(BasePath, string.Concat(FileName, postfix, Extension));
            }

            public IFileLoggerSettingsBase Settings { get; set; }

            public ActionBlock<FileLogEntry> Queue { get; set; }

            public int Counter { get; set; }
        }

        [ThreadStatic]
        private static StringBuilder s_stringBuilder;
        private readonly Lazy<PhysicalFileAppender> _fallbackFileAppender;
        private readonly Dictionary<string, LogFileInfo> _logFiles;
        private readonly CancellationTokenRegistration _completeTokenRegistration;
        private CancellationTokenSource _shutdownTokenSource;
        private bool _isDisposed;

        public FileLoggerProcessor(IFileLoggerContext context, IFileLoggerSettingsBase settings)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

#pragma warning disable CS0618 // Type or member is obsolete
            PhysicalFileProvider physicalFileProvider = context.FileProvider == null ? null : context.FileProvider as PhysicalFileProvider ??
#pragma warning restore CS0618 // Type or member is obsolete
                throw new ArgumentException($"File provider must be {nameof(PhysicalFileProvider)}", nameof(context));

            _fallbackFileAppender =
                physicalFileProvider != null ?
                new Lazy<PhysicalFileAppender>(() => new PhysicalFileAppender(physicalFileProvider)) :
                new Lazy<PhysicalFileAppender>(() => new PhysicalFileAppender(Environment.CurrentDirectory));

            Context = context;
            Settings = settings.Freeze();

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

        protected IFileLoggerSettingsBase Settings { get; private set; }

        private void Complete()
        {
            CompleteAsync(null);
        }

        public Task CompleteAsync(IFileLoggerSettingsBase newSettings)
        {
            Task result = CompleteCoreAsync(newSettings);
            Context.OnComplete(this, result);
            return result;
        }

        private async Task CompleteCoreAsync(IFileLoggerSettingsBase newSettings)
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

                if (newSettings != null)
                    Settings = newSettings.Freeze();
            }

            await Task.WhenAny(Task.WhenAll(completionTasks), Task.Delay(Context.CompletionTimeout)).ConfigureAwait(false);

            shutdownTokenSource.Cancel();
            shutdownTokenSource.Dispose();
        }

        protected virtual LogFileInfo CreateLogFile()
        {
            return new LogFileInfo();
        }

        protected virtual LogFileInfo CreateLogFile(string fileName)
        {
            LogFileInfo logFile = CreateLogFile();

            logFile.BasePath = Settings.BasePath ?? string.Empty;
            logFile.FileName = Path.ChangeExtension(fileName, null);
            logFile.Extension = Path.GetExtension(fileName);

            logFile.Settings = Settings;

            // important: closure must pick up the current token!
            CancellationToken shutdownToken = _shutdownTokenSource.Token;
            logFile.Queue = new ActionBlock<FileLogEntry>(
                e => WriteEntryAsync(logFile, e, shutdownToken),
                new ExecutionDataflowBlockOptions
                {
                    MaxDegreeOfParallelism = 1,
                    BoundedCapacity = Settings.MaxQueueSize,
                });

            return logFile;
        }

        public void Enqueue(string fileName, FileLogEntry entry)
        {
            LogFileInfo logFile;

            lock (_logFiles)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FileLoggerProcessor));

                if (Context.CompleteToken.IsCancellationRequested)
                    return;

                if (!_logFiles.TryGetValue(fileName, out logFile))
                    _logFiles.Add(fileName, logFile = CreateLogFile(fileName));
            }

            logFile.Queue.Post(entry);
        }

        protected virtual IFileAppender GetFileAppender(LogFileInfo logFile)
        {
            return logFile.Settings.FileAppender ?? _fallbackFileAppender.Value;
        }

        protected virtual Encoding GetFileEncoding(LogFileInfo logFile, FileLogEntry entry)
        {
            return logFile.Settings.FileEncoding ?? Encoding.UTF8;
        }

        protected virtual bool HasPostfix(LogFileInfo logFile, FileLogEntry entry)
        {
            return !string.IsNullOrEmpty(logFile.Settings.DateFormat) || logFile.Settings.MaxFileSize > 0;
        }

        protected virtual void BuildPostfix(StringBuilder sb, LogFileInfo logFile, FileLogEntry entry)
        {
            if (!string.IsNullOrEmpty(logFile.Settings.DateFormat))
            {
                sb.Append('-');
                sb.Append(entry.Timestamp.ToLocalTime().ToString(logFile.Settings.DateFormat, CultureInfo.InvariantCulture));
            }

            if (logFile.Settings.MaxFileSize > 0)
            {
                sb.Append('-');
                sb.Append(logFile.Counter.ToString(logFile.Settings.CounterFormat, CultureInfo.InvariantCulture));
            }
        }

        protected virtual bool CheckLogFile(LogFileInfo logFile, string postfix, IFileAppender fileAppender, Encoding fileEncoding, FileLogEntry entry)
        {
            if (logFile.Settings.MaxFileSize > 0)
            {
                IFileInfo fileInfo = fileAppender.FileProvider.GetFileInfo(logFile.GetFilePath(postfix));
                if (fileInfo.Exists &&
                    (fileInfo.IsDirectory || GetExpectedFileSize(fileInfo, entry, fileEncoding) > logFile.Settings.MaxFileSize))
                {
                    logFile.Counter++;
                    return false;
                }
            }

            return true;

            long GetExpectedFileSize(IFileInfo fi, FileLogEntry le, Encoding enc)
            {
                var fileSize = fi.Length;
                if (fileSize == 0)
                    fileSize += enc.GetPreamble().Length;
                return fileSize += enc.GetByteCount(le.Text);
            }
        }

        private string GetPostfix(LogFileInfo logFile, IFileAppender fileAppender, Encoding fileEncoding, FileLogEntry entry)
        {
            if (HasPostfix(logFile, entry))
            {
                StringBuilder sb = s_stringBuilder;
                s_stringBuilder = null;
                if (sb == null)
                    sb = new StringBuilder();

                while (true)
                {
                    BuildPostfix(sb, logFile, entry);
                    var postfix = sb.ToString();
                    sb.Clear();

                    if (CheckLogFile(logFile, postfix, fileAppender, fileEncoding, entry))
                    {
                        if (sb.Capacity > 64)
                            sb.Capacity = 64;

                        s_stringBuilder = sb;

                        return postfix;
                    }
                }
            }
            else
                return null;
        }

        private async Task WriteEntryAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken shutdownToken)
        {
            // discarding remaining entries on shutdown
            shutdownToken.ThrowIfCancellationRequested();

            IFileAppender fileAppender = GetFileAppender(logFile);
            Encoding fileEncoding = GetFileEncoding(logFile, entry);
            var postfix = GetPostfix(logFile, fileAppender, fileEncoding, entry);
            var filePath = logFile.GetFilePath(postfix);
            var ensureBasePath = logFile.Settings.EnsureBasePath;

            IFileInfo fileInfo = fileAppender.FileProvider.GetFileInfo(filePath);

            while (true)
            {
                try
                {
                    await fileAppender.AppendAllTextAsync(fileInfo, entry.Text, fileEncoding, shutdownToken).ConfigureAwait(false);
                    return;
                }
                catch
                {
                    if (ensureBasePath)
                        try
                        {
                            if (await fileAppender.EnsureDirAsync(fileInfo, shutdownToken).ConfigureAwait(false))
                            {
                                await fileAppender.AppendAllTextAsync(fileInfo, entry.Text, fileEncoding, shutdownToken).ConfigureAwait(false);
                                return;
                            }
                        }
                        catch { }
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
