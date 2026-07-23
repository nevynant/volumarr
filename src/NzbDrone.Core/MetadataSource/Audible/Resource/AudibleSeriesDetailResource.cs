using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.Audible.Resource
{
    // Shape confirmed live against GET /1.0/catalog/products/{asin}
    // ?response_groups=relationships,product_desc,contributors -- treating
    // the series' own ASIN as a "product" and reading its child relationships
    // is the only way to get a full book roster (Audible has no dedicated
    // /series/{asin} endpoint, confirmed during the original Volumarr
    // research this fork's Audible client design is based on).
    public class AudibleRelationshipResource
    {
        public string Asin { get; set; }

        [JsonProperty("relationship_to_product")]
        public string RelationshipToProduct { get; set; }

        [JsonProperty("relationship_type")]
        public string RelationshipType { get; set; }

        public string Sequence { get; set; }
    }

    public class AudibleSeriesDetailResource
    {
        public string Asin { get; set; }
        public string Title { get; set; }
        public List<AudibleContributorResource> Authors { get; set; } = new List<AudibleContributorResource>();
        public List<AudibleRelationshipResource> Relationships { get; set; } = new List<AudibleRelationshipResource>();
    }

    public class AudibleSeriesDetailResponse
    {
        public AudibleSeriesDetailResource Product { get; set; }
    }
}
