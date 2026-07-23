namespace NzbDrone.Core.MetadataSource.Audible.Resource
{
    // The "series" a book belongs to, as embedded in a product/search result --
    // confirmed live: {"asin": "...", "sequence": "1", "title": "..."}.
    public class AudibleSeriesRefResource
    {
        public string Asin { get; set; }
        public string Sequence { get; set; }
        public string Title { get; set; }
    }
}
