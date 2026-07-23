using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MetadataSource.Audible
{
    // IProvideSeriesInfo.GetSeriesInfo(int) is an int-only contract inherited
    // from Sonarr's TVDB-shaped interface, but Audible identifies series by an
    // alphanumeric ASIN (e.g. "B08G9PRS1K"), not an integer. This table is the
    // reversible mapping between a stable, locally-assigned integer id (used
    // everywhere the interface expects a "TvdbId") and the real ASIN (used for
    // every actual call to Audible/Audnexus). Persisted rather than an
    // in-memory cache so it survives restarts between a search and a later add
    // or refresh.
    public class AudibleSeriesMap : ModelBase
    {
        public string Asin { get; set; }
    }
}
