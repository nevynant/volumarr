using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Newznab
{
    public class NewznabCapabilities
    {
        public int DefaultPageSize { get; set; }
        public int MaxPageSize { get; set; }
        public string[] SupportedSearchParameters { get; set; }
        public string[] SupportedTvSearchParameters { get; set; }

        // Audiobook indexers (e.g. AudioBookBay via Jackett) advertise
        // book-search, not tv-search -- real Sonarr never parsed this
        // since TV indexers never use it, but this fork needs it as a
        // fallback search tier for indexers with no tv-search support.
        public string[] SupportedBookSearchParameters { get; set; }
        public bool SupportsAggregateIdSearch { get; set; }
        public string TextSearchEngine { get; set; }
        public string TvTextSearchEngine { get; set; }
        public List<NewznabCategory> Categories { get; set; }

        public NewznabCapabilities()
        {
            DefaultPageSize = 100;
            MaxPageSize = 100;
            SupportedSearchParameters = new[] { "q" };
            SupportedTvSearchParameters = new[] { "q", "rid", "season", "ep" }; // This should remain 'rid' for older newznab installs.
            SupportedBookSearchParameters = null;
            SupportsAggregateIdSearch = false;
            TextSearchEngine = "sphinx";    // This should remain 'sphinx' for odler newznab installs
            TvTextSearchEngine = "sphinx";  // This should remain 'sphinx' for odler newznab installs
            Categories = new List<NewznabCategory>();
        }
    }

    public class NewznabCategory
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public List<NewznabCategory> Subcategories { get; set; }
    }
}
