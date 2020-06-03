using System;
using Microsoft.Extensions.Logging;

namespace Itg.Persistence.Secondary
{
    public static class LoggerExtensions
    {
        public static void RecordNotFound(this ILogger logger, string key, int count) => logger.LogError(new EventId(0, nameof(RecordNotFound)), "{KEY} {COUNT}", key, count);
        public static void DeletingRecord(this ILogger logger, string key, DateTimeOffset stamp) => logger.LogInformation(new EventId(1, nameof(DeletingRecord)), "{KEY} {STAMP}", key, stamp);
        public static void SettingNewRecord(this ILogger logger, string key, DateTimeOffset stamp) => logger.LogInformation(new EventId(2, nameof(SettingNewRecord)), "{KEY} {STAMP}", key, stamp);
        public static void OverwritingRecord(this ILogger logger, string key, DateTimeOffset stamp) => logger.LogInformation(new EventId(3, nameof(OverwritingRecord)), "{KEY} {STAMP}", key, stamp);
        public static void ClearingAllRecords(this ILogger logger, int count) => logger.LogInformation(new EventId(4, nameof(ClearingAllRecords)), "{COUNT}", count);
        public static void ClearingOldRecords(this ILogger logger, int count, TimeSpan interval) => logger.LogInformation(new EventId(5, nameof(ClearingOldRecords)), "{COUNT} {INTERVAL}", count, interval);
        public static void LoadFailed(this ILogger logger, string path, int attempt, Exception ex) => logger.LogCritical(new EventId(6, nameof(LoadFailed)), ex, "{PATH} {ATTEMPT}", path, attempt);
        public static void LoadFailed(this ILogger logger, string path, Exception ex) => logger.LoadFailed(path, 0, ex);
        public static void ResourceDidNotChange(this ILogger logger, string path) => logger.LogInformation(new EventId(7, nameof(ResourceDidNotChange)), "{PATH}", path);
        public static void MediumIdentified(this ILogger logger, string path, bool notExists) => logger.LogInformation(new EventId(8, nameof(MediumIdentified)), "{PATH} {NOT_EXISTS}", path, notExists);
        public static void SaveFailed(this ILogger logger, string path, int attempt, Exception ex) => logger.LogCritical(new EventId(9, nameof(SaveFailed)), ex, "{PATH} {ATTEMPT}", path, attempt);
        public static void SaveFailed(this ILogger logger, string path, Exception ex) => logger.SaveFailed(path, 0, ex);
    }
}
