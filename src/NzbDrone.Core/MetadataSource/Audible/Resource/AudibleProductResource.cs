using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.Audible.Resource
{
    // Shape confirmed live against api.audible.com's undocumented catalog
    // endpoint (both the "search by keywords" and the bulk "lookup by asins"
    // forms return this same product shape). Audible's own JSON uses
    // snake_case, hence the explicit JsonProperty names below -- Sonarr's
    // default Newtonsoft camelCase resolver only case-folds the first
    // letter, it doesn't strip underscores.
    public class AudibleProductResource
    {
        public string Asin { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }

        [JsonProperty("merchandising_summary")]
        public string MerchandisingSummary { get; set; }

        [JsonProperty("release_date")]
        public string ReleaseDate { get; set; }

        [JsonProperty("runtime_length_min")]
        public int? RuntimeLengthMin { get; set; }

        public List<AudibleContributorResource> Authors { get; set; } = new List<AudibleContributorResource>();
        public List<AudibleContributorResource> Narrators { get; set; } = new List<AudibleContributorResource>();
        public List<AudibleSeriesRefResource> Series { get; set; } = new List<AudibleSeriesRefResource>();

        [JsonProperty("product_images")]
        public Dictionary<string, string> ProductImages { get; set; } = new Dictionary<string, string>();
    }

    public class AudibleProductSearchResponse
    {
        public List<AudibleProductResource> Products { get; set; } = new List<AudibleProductResource>();
    }
}
