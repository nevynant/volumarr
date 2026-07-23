using System.Linq;
using NLog;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications.Search
{
    public class SingleEpisodeSearchMatchSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly Logger _logger;
        private readonly ISceneMappingService _sceneMappingService;

        public SingleEpisodeSearchMatchSpecification(ISceneMappingService sceneMappingService, Logger logger)
        {
            _logger = logger;
            _sceneMappingService = sceneMappingService;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, ReleaseDecisionInformation information)
        {
            var searchCriteria = information.SearchCriteria;

            if (searchCriteria == null)
            {
                return DownloadSpecDecision.Accept();
            }

            if (searchCriteria is SingleEpisodeSearchCriteria singleEpisodeSpec)
            {
                return IsSatisfiedBy(remoteEpisode, singleEpisodeSpec);
            }

            if (searchCriteria is AnimeEpisodeSearchCriteria animeEpisodeSpec)
            {
                return IsSatisfiedBy(remoteEpisode, animeEpisodeSpec);
            }

            return DownloadSpecDecision.Accept();
        }

        private DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, SingleEpisodeSearchCriteria singleEpisodeSpec)
        {
            // Absolute-numbered releases (audiobooks, anime) have no real
            // season/episode pair in the raw parse -- SeasonNumber defaults
            // to 0 and EpisodeNumbers stays empty, since the title only
            // ever yields an AbsoluteEpisodeNumber. By this point the
            // release has already been resolved to a specific episode via
            // that absolute number (remoteEpisode.Episodes), so check
            // against the resolved episode instead of the raw parse.
            if (remoteEpisode.ParsedEpisodeInfo.IsAbsoluteNumbering)
            {
                if (!remoteEpisode.Episodes.Any(e => e.SeasonNumber == singleEpisodeSpec.SeasonNumber && e.EpisodeNumber == singleEpisodeSpec.EpisodeNumber))
                {
                    _logger.Debug("Resolved episode does not match searched episode, skipping.");
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongEpisode, "Wrong episode");
                }

                return DownloadSpecDecision.Accept();
            }

            if (singleEpisodeSpec.SeasonNumber != remoteEpisode.ParsedEpisodeInfo.SeasonNumber)
            {
                _logger.Debug("Season number does not match searched season number, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongSeason, "Wrong season");
            }

            if (!remoteEpisode.ParsedEpisodeInfo.EpisodeNumbers.Any())
            {
                _logger.Debug("Full season result during single episode search, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.FullSeason, "Full season pack");
            }

            if (!remoteEpisode.ParsedEpisodeInfo.EpisodeNumbers.Contains(singleEpisodeSpec.EpisodeNumber))
            {
                _logger.Debug("Episode number does not match searched episode number, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.WrongEpisode, "Wrong episode");
            }

            return DownloadSpecDecision.Accept();
        }

        private DownloadSpecDecision IsSatisfiedBy(RemoteEpisode remoteEpisode, AnimeEpisodeSearchCriteria animeEpisodeSpec)
        {
            if (remoteEpisode.ParsedEpisodeInfo.FullSeason && !animeEpisodeSpec.IsSeasonSearch)
            {
                _logger.Debug("Full season result during single episode search, skipping.");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.FullSeason, "Full season pack");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
