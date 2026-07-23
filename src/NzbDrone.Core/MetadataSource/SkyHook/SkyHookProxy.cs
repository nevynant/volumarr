using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Audible;
using NzbDrone.Core.MetadataSource.Audible.Resource;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.SkyHook
{
    // Despite the "SkyHook"/TVDB-shaped class name and interfaces inherited
    // from Sonarr (kept unchanged -- see project rebranding notes: renaming
    // types is out of scope, only behavior changes), this now talks to
    // Audible's undocumented catalog API instead of TheTVDB.
    // IProvideSeriesInfo.GetSeriesInfo(int) and the rest of ISearchForNewSeries
    // are TVDB-shaped contracts (int ids) that can't be changed without
    // touching every caller, so Series.TvdbId is repurposed as an opaque,
    // locally-assigned id (see AudibleSeriesMap) and Series.ImdbId holds the
    // real Audible series ASIN used for every actual API call.
    public class SkyHookProxy : IProvideSeriesInfo, ISearchForNewSeries
    {
        private const string BaseUrl = "https://api.audible.com/1.0";

        // Audible ASINs are marketplace-specific -- the US and UK catalogs
        // use entirely separate id spaces for the same book. Confirmed
        // live: none of this fork's existing US-sourced series resolve at
        // all against the UK host, and conversely a UK-exclusive release
        // ("Operation Bounce House") returns nothing from the US host.
        // GetSeriesDetail tries US first and only falls back to UK if that
        // comes up empty, so nothing already working changes.
        private const string BaseUrlUk = "https://api.audible.co.uk/1.0";
        private const string Marketplace = "us";

        // Synthetic "asin" prefix for locally-defined series (see
        // LoadManualSeries) -- content Audible has no listing for at all.
        private const string ManualAsinPrefix = "manual:";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ISeriesService _seriesService;
        private readonly IAudibleIdMapService _idMapService;
        private readonly IAppFolderInfo _appFolderInfo;

        public SkyHookProxy(IHttpClient httpClient,
                            ISeriesService seriesService,
                            IAudibleIdMapService idMapService,
                            IAppFolderInfo appFolderInfo,
                            Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _seriesService = seriesService;
            _idMapService = idMapService;
            _appFolderInfo = appFolderInfo;
        }

        public Tuple<Series, List<Episode>> GetSeriesInfo(int tvdbSeriesId)
        {
            var asin = _idMapService.FindAsin(tvdbSeriesId);

            if (asin == null)
            {
                throw new SeriesNotFoundException(tvdbSeriesId);
            }

            if (asin.StartsWith(ManualAsinPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return GetManualSeriesInfo(asin, tvdbSeriesId);
            }

            var detail = GetSeriesDetail(asin, out var resolvedBaseUrl);

            if (detail == null)
            {
                throw new SeriesNotFoundException(tvdbSeriesId);
            }

            // If the stored ASIN is a single BOOK rather than a series
            // parent (the user added the series by pasting a product-page
            // URL), follow its parent-series relationship so the full book
            // list resolves instead of an empty one.
            var parentAsin = detail.Relationships?
                .FirstOrDefault(r => r.RelationshipToProduct == "parent" && r.RelationshipType == "series")?.Asin;

            if (parentAsin.IsNotNullOrWhiteSpace())
            {
                var parentDetail = FetchSeriesDetail(resolvedBaseUrl, parentAsin);

                if (parentDetail != null)
                {
                    detail = parentDetail;
                }
            }

            var positions = ResolveBookPositions(detail.Relationships, out var alternateEditions);

            // Standalone book with NO series relationships at all (real
            // case: "Operation Bounce House" -- Audible returns an empty
            // relationships list): synthesize a one-book series from the
            // product itself instead of an empty episode list.
            if (positions.Count == 0 && detail.Asin.IsNotNullOrWhiteSpace())
            {
                positions = new Dictionary<string, int> { { detail.Asin, 1 } };
            }

            var books = GetProductsByAsins(positions.Keys.ToList(), resolvedBaseUrl);

            PatchPlaceholderReleaseDates(books, alternateEditions, resolvedBaseUrl);

            // Season 1 = audiobooks, Season 2 = ebook editions of the same
            // books (one Sonarr episode can only hold one file, so the two
            // formats need separate episodes). Ebook absolutes are offset
            // (see Parser.EbookAbsoluteEpisodeOffset) so .epub files can't
            // steal the audiobook slots during file matching.
            var audiobookEpisodes = books.Select(b => MapEpisode(b, positions.GetValueOrDefault(b.Asin))).ToList();
            var ebookEpisodes = audiobookEpisodes.Select(MapEbookEpisode).ToList();
            var episodes = audiobookEpisodes.Concat(ebookEpisodes).ToList();
            var series = MapSeriesFromDetail(detail, tvdbSeriesId, books);

            return new Tuple<Series, List<Episode>>(series, episodes);
        }

        public List<Series> SearchForNewSeriesByImdbId(string imdbId)
        {
            // ImdbId is repurposed to hold the Audible series ASIN directly
            // (see AudibleSeriesMap's docs) -- this lets "add by ASIN" reuse
            // the same interface method Sonarr uses for IMDb cross-references.
            if (imdbId.IsNullOrWhiteSpace())
            {
                return new List<Series>();
            }

            try
            {
                var existing = _seriesService.FindByImdbId(imdbId);
                if (existing != null)
                {
                    return new List<Series> { existing };
                }

                var detail = GetSeriesDetail(imdbId, out var resolvedBaseUrl);
                if (detail == null)
                {
                    return new List<Series>();
                }

                var id = _idMapService.GetOrCreateId(detail.Asin);
                var firstBookAsin = detail.Relationships
                    .FirstOrDefault(r => r.RelationshipToProduct == "child" && r.RelationshipType == "series")?.Asin;
                var sampleBooks = firstBookAsin != null
                    ? GetProductsByAsins(new List<string> { firstBookAsin }, resolvedBaseUrl)
                    : new List<AudibleProductResource>();

                return new List<Series> { MapSeriesFromDetail(detail, id, sampleBooks) };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to look up Audible series by ASIN '{0}'", imdbId);
                return new List<Series>();
            }
        }

        // AniList/MyAnimeList/TMDb are TV/anime-specific external databases
        // with no Audible equivalent -- genuinely inapplicable, not a gap.
        public List<Series> SearchForNewSeriesByAniListId(int aniListId) => new List<Series>();

        public List<Series> SearchForNewSeriesByMyAnimeListId(int malId) => new List<Series>();

        public List<Series> SearchForNewSeriesByTmdbId(int tmdbId) => new List<Series>();

        public List<Series> SearchForNewSeries(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<Series>();
            }

            // Manual series first, so locally-defined entries surface above
            // Audible's fuzzy matches. Also kept if the Audible call fails:
            // a network error shouldn't hide series that live on local disk.
            var manualResults = SearchManualSeries(title);

            try
            {
                var httpRequest = new HttpRequestBuilder(BaseUrl)
                    .Resource("/catalog/products")
                    .Accept(HttpAccept.Json)
                    .AddQueryParam("keywords", title)
                    .AddQueryParam("num_results", "15")
                    .AddQueryParam("response_groups", "series,product_desc,product_attrs,contributors,media")
                    .AddQueryParam("marketplace", Marketplace)
                    .Build();

                var httpResponse = _httpClient.Get<AudibleProductSearchResponse>(httpRequest);

                var seenSeriesAsins = new HashSet<string>();
                var results = new List<Series>();

                foreach (var product in httpResponse.Resource.Products)
                {
                    var seriesRef = product.Series.FirstOrDefault();

                    // Volumarr tracks *series* completion -- a standalone
                    // audiobook with no series isn't something it can track,
                    // so it's dropped here rather than shown as a result
                    // nothing further can be done with.
                    if (seriesRef == null || !seenSeriesAsins.Add(seriesRef.Asin))
                    {
                        continue;
                    }

                    var existing = _seriesService.FindByImdbId(seriesRef.Asin);
                    if (existing != null)
                    {
                        results.Add(existing);
                        continue;
                    }

                    var id = _idMapService.GetOrCreateId(seriesRef.Asin);
                    var detail = new AudibleSeriesDetailResource
                    {
                        Asin = seriesRef.Asin,
                        Title = seriesRef.Title,
                        Authors = product.Authors,
                    };

                    results.Add(MapSeriesFromDetail(detail, id, new List<AudibleProductResource> { product }));
                }

                return manualResults.Concat(results).ToList();
            }
            catch (HttpException ex)
            {
                if (manualResults.Any())
                {
                    _logger.Warn(ex, "Audible search failed; returning manual series matches only");
                    return manualResults;
                }

                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Unable to communicate with Audible. {1}", ex, title, ex.Message);
            }
            catch (WebException ex)
            {
                if (manualResults.Any())
                {
                    _logger.Warn(ex, "Audible search failed; returning manual series matches only");
                    return manualResults;
                }

                _logger.Warn(ex);
                throw new SkyHookException("Search for '{0}' failed. Unable to communicate with Audible. {1}", ex, title, ex.Message);
            }
        }

        private AudibleSeriesDetailResource GetSeriesDetail(string seriesAsin)
        {
            return GetSeriesDetail(seriesAsin, out _);
        }

        private AudibleSeriesDetailResource GetSeriesDetail(string seriesAsin, out string resolvedBaseUrl)
        {
            var detail = FetchSeriesDetail(BaseUrl, seriesAsin);

            if (detail != null)
            {
                resolvedBaseUrl = BaseUrl;
                return detail;
            }

            resolvedBaseUrl = BaseUrlUk;
            return FetchSeriesDetail(BaseUrlUk, seriesAsin);
        }

        private AudibleSeriesDetailResource FetchSeriesDetail(string baseUrl, string seriesAsin)
        {
            var httpRequest = new HttpRequestBuilder(baseUrl)
                .Resource($"/catalog/products/{seriesAsin}")
                .Accept(HttpAccept.Json)
                .AddQueryParam("response_groups", "relationships,product_desc,contributors")
                .AddQueryParam("marketplace", Marketplace)
                .Build();

            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<AudibleSeriesDetailResponse>(httpRequest);

            if (httpResponse.HasHttpError)
            {
                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw new HttpException(httpRequest, httpResponse);
            }

            var product = httpResponse.Resource.Product;

            // A marketplace that doesn't have this ASIN returns 200 OK with
            // an otherwise-empty product (just the asin echoed back) rather
            // than a 404 -- confirmed live against api.audible.com for a
            // UK-exclusive ASIN. Treat that the same as "not found" so the
            // US/UK fallback above actually triggers.
            return product?.Title.IsNotNullOrWhiteSpace() == true ? product : null;
        }

        private List<AudibleProductResource> GetProductsByAsins(List<string> asins, string baseUrl = BaseUrl)
        {
            var products = new List<AudibleProductResource>();

            if (asins.Count == 0)
            {
                return products;
            }

            // Confirmed live: response_groups must include product_attrs, not
            // just product_desc, or the bulk endpoint silently omits
            // release_date -- even though the single-product endpoint above
            // includes it under product_desc alone. Chunked because Audible's
            // bulk endpoint has a practical limit on asins per request.
            foreach (var chunk in asins.Chunk(50))
            {
                var httpRequest = new HttpRequestBuilder(baseUrl)
                    .Resource("/catalog/products")
                    .Accept(HttpAccept.Json)
                    .AddQueryParam("asins", string.Join(",", chunk))
                    .AddQueryParam("response_groups", "product_desc,product_attrs,series,contributors,media")
                    .AddQueryParam("marketplace", Marketplace)
                    .Build();

                var httpResponse = _httpClient.Get<AudibleProductSearchResponse>(httpRequest);
                products.AddRange(httpResponse.Resource.Products);
            }

            return products;
        }

        private static Series MapSeriesFromDetail(AudibleSeriesDetailResource detail, int syntheticId, List<AudibleProductResource> sampleBooks)
        {
            var series = new Series
            {
                TvdbId = syntheticId,
                ImdbId = detail.Asin,
                Title = detail.Title,
                CleanTitle = Parser.Parser.CleanSeriesTitle(detail.Title),
                SortTitle = SeriesTitleNormalizer.Normalize(detail.Title, syntheticId),
                TitleSlug = syntheticId.ToString(),
                Status = SeriesStatusType.Continuing,
                Monitored = true,
                // Season 1 = audiobooks, Season 2 = ebooks; both monitored
                // so missing ebooks count as gaps (deliberate -- the user
                // wants complete m4b+epub pairs for Storyteller readalongs).
                Seasons = new List<Season>
                {
                    new Season { SeasonNumber = 1, Monitored = true },
                    new Season { SeasonNumber = 2, Monitored = true }
                },
                Ratings = new Ratings(),
            };

            var firstBookWithSummary = sampleBooks.FirstOrDefault(b => b.MerchandisingSummary.IsNotNullOrWhiteSpace());
            series.Overview = StripHtml(firstBookWithSummary?.MerchandisingSummary);

            var authorNames = sampleBooks.SelectMany(b => b.Authors).Select(a => a.Name)
                .Concat((detail.Authors ?? new List<AudibleContributorResource>()).Select(a => a.Name))
                .Where(n => n.IsNotNullOrWhiteSpace())
                .Distinct()
                .ToList();
            series.Network = authorNames.Count > 0 ? string.Join(", ", authorNames) : null;

            var cover = sampleBooks.FirstOrDefault(b => b.ProductImages != null && b.ProductImages.ContainsKey("500"));
            if (cover != null)
            {
                series.Images = new List<MediaCover.MediaCover>
                {
                    new MediaCover.MediaCover(MediaCoverTypes.Poster, cover.ProductImages["500"]),
                };
            }

            return series;
        }

        // Audible's series relationships list is messier than a clean 1:1
        // book->position mapping -- confirmed live against a real series
        // (The Beginning After the End): it mixes in omnibus/bundle entries
        // (sequence "1-2", spanning multiple books in one product) and
        // duplicate editions of the same book under different ASINs (e.g.
        // two different ASINs both at sequence "1"), alongside genuine
        // fractional companion novellas (sequence "8.5", which the plain
        // ParseSequenceNumber/Math.Round path collided straight into book
        // 8 -- Math.Round(8.5) rounds to even, i.e. 8, silently dropping
        // one of the two). This resolves all three: bundle/range entries
        // are dropped entirely (their contents are already listed
        // individually elsewhere in the same relationships list),
        // duplicate editions keep only the first ASIN seen per sequence,
        // and fractional entries get their own slot (floor*1000+500, e.g.
        // 8.5 -> 8500) distinct from any whole number.
        //
        // Whole-numbered books deliberately keep their exact original
        // position, even in a series that also has a fractional book --
        // an earlier version of this method scaled EVERY position x10
        // whenever any fraction was present, which broke the
        // episode/file link for every already-imported book in a real
        // series (their on-disk filenames still contain the un-scaled
        // "Book 008" text, confirmed live: RescanSeries could not
        // reconnect them under the new numbers). Only the fractional
        // entry itself gets a special-cased position; everything else is
        // untouched.
        private static Dictionary<string, int> ResolveBookPositions(List<AudibleRelationshipResource> relationships, out Dictionary<string, List<string>> alternateEditions)
        {
            var bySequence = new Dictionary<decimal, List<string>>();

            foreach (var rel in relationships)
            {
                if (rel.RelationshipToProduct != "child" || rel.RelationshipType != "series")
                {
                    continue;
                }

                var sequence = rel.Sequence;

                if (sequence.IsNullOrWhiteSpace() || sequence.Contains('-'))
                {
                    continue;
                }

                // AllowDecimalPoint only, not NumberStyles.Any -- Any accepts
                // thousands separators, so a hypothetical "1,2" sequence
                // would silently parse as book 12 instead of being skipped
                // as unparseable like the "1-2" range form is.
                if (!decimal.TryParse(sequence, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }

                if (!bySequence.TryGetValue(value, out var asins))
                {
                    asins = new List<string>();
                    bySequence[value] = asins;
                }

                asins.Add(rel.Asin);
            }

            // Fractional books slot at floor*1000+500 (8.5 -> 8500). The
            // encoding is deliberately self-identifying -- no real series
            // has a book numbered 8500 -- so the frontend can decode it
            // back to "8.5" for display and sorting with no extra data
            // (see frontend/src/Utilities/Number/bookNumber.ts). Two
            // distinct fractional sequences between the same neighbors
            // (8.2 and 8.5) would both slot to 8500 -- first by sequence
            // order wins, the rest are dropped rather than creating
            // duplicate episode numbers.
            //
            // Duplicate editions of one book (same sequence, multiple
            // ASINs -- real Audible data, e.g. TBATE books 1-4 and 11):
            // the first stays the tracked edition, but the rest are no
            // longer discarded -- they're returned as alternates, because
            // the tracked edition sometimes carries Audible's 2200-01-01
            // "no date" sentinel while an alternate has the real release
            // date (confirmed live for TBATE books 2, 3 and 11).
            var positions = new Dictionary<string, int>();
            var usedPositions = new HashSet<int>();
            alternateEditions = new Dictionary<string, List<string>>();

            foreach (var kvp in bySequence.OrderBy(k => k.Key))
            {
                var position = SlotForPosition(kvp.Key);

                if (usedPositions.Add(position))
                {
                    positions[kvp.Value[0]] = position;

                    if (kvp.Value.Count > 1)
                    {
                        alternateEditions[kvp.Value[0]] = kvp.Value.Skip(1).ToList();
                    }
                }
            }

            return positions;
        }

        // Whole positions map to themselves; fractional books slot at
        // floor*1000+500 (8.5 -> 8500) -- see ResolveBookPositions' comment
        // and frontend/src/Utilities/Number/bookNumber.ts.
        private static int SlotForPosition(decimal position)
        {
            return position % 1 == 0
                ? (int)position
                : ((int)Math.Floor(position) * 1000) + 500;
        }

        // Manual series definitions: JSON files in <AppData>/manual-series,
        // one per series, for content Audible has no listing for at all
        // (real case: "Kaiju Battlefield Surgeon", a Soundbooth Theater
        // exclusive -- it previously could only be tracked by wearing a
        // WRONG Audible identity). The filename minus ".json" is the stable
        // key; the synthetic "asin" stored in the id map is "manual:<key>".
        // Shape:
        //   {
        //     "title": "...",
        //     "overview": "...",
        //     "authors": ["..."],
        //     "books": [
        //       { "position": 1, "title": "...", "releaseDate": "yyyy-MM-dd" }
        //     ]
        //   }
        // "position" accepts fractions (8.5) using the same slotting as
        // Audible sequences; "releaseDate" is optional.
        private class ManualSeriesResource
        {
            public string Title { get; set; }
            public string Overview { get; set; }
            public List<string> Authors { get; set; }
            public List<ManualBookResource> Books { get; set; } = new List<ManualBookResource>();
        }

        private class ManualBookResource
        {
            public decimal Position { get; set; }
            public string Title { get; set; }
            public string ReleaseDate { get; set; }
        }

        private string ManualSeriesFolder => Path.Combine(_appFolderInfo.AppDataFolder, "manual-series");

        private Dictionary<string, ManualSeriesResource> LoadManualSeries()
        {
            var result = new Dictionary<string, ManualSeriesResource>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(ManualSeriesFolder))
            {
                return result;
            }

            foreach (var file in Directory.GetFiles(ManualSeriesFolder, "*.json"))
            {
                try
                {
                    var manual = Json.Deserialize<ManualSeriesResource>(File.ReadAllText(file));

                    if (manual?.Title.IsNotNullOrWhiteSpace() == true)
                    {
                        result[Path.GetFileNameWithoutExtension(file)] = manual;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Ignoring invalid manual series file {0}", file);
                }
            }

            return result;
        }

        private List<Series> SearchManualSeries(string title)
        {
            var results = new List<Series>();

            foreach (var kvp in LoadManualSeries())
            {
                if (!kvp.Value.Title.ContainsIgnoreCase(title) && !title.ContainsIgnoreCase(kvp.Value.Title))
                {
                    continue;
                }

                var asin = ManualAsinPrefix + kvp.Key;
                var existing = _seriesService.FindByImdbId(asin);

                if (existing != null)
                {
                    results.Add(existing);
                    continue;
                }

                var id = _idMapService.GetOrCreateId(asin);
                results.Add(MapManualSeries(asin, id, kvp.Value));
            }

            return results;
        }

        private static Series MapManualSeries(string asin, int syntheticId, ManualSeriesResource manual)
        {
            var detail = new AudibleSeriesDetailResource
            {
                Asin = asin,
                Title = manual.Title,
                Authors = (manual.Authors ?? new List<string>())
                    .Select(a => new AudibleContributorResource { Name = a })
                    .ToList(),
            };

            var series = MapSeriesFromDetail(detail, syntheticId, new List<AudibleProductResource>());
            series.Overview = manual.Overview;

            return series;
        }

        private Tuple<Series, List<Episode>> GetManualSeriesInfo(string asin, int tvdbSeriesId)
        {
            var key = asin.Substring(ManualAsinPrefix.Length);

            if (!LoadManualSeries().TryGetValue(key, out var manual))
            {
                throw new SeriesNotFoundException(tvdbSeriesId);
            }

            var usedPositions = new HashSet<int>();
            var audiobookEpisodes = new List<Episode>();

            foreach (var book in manual.Books.Where(b => b.Title.IsNotNullOrWhiteSpace()).OrderBy(b => b.Position))
            {
                var position = SlotForPosition(book.Position);

                if (!usedPositions.Add(position))
                {
                    continue;
                }

                var episode = new Episode
                {
                    SeasonNumber = 1,
                    EpisodeNumber = position,
                    AbsoluteEpisodeNumber = position,
                    Title = book.Title,
                    Ratings = new Ratings(),
                };

                if (TryParseReleaseDate(book.ReleaseDate, out var releaseDate))
                {
                    episode.AirDate = releaseDate.ToString("yyyy-MM-dd");
                    episode.AirDateUtc = releaseDate;
                }

                audiobookEpisodes.Add(episode);
            }

            var episodes = audiobookEpisodes.Concat(audiobookEpisodes.Select(MapEbookEpisode)).ToList();
            var series = MapManualSeries(asin, tvdbSeriesId, manual);

            return new Tuple<Series, List<Episode>>(series, episodes);
        }

        // Audible uses far-future sentinel dates (2200-01-01, seen live) on
        // editions it has no real date for -- treat those as missing.
        private static bool TryParseReleaseDate(string releaseDate, out DateTime parsed)
        {
            if (!DateTime.TryParseExact(releaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return false;
            }

            return parsed.Year < 2100;
        }

        // For books whose tracked edition has no usable release date, borrow
        // one from an alternate edition of the same book (one extra batched
        // API call, only made when at least one book actually needs it).
        private void PatchPlaceholderReleaseDates(List<AudibleProductResource> books, Dictionary<string, List<string>> alternateEditions, string baseUrl)
        {
            var needingDates = books
                .Where(b => !TryParseReleaseDate(b.ReleaseDate, out _) && alternateEditions.ContainsKey(b.Asin))
                .ToList();

            if (!needingDates.Any())
            {
                return;
            }

            var altAsins = needingDates.SelectMany(b => alternateEditions[b.Asin]).Distinct().ToList();
            var altBooks = GetProductsByAsins(altAsins, baseUrl).ToDictionary(b => b.Asin);

            foreach (var book in needingDates)
            {
                foreach (var altAsin in alternateEditions[book.Asin])
                {
                    if (altBooks.TryGetValue(altAsin, out var alt) && TryParseReleaseDate(alt.ReleaseDate, out _))
                    {
                        book.ReleaseDate = alt.ReleaseDate;
                        break;
                    }
                }
            }
        }

        private static Episode MapEpisode(AudibleProductResource book, int? overridePosition = null)
        {
            var position = overridePosition ?? ParseSequenceNumber(book.Series.FirstOrDefault()?.Sequence);

            var episode = new Episode
            {
                SeasonNumber = 1,
                EpisodeNumber = position,

                // Files are matched to episodes via the absolute-numbering
                // path (see Parser.cs's "Book NNN" pattern and
                // ParsingService.GetAnimeEpisodes), which looks episodes up
                // by AbsoluteEpisodeNumber specifically, not EpisodeNumber --
                // confirmed live: leaving this unset meant real files parsed
                // correctly but never matched any episode, silently.
                AbsoluteEpisodeNumber = position,
                Title = book.Title,
                Overview = StripHtml(book.MerchandisingSummary),
                Runtime = book.RuntimeLengthMin ?? 0,
                Ratings = new Ratings(),
            };

            // TryParseReleaseDate also rejects Audible's 2200-01-01 "no
            // date" sentinel -- better to show no date than a bogus one.
            if (TryParseReleaseDate(book.ReleaseDate, out var releaseDate))
            {
                episode.AirDate = releaseDate.ToString("yyyy-MM-dd");
                episode.AirDateUtc = releaseDate;
            }

            if (book.ProductImages != null && book.ProductImages.TryGetValue("500", out var imageUrl))
            {
                episode.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Screenshot, imageUrl));
            }

            return episode;
        }

        private static Episode MapEbookEpisode(Episode audiobookEpisode)
        {
            return new Episode
            {
                SeasonNumber = 2,
                EpisodeNumber = audiobookEpisode.EpisodeNumber,
                AbsoluteEpisodeNumber = audiobookEpisode.AbsoluteEpisodeNumber + Parser.Parser.EbookAbsoluteEpisodeOffset,
                Title = audiobookEpisode.Title,
                Overview = audiobookEpisode.Overview,

                // Runtime is an audio concept; ebooks have none.
                Runtime = 0,
                Ratings = new Ratings(),
                AirDate = audiobookEpisode.AirDate,
                AirDateUtc = audiobookEpisode.AirDateUtc,
                Images = audiobookEpisode.Images.ToList(),
            };
        }

        private static int ParseSequenceNumber(string sequence)
        {
            if (sequence.IsNullOrWhiteSpace())
            {
                return 0;
            }

            // Audible sequence numbers are usually plain integers ("1", "2"),
            // but can be fractional for novellas ("8.5" -- confirmed present
            // in real libraries during the original Volumarr matcher work).
            // Episode.EpisodeNumber is an int (Sonarr's TV-shaped schema has
            // no fractional-episode concept), so a fractional sequence is
            // rounded here -- a known, deliberate limitation, not a silent
            // mishandling: a novella and its neighbour could round to the
            // same episode number.
            return double.TryParse(sequence, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? (int)Math.Round(value)
                : 0;
        }

        private static string StripHtml(string html)
        {
            if (html.IsNullOrWhiteSpace())
            {
                return html;
            }

            return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", string.Empty).Trim();
        }
    }
}
