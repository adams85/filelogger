using System.IO;
using Karambolo.Extensions.Logging.File;
using Microsoft.Extensions.FileProviders;

namespace LogRotation;

public class RotatingFileLoggerProcessor : FileLoggerProcessor
{
    public RotatingFileLoggerProcessor(FileLoggerContext context) : base(context) { }

    protected override LogFileInfo CreateLogFile(ILogFileSettings fileSettings, IFileLoggerSettings settings) =>
        new ExtendedLogFileInfo(this, fileSettings, settings);

    protected override void HandleFilePathChange(LogFileInfo logFile, FileLogEntry entry)
    {
        base.HandleFilePathChange(logFile, entry);

        ExtendedLogFileInfo extendedLogFile;
        if (logFile.MaxSize > 0 // rotate only if a size limit is set for the file
            && logFile.Counter > 0 // rotation happens only when the first file is full
            && (extendedLogFile = (ExtendedLogFileInfo)logFile).MaxFiles > 1 // rotation can only be done using at least 2 files
            && logFile.FileAppender.FileProvider is PhysicalFileProvider) // we can't really implement rotation for file providers other than physical
        {
            logFile.Counter = extendedLogFile.MaxFiles - 1;
            string filePath = FormatFilePath(logFile, entry);
            string fileFullPath = logFile.FileAppender.FileProvider.GetFileInfo(Path.Combine(logFile.BasePath, filePath)).PhysicalPath!;
            if (File.Exists(fileFullPath))
                File.Delete(fileFullPath);

            string previousFilePath;
            string previousFileFullPath;
            do
            {
                logFile.Counter--;
                previousFilePath = FormatFilePath(logFile, entry);
                if (previousFilePath == filePath)
                    break;

                previousFileFullPath = logFile.FileAppender.FileProvider.GetFileInfo(Path.Combine(logFile.BasePath, previousFilePath)).PhysicalPath!;
                if (File.Exists(previousFileFullPath))
                    File.Move(previousFileFullPath, fileFullPath);

                filePath = previousFilePath;
                fileFullPath = previousFileFullPath;
            }
            while (logFile.Counter > 0);

            logFile.CurrentPath = filePath;
        }
    }

    private sealed class ExtendedLogFileInfo : LogFileInfo
    {
        public ExtendedLogFileInfo(FileLoggerProcessor processor, ILogFileSettings fileSettings, IFileLoggerSettings settings)
            : base(processor, fileSettings, settings)
        {
            MaxFiles = (fileSettings as RotatingLogFileOptions)?.MaxFiles ?? (settings as RotatingFileLoggerOptions)?.MaxFiles ?? 0;
        }

        public int MaxFiles { get; }
    }
}
