namespace NzbDrone.Core.MetadataSource.Audible
{
    public interface IAudibleIdMapService
    {
        int GetOrCreateId(string asin);
        string FindAsin(int id);
    }

    public class AudibleIdMapService : IAudibleIdMapService
    {
        private readonly IAudibleSeriesMapRepository _repository;

        public AudibleIdMapService(IAudibleSeriesMapRepository repository)
        {
            _repository = repository;
        }

        public int GetOrCreateId(string asin)
        {
            var existing = _repository.FindByAsin(asin);

            if (existing != null)
            {
                return existing.Id;
            }

            var created = _repository.Insert(new AudibleSeriesMap { Asin = asin });

            return created.Id;
        }

        public string FindAsin(int id)
        {
            return _repository.Find(id)?.Asin;
        }
    }
}
