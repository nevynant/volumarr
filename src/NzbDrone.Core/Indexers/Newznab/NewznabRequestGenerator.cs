using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Indexers.Newznab
{
    public class NewznabRequestGenerator : IIndexerRequestGenerator
    {
        private readonly Logger _logger;
        private readonly INewznabCapabilitiesProvider _capabilitiesProvider;

        public ProviderDefinition Definition { get; set; }
        public int MaxPages { get; set; }
        public int PageSize { get; set; }
        public NewznabSettings Settings { get; set; }

        public NewznabRequestGenerator(INewznabCapabilitiesProvider capabilitiesProvider)
        {
            _logger = NzbDroneLogger.GetLogger(GetType());
            _capabilitiesProvider = capabilitiesProvider;

            MaxPages = 30;
            PageSize = 100;
        }

        // Used for anime
        private bool SupportsSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedSearchParameters != null &&
                       capabilities.SupportedSearchParameters.Contains("q");
            }
        }

        // Used for standard/daily
        private bool SupportsTvQuerySearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("q");
            }
        }

        // Used for standard/daily
        private bool SupportsTvTitleSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("title");
            }
        }

        // Combines 'SupportsTvQuerySearch' and 'SupportsTvTitleSearch'
        private bool SupportsTvTextSearches
        {
            get
            {
                return SupportsTvQuerySearch || SupportsTvTitleSearch;
            }
        }

        // Audiobook-only indexers (e.g. AudioBookBay) advertise book-search
        // instead of tv-search -- used as a last-resort fallback in
        // AddTitlePageableRequests below, since this fork otherwise has no
        // way to query indexers with no tv-search capability at all.
        private bool SupportsBookQuerySearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedBookSearchParameters != null &&
                       capabilities.SupportedBookSearchParameters.Contains("q");
            }
        }

        private bool SupportsTvdbSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("tvdbid");
            }
        }

        private bool SupportsImdbSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("imdbid");
            }
        }

        private bool SupportsTvRageSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("rid");
            }
        }

        private bool SupportsTvMazeSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("tvmazeid");
            }
        }

        private bool SupportsTmdbSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("tmdbid");
            }
        }

        // Combines all ID based searches
        private bool SupportsTvIdSearches
        {
            get
            {
                return SupportsTvdbSearch || SupportsImdbSearch || SupportsTvRageSearch || SupportsTvMazeSearch || SupportsTmdbSearch;
            }
        }

        private bool SupportsSeasonSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("season");
            }
        }

        private bool SupportsEpisodeSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportedTvSearchParameters != null &&
                       capabilities.SupportedTvSearchParameters.Contains("season") &&
                       capabilities.SupportedTvSearchParameters.Contains("ep");
            }
        }

        private bool SupportsAggregatedIdSearch
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.SupportsAggregateIdSearch;
            }
        }

        private string TextSearchEngine
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.TextSearchEngine;
            }
        }

        private string TvTextSearchEngine
        {
            get
            {
                var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

                return capabilities.TvTextSearchEngine;
            }
        }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();

            var capabilities = _capabilitiesProvider.GetCapabilities(Settings);

            if (capabilities.SupportedTvSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.Categories.Concat(Settings.AnimeCategories), "tvsearch", ""));
            }
            else if (capabilities.SupportedSearchParameters != null)
            {
                pageableRequests.Add(GetPagedRequests(MaxPages, Settings.AnimeCategories, "search", ""));
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SingleEpisodeSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            // Book-search-only indexers (e.g. AudioBookBay) have no
            // season/ep params at all -- they're a flat title query,
            // relying on AddTitlePageableRequests' book branch plus
            // release-title parsing downstream (same "Book NNN" parsing
            // used for file matching) to land on the right episode. Skip
            // the season/ep capability requirement for them rather than
            // bailing out before ever reaching that branch.
            if (!SupportsEpisodeSearch && !SupportsBookQuerySearch)
            {
                _logger.Debug("Indexer capabilities lacking season & ep query parameters, no Standard series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            if (!SupportsTvTextSearches && !SupportsTvIdSearches && !SupportsBookQuerySearch)
            {
                _logger.Debug("Indexer capabilities lacking q, title, tvdbid, imdbid, rid and tvmazeid parameters, no Standard series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            var categories = GetSearchCategories(searchCriteria);

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchID) || searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTvIdPageableRequests(pageableRequests,
                    categories,
                    searchCriteria,
                    $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}&ep={searchCriteria.EpisodeNumber}");
            }

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchTitle))
            {
                AddTitlePageableRequests(pageableRequests,
                    categories,
                    searchCriteria,
                    $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}&ep={searchCriteria.EpisodeNumber}");
            }

            pageableRequests.AddTier();

            if (searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTitlePageableRequests(pageableRequests,
                    categories,
                    searchCriteria,
                    $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}&ep={searchCriteria.EpisodeNumber}");
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SeasonSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (!SupportsSeasonSearch)
            {
                _logger.Debug("Indexer capabilities lacking season query parameter, no Standard series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            if (!SupportsTvTextSearches && !SupportsTvIdSearches)
            {
                _logger.Debug("Indexer capabilities lacking q, title, tvdbid, imdbid, rid and tvmazeid parameters, no Standard series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchID) || searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTvIdPageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}");
            }

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchTitle))
            {
                AddTitlePageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}");
            }

            pageableRequests.AddTier();

            if (searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTitlePageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}");
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(DailyEpisodeSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (!SupportsEpisodeSearch)
            {
                _logger.Debug("Indexer capabilities lacking season & ep query parameters, no Daily series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            if (!SupportsTvTextSearches && !SupportsTvIdSearches)
            {
                _logger.Debug("Indexer capabilities lacking q, title, tvdbid, imdbid, rid and tvmazeid parameters, no Daily series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchID) || searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTvIdPageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={searchCriteria.AirDate:yyyy}&ep={searchCriteria.AirDate:MM}/{searchCriteria.AirDate:dd}");
            }

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchTitle))
            {
                AddTitlePageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={searchCriteria.AirDate:yyyy}&ep={searchCriteria.AirDate:MM}/{searchCriteria.AirDate:dd}");
            }

            pageableRequests.AddTier();

            if (searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTitlePageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={searchCriteria.AirDate:yyyy}&ep={searchCriteria.AirDate:MM}/{searchCriteria.AirDate:dd}");
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(DailySeasonSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (!SupportsEpisodeSearch)
            {
                _logger.Debug("Indexer capabilities lacking season query parameter, no Daily series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            if (!SupportsTvTextSearches && !SupportsTvIdSearches)
            {
                _logger.Debug("Indexer capabilities lacking q, title, tvdbid, imdbid, rid and tvmazeid parameters, no Daily series search possible: {0}", Definition.Name);

                return pageableRequests;
            }

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchID) || searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTvIdPageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={searchCriteria.Year}");
            }

            if (searchCriteria.SearchMode.HasFlag(SearchMode.SearchTitle))
            {
                AddTitlePageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={searchCriteria.Year}");
            }

            pageableRequests.AddTier();

            if (searchCriteria.SearchMode == SearchMode.Default)
            {
                AddTitlePageableRequests(pageableRequests,
                    Settings.Categories,
                    searchCriteria,
                    $"&season={searchCriteria.Year}");
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AnimeEpisodeSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (SupportsSearch)
            {
                AddTvIdPageableRequests(pageableRequests,
                    Settings.AnimeCategories,
                    searchCriteria,
                    $"&q={searchCriteria.AbsoluteEpisodeNumber:00}");

                var includeAnimeStandardFormatSearch = Settings.AnimeStandardFormatSearch &&
                                                       searchCriteria.SeasonNumber > 0 &&
                                                       searchCriteria.EpisodeNumber > 0;

                if (includeAnimeStandardFormatSearch && SupportsEpisodeSearch)
                {
                    AddTvIdPageableRequests(pageableRequests,
                        Settings.AnimeCategories,
                        searchCriteria,
                        $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}&ep={searchCriteria.EpisodeNumber}");
                }

                var queryTitles = TextSearchEngine == "raw" ? searchCriteria.AllSceneTitles : searchCriteria.CleanSceneTitles;

                foreach (var queryTitle in queryTitles)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        Settings.AnimeCategories,
                        "search",
                        $"&q={NewsnabifyTitle(queryTitle)}+{searchCriteria.AbsoluteEpisodeNumber:00}"));

                    if (includeAnimeStandardFormatSearch && SupportsEpisodeSearch)
                    {
                        pageableRequests.Add(GetPagedRequests(MaxPages,
                            Settings.AnimeCategories,
                            "tvsearch",
                            $"&q={NewsnabifyTitle(queryTitle)}&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}&ep={searchCriteria.EpisodeNumber}"));
                    }

                    // Book-search-only indexers (e.g. AudioBookBay) don't
                    // match the combined "title+NN" query above -- their
                    // search engines expect a plain title query, with the
                    // right book/episode picked out downstream by release
                    // title parsing (same as AddTitlePageableRequests'
                    // book branch for the single-episode search path).
                    if (SupportsBookQuerySearch)
                    {
                        pageableRequests.Add(GetPagedRequests(MaxPages,
                            Settings.AnimeCategories,
                            "book",
                            $"&q={NewsnabifyTitle(queryTitle)}"));
                    }
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(AnimeSeasonSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (SupportsSearch && Settings.AnimeStandardFormatSearch && searchCriteria.SeasonNumber > 0)
            {
                AddTvIdPageableRequests(pageableRequests,
                    Settings.AnimeCategories,
                    searchCriteria,
                    $"&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}");

                var queryTitles = TextSearchEngine == "raw" ? searchCriteria.AllSceneTitles : searchCriteria.CleanSceneTitles;

                foreach (var queryTitle in queryTitles)
                {
                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        Settings.AnimeCategories,
                        "tvsearch",
                        $"&q={NewsnabifyTitle(queryTitle)}&season={NewznabifySeasonNumber(searchCriteria.SeasonNumber)}"));
                }
            }

            return pageableRequests;
        }

        public virtual IndexerPageableRequestChain GetSearchRequests(SpecialEpisodeSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();

            if (SupportsSearch)
            {
                var categories = GetSearchCategories(searchCriteria);

                foreach (var queryTitle in searchCriteria.EpisodeQueryTitles)
                {
                    var query = queryTitle.Replace('+', ' ');
                    query = System.Web.HttpUtility.UrlEncode(query);

                    pageableRequests.Add(GetPagedRequests(MaxPages,
                        categories,
                        "search",
                        $"&q={query}"));
                }
            }

            return pageableRequests;
        }

        private void AddTvIdPageableRequests(IndexerPageableRequestChain chain, IEnumerable<int> categories, SearchCriteriaBase searchCriteria, string parameters)
        {
            // ID-based search is always disabled here -- Series.TvdbId is a
            // locally-assigned opaque id (see AudibleSeriesMap) and
            // Series.ImdbId holds an Audible ASIN, not a real IMDb id.
            // Both are always non-empty by design, so the unmodified checks
            // below would always fire whenever an indexer/Prowlarr
            // advertises tvdbid/imdbid support, sending meaningless ids to
            // real indexers -- confirmed live: NZBFinder hard-rejected
            // imdbid=<ASIN> with a 400 "Incorrect parameter" error, and
            // other indexers returned nothing since neither id corresponds
            // to anything they can look up. Text search (AddTitlePageableRequests,
            // tried as the next tier) is the only search mode that makes
            // sense for this fork.
            var includeTvdbSearch = false;
            var includeImdbSearch = false;
            var includeTvRageSearch = SupportsTvRageSearch && searchCriteria.Series.TvRageId > 0;
            var includeTvMazeSearch = SupportsTvMazeSearch && searchCriteria.Series.TvMazeId > 0;
            var includeTmdbSearch = SupportsTmdbSearch && searchCriteria.Series.TmdbId > 0;

            if (SupportsAggregatedIdSearch && (includeTvdbSearch || includeTvRageSearch || includeTvMazeSearch || includeTmdbSearch))
            {
                var ids = "";

                if (includeTvdbSearch)
                {
                    ids += "&tvdbid=" + searchCriteria.Series.TvdbId;
                }

                if (includeImdbSearch)
                {
                    ids += "&imdbid=" + searchCriteria.Series.ImdbId;
                }

                if (includeTvRageSearch)
                {
                    ids += "&rid=" + searchCriteria.Series.TvRageId;
                }

                if (includeTvMazeSearch)
                {
                    ids += "&tvmazeid=" + searchCriteria.Series.TvMazeId;
                }

                if (includeTmdbSearch)
                {
                    ids += "&tmdbid=" + searchCriteria.Series.TmdbId;
                }

                chain.Add(GetPagedRequests(MaxPages, categories, "tvsearch", ids + parameters));
            }
            else
            {
                if (includeTvdbSearch)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        categories,
                        "tvsearch",
                        $"&tvdbid={searchCriteria.Series.TvdbId}{parameters}"));
                }
                else if (includeImdbSearch)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        categories,
                        "tvsearch",
                        $"&imdbid={searchCriteria.Series.ImdbId}{parameters}"));
                }
                else if (includeTvRageSearch)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        categories,
                        "tvsearch",
                        $"&rid={searchCriteria.Series.TvRageId}{parameters}"));
                }
                else if (includeTvMazeSearch)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        categories,
                        "tvsearch",
                        $"&tvmazeid={searchCriteria.Series.TvMazeId}{parameters}"));
                }
                else if (includeTmdbSearch)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        categories,
                        "tvsearch",
                        $"&tmdbid={searchCriteria.Series.TmdbId}{parameters}"));
                }
            }
        }

        private void AddTitlePageableRequests(IndexerPageableRequestChain chain, IEnumerable<int> categories, SearchCriteriaBase searchCriteria, string parameters)
        {
            if (SupportsTvTitleSearch)
            {
                foreach (var searchTerm in searchCriteria.SceneTitles)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "tvsearch",
                        $"&title={Uri.EscapeDataString(searchTerm)}{parameters}"));
                }
            }
            else if (SupportsTvQuerySearch)
            {
                var queryTitles = TvTextSearchEngine == "raw" ? searchCriteria.AllSceneTitles : searchCriteria.CleanSceneTitles;
                foreach (var queryTitle in queryTitles)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "tvsearch",
                        $"&q={NewsnabifyTitle(queryTitle)}{parameters}"));
                }
            }
            else if (SupportsBookQuerySearch)
            {
                foreach (var queryTitle in searchCriteria.CleanSceneTitles)
                {
                    chain.Add(GetPagedRequests(MaxPages,
                        Settings.Categories,
                        "book",
                        $"&q={NewsnabifyTitle(queryTitle)}"));
                }
            }
        }

        private IEnumerable<IndexerRequest> GetPagedRequests(int maxPages, IEnumerable<int> categories, string searchType, string parameters)
        {
            if (categories.Empty())
            {
                yield break;
            }

            var categoriesQuery = string.Join(",", categories.Distinct());

            var baseUrl =
                $"{Settings.BaseUrl.TrimEnd('/')}{Settings.ApiPath.TrimEnd('/')}?t={searchType}&cat={categoriesQuery}&extended=1{Settings.AdditionalParameters}";

            if (Settings.ApiKey.IsNotNullOrWhiteSpace())
            {
                baseUrl += "&apikey=" + Settings.ApiKey;
            }

            if (PageSize == 0)
            {
                yield return new IndexerRequest($"{baseUrl}{parameters}", HttpAccept.Rss);
            }
            else
            {
                for (var page = 0; page < maxPages; page++)
                {
                    yield return new IndexerRequest($"{baseUrl}&offset={page * PageSize}&limit={PageSize}{parameters}", HttpAccept.Rss);
                }
            }
        }

        private static string NewsnabifyTitle(string title)
        {
            title = title.Replace("+", " ");
            return Uri.EscapeDataString(title);
        }

        // Temporary workaround for NNTMux considering season=0 -> null. '00' should work on existing newznab indexers.
        private static string NewznabifySeasonNumber(int seasonNumber)
        {
            return seasonNumber == 0 ? "00" : seasonNumber.ToString();
        }

        private IList<int> GetSearchCategories(SearchCriteriaBase searchCriteria)
        {
            return searchCriteria.Series?.SeriesType is SeriesTypes.Anime
                ? Settings.AnimeCategories.ToList()
                : Settings.Categories.ToList();
        }
    }
}
