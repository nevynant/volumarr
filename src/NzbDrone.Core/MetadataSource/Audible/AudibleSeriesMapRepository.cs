using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.Audible
{
    public interface IAudibleSeriesMapRepository : IBasicRepository<AudibleSeriesMap>
    {
        AudibleSeriesMap FindByAsin(string asin);
    }

    public class AudibleSeriesMapRepository : BasicRepository<AudibleSeriesMap>, IAudibleSeriesMapRepository
    {
        public AudibleSeriesMapRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public AudibleSeriesMap FindByAsin(string asin)
        {
            return Query(x => x.Asin == asin).SingleOrDefault();
        }
    }
}
