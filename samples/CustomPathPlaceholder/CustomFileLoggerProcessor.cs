using System.Globalization;
using System.Reflection;
using Karambolo.Extensions.Logging.File;

namespace CustomBehavior
{
    public class CustomFileLoggerProcessor : FileLoggerProcessor
    {
        private static readonly string s_appName = Assembly.GetEntryAssembly().GetName().Name;

        public CustomFileLoggerProcessor(FileLoggerContext context) : base(context) { }

        // offsets the counter by 1 -> the counter will start at 1
        protected override string GetCounter(string inlineFormat, LogFileInfo logFile, FileLogEntry entry) =>
            (logFile.Counter + 1).ToString(inlineFormat ?? logFile.CounterFormat, CultureInfo.InvariantCulture);

        // adds support for the custom path variable '<appname>'
        protected override string FormatFilePath(LogFileInfo logFile, FileLogEntry entry) =>
            base.FormatFilePath(logFile, entry).Replace("<appname>", s_appName);
    }
}
