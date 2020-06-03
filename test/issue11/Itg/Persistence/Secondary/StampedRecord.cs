using System;

namespace Itg.Persistence.Secondary
{
    [Serializable]
    public class StampedRecord<TModel>
    {
        public StampedRecord(TModel record) =>
            (Record, Stamp) = (record, DateTimeOffset.UtcNow);

        public TModel Record { get; }
        public DateTimeOffset Stamp { get; }
    }
}
