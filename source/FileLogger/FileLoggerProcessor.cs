using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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
            public string BasePath => Settings.BasePath;
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
        static StringBuilder stringBuilder;

        readonly Dictionary<string, LogFileInfo> _logFiles;

        readonly CancellationTokenRegistration _completeTokenRegistration;
        readonly CancellationTokenSource _disposeTokenSource;

        bool _isDisposed;

        public FileLoggerProcessor(IFileLoggerContext context, IFileLoggerSettingsBase settings)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            Context = context;
            Settings = settings.ToImmutable();

            _logFiles = new Dictionary<string, LogFileInfo>();

            _disposeTokenSource = new CancellationTokenSource();
            _completeTokenRegistration = context.CompleteToken.Register(Complete, useSynchronizationContext: false);
        }

        protected virtual void DisposeCore()
        {
            _completeTokenRegistration.Dispose();

            _disposeTokenSource.Cancel();
            _disposeTokenSource.Dispose();
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

        void Complete()
        {
            Context.OnComplete(this, CompleteCoreAsync(null));
        }

        public Task CompleteAsync(IFileLoggerSettingsBase newSettings)
        {
            var result = CompleteCoreAsync(newSettings);
            Context.OnComplete(this, result);
            return result;
        }

        Task CompleteCoreAsync(IFileLoggerSettingsBase newSettings)
        {
            Task[] completionTasks;
            lock (_logFiles)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(FileLoggerProcessor));

                completionTasks = _logFiles.Values.Select(lf =>
                {
                    lf.Queue.Complete();
                    return lf.Queue.Completion;
                }).ToArray();

                _logFiles.Clear();

                if (newSettings != null)
                    Settings = newSettings.ToImmutable();
            }

            return Task.WhenAll(completionTasks);
        }

        protected virtual LogFileInfo CreateLogFile()
        {
            return new LogFileInfo();
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
                {
                    logFile = CreateLogFile();
                    logFile.FileName = Path.ChangeExtension(fileName, null);
                    logFile.Extension = Path.GetExtension(fileName);

                    logFile.Settings = Settings;

                    logFile.Queue = new ActionBlock<FileLogEntry>(
                        e => WriteEntryAsync(logFile, e, _disposeTokenSource.Token),
                        new ExecutionDataflowBlockOptions
                        {
                            MaxDegreeOfParallelism = 1,
                            BoundedCapacity = Settings.MaxQueueSize,
                        });

                    _logFiles.Add(fileName, logFile);
                }
            }

            logFile.Queue.Post(entry);
        }

        protected virtual Encoding GetFileEncoding(LogFileInfo logFile, FileLogEntry entry)
        {
            return logFile.Settings.FileEncoding;
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

        protected virtual bool CheckLogFile(LogFileInfo logFile, string postfix, Encoding fileEncoding, FileLogEntry entry)
        {
            if (logFile.Settings.MaxFileSize > 0)
            {
                var fileInfo = Context.FileProvider.GetFileInfo(logFile.GetFilePath(postfix));
                if (fileInfo.Exists &&
                    (fileInfo.IsDirectory || fileInfo.Length + fileEncoding.GetByteCount(entry.Text) > logFile.Settings.MaxFileSize))
                {
                    logFile.Counter++;
                    return false;
                }
            }

            return true;
        }

        string GetPostfix(LogFileInfo logFile, Encoding fileEncoding, FileLogEntry entry)
        {
            if (HasPostfix(logFile, entry))
            {
                var sb = stringBuilder;
                stringBuilder = null;
                if (sb == null)
                    sb = new StringBuilder();

                while (true)
                {
                    BuildPostfix(sb, logFile, entry);
                    var postfix = sb.ToString();
                    sb.Clear();

                    if (CheckLogFile(logFile, postfix, fileEncoding, entry))
                    {
                        if (sb.Capacity > 64)
                            sb.Capacity = 64;

                        stringBuilder = sb;

                        return postfix;
                    }
                }
            }
            else
                return null;
        }

        async Task WriteEntryAsync(LogFileInfo logFile, FileLogEntry entry, CancellationToken disposeToken)
        {
            // discarding remaining entries if queue got disposed
            disposeToken.ThrowIfCancellationRequested();

            Encoding fileEncoding;
            string filePath;
            bool ensureBasePath;

            fileEncoding = GetFileEncoding(logFile, entry);
            var postfix = GetPostfix(logFile, fileEncoding, entry);
            filePath = logFile.GetFilePath(postfix);
            ensureBasePath = logFile.Settings.EnsureBasePath;

            var fileInfo = Context.FileProvider.GetFileInfo(filePath);

            while (true)
            {
                try
                {
                    await Context.AppendAllTextAsync(fileInfo, entry.Text, fileEncoding).ConfigureAwait(false);
                    return;
                }
                catch
                {
                    if (ensureBasePath)
                        try
                        {
                            if (await Context.EnsureDirAsync(fileInfo).ConfigureAwait(false))
                            {
                                await Context.AppendAllTextAsync(fileInfo, entry.Text, fileEncoding).ConfigureAwait(false);
                                return;
                            }
                        }
                        catch { }
                }

                // discarding failed entry if queue got disposed
                await Task.Delay(1000, disposeToken).ConfigureAwait(false);
            }
        }
    }
}
