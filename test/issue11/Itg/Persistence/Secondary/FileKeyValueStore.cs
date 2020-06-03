using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Itg.Persistence.Secondary
{
    public class FileKeyValueStore<TModel> : IKeyValueStore<TModel>
    {
        private const string FILE_EXTENSION = ".store";
        private const int MAX_ATTEMPTS = 3;

        private readonly ILogger<FileKeyValueStore<TModel>> _logger;
        private readonly IFileProvider _fileProvider;
        private readonly IFileInfo _fileInfo;

        private Dictionary<string, StampedRecord<TModel>> buffer;
        private IChangeToken currentToken;
        private int currentSaveAttempt;
        private int currentLoadAttempt;

        public FileKeyValueStore(
          FileKeyValueStoreOptions dataOptions,
          IWebHostEnvironment environment,
          ILogger<FileKeyValueStore<TModel>> logger)
        {
            this._logger = logger;
            this._fileProvider = environment.ContentRootFileProvider;
            this._fileInfo = this.GetFileInfo(dataOptions);
            System.Diagnostics.Debug.WriteLine("############################################");
            System.Diagnostics.Debug.WriteLine("############# CREATING FILE STORE #############");
            System.Diagnostics.Debug.WriteLine("############################################");
        }

        public async Task<TModel> Get(string key, CancellationToken ct = default)
        {
            await this.LoadData(ct);

            if (this.buffer.Count == 0 || !this.buffer.ContainsKey(key))
            {
                return default;
            }

            return this.buffer[key].Record;
        }

        public async Task Delete(string key, CancellationToken ct = default)
        {
            await this.LoadData(ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (this.buffer.Count == 0 || !this.buffer.ContainsKey(key))
            {
                this._logger.RecordNotFound(key, this.buffer.Count);
                return;
            }

            this._logger.DeletingRecord(key, this.buffer[key].Stamp);
            this.buffer.Remove(key);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            await this.SaveData(ct);
        }

        public async Task Set(string key, TModel data, CancellationToken ct = default)
        {
            await this.LoadData(ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            var newRecord = new StampedRecord<TModel>(data);
            if (this.buffer is null)
            {
                this._logger.SettingNewRecord(key, newRecord.Stamp);
                this.buffer = new Dictionary<string, StampedRecord<TModel>>
                {
                    { key, newRecord }
                };
            }
            else if (this.buffer.ContainsKey(key))
            {
                this._logger.OverwritingRecord(key, newRecord.Stamp);
                this.buffer[key] = newRecord;
            }
            else
            {
                this._logger.SettingNewRecord(key, newRecord.Stamp);
                /********************************************************
                *     ^ Actual line I'd see on console and on reload ^    *
                ********************************************************/
                this.buffer.Add(key, newRecord);
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            await this.SaveData(ct);
        }

        public async Task<int> Clear(TimeSpan? olderThan = null, CancellationToken ct = default)
        {
            await this.LoadData(ct);
            ct.ThrowIfCancellationRequested();

            if (this.buffer is null)
            {
                this.buffer = new Dictionary<string, StampedRecord<TModel>>();
                return 0;
            }

            if (olderThan is null)
            {
                var oldRecordsCount = this.buffer.Count;
                this.buffer.Clear();
                await this.SaveData(ct);
                this._logger.ClearingAllRecords(oldRecordsCount);
                return 0;
            }

            ct.ThrowIfCancellationRequested();
            var recordsToClear = this.buffer.Where(r => r.Value.Stamp > DateTime.Now - olderThan.Value).ToArray();
            foreach (var record in recordsToClear)
            {
                this.buffer.Remove(record.Key);
            }

            ct.ThrowIfCancellationRequested();
            await this.SaveData(ct);
            this._logger.ClearingOldRecords(recordsToClear.Length, olderThan.Value);

            return this.buffer.Count;
        }

        protected virtual async Task LoadData(CancellationToken ct = default)
        {
            this.currentLoadAttempt = 1;
            if (!this._fileInfo.Exists || ct.IsCancellationRequested)
            {
                if (this.buffer is null)
                {
                    this.buffer = new Dictionary<string, StampedRecord<TModel>>();
                }
                return;
            }

            if (this.currentToken is null || this.currentToken.HasChanged)
            {
                this.currentToken = this._fileProvider.Watch(this._fileInfo.PhysicalPath);
                try
                {
                    await Task.Run(() => this.TryLoadData(ct), ct);
                }
                catch (Exception ex)
                {
                    this._logger.LoadFailed(this._fileInfo.PhysicalPath, ex);
                    throw;
                }
            }
            else
            {
                this._logger.ResourceDidNotChange(this._fileInfo.PhysicalPath);
            }
        }

        protected virtual async Task SaveData(CancellationToken ct = default)
        {
            this.currentSaveAttempt = 1;
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Run(() => this.TrySaveData(ct), ct);
            }
            catch (Exception ex)
            {
                this._logger.SaveFailed(this._fileInfo.PhysicalPath, ex);
                throw;
            }

            this.currentToken = this._fileProvider.Watch(this._fileInfo.PhysicalPath);
        }

        private async Task TrySaveData(CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var format = new BinaryFormatter();
                var resourceExists = this._fileInfo.Exists;
                using var fileStream = new FileStream(this._fileInfo.PhysicalPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                this._logger.MediumIdentified(this._fileInfo.PhysicalPath, !resourceExists);
                /********************************************************
                *     ^ Actual line I'd see on console and on reload ^    *
                ********************************************************/
                using var zipStream = new GZipStream(fileStream, CompressionMode.Compress);
                format.Serialize(zipStream, this.buffer);

                // TODO: zipStream does not support reading!
                //if (this._logger.IsEnabled(LogLevel.Debug))
                //{
                //    using var ms = new MemoryStream();
                //    zipStream.CopyTo(ms);
                //    this._logger.ResourceWritten(ms.Length, this._fileInfo.PhysicalPath);
                //}
            }
            catch (IOException ex)
            {
                this._logger.SaveFailed(this._fileInfo.PhysicalPath, this.currentSaveAttempt, ex);
                if (this.currentSaveAttempt <= MAX_ATTEMPTS)
                {
                    ++this.currentSaveAttempt;
                    await Task.Delay(500, ct);
                    await this.TrySaveData(ct);
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task TryLoadData(CancellationToken ct)
        {
            try
            {
                var format = new BinaryFormatter();
                using var fileStream = new FileStream(this._fileInfo.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.None);
                using var zipStream = new GZipStream(fileStream, CompressionMode.Decompress);

                // TODO: Messes up deserialization, and the zipStream cannot be seeked back to position 0.
                //if (this._logger.IsEnabled(LogLevel.Debug))
                //{
                //    using var ms = new MemoryStream();
                //    zipStream.CopyTo(ms);
                //    this._logger.ResourceRead(ms.Length, this._fileInfo.PhysicalPath);
                //}

                this.buffer = (Dictionary<string, StampedRecord<TModel>>)format.Deserialize(zipStream);
            }
            catch (IOException ex)
            {
                this._logger.LoadFailed(this._fileInfo.PhysicalPath, this.currentLoadAttempt, ex);
                if (this.currentLoadAttempt <= MAX_ATTEMPTS)
                {
                    ++this.currentLoadAttempt;
                    await Task.Delay(500, ct);
                    await this.TryLoadData(ct);
                }
                else
                {
                    throw;
                }
            }
            catch (InvalidCastException ex)
            {
                this._logger.LoadFailed(this._fileInfo.PhysicalPath, ex);
                this.buffer = null;
                /* swallowing */
            }
        }

        private IFileInfo GetFileInfo(FileKeyValueStoreOptions dataOptions)
        {
            var filePath = Path.Combine(
              dataOptions.FileBasePath ?? string.Empty,
              dataOptions.FileNamePrefix + typeof(TModel).Name + FILE_EXTENSION);

            return this._fileProvider.GetFileInfo(filePath);
        }

        #region Added for testing purposes

        public IFileInfo FileInfo => _fileInfo;

        #endregion
    }
}
