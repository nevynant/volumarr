using System;
using System.IO;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Specifications
{
    public class HasAudioTrackSpecification : IImportDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public HasAudioTrackSpecification(Logger logger)
        {
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            // Ebook editions (.epub, Season 2) are text, not audio -- this
            // check exists to catch corrupted/fake audio releases and never
            // applies to them. Without this bypass, epubs were accepted or
            // rejected essentially at random depending on whether ffprobe
            // happened to find embedded cover art it treated as a stream:
            // confirmed live, a real user file (Heretical Fishing book 3's
            // epub) got a valid MediaInfo result with zero audio streams
            // and was rejected, while sibling books' identically-shaped
            // epub files returned null MediaInfo (ffprobe gave up on a
            // non-media container) and skipped the check entirely below.
            if (Path.GetExtension(localEpisode.Path).Equals(".epub", StringComparison.OrdinalIgnoreCase))
            {
                return ImportSpecDecision.Accept();
            }

            if (localEpisode.MediaInfo == null)
            {
                _logger.Debug("Failed to get media info from the file, make sure ffprobe is available, skipping check");
                return ImportSpecDecision.Accept();
            }

            if (localEpisode.MediaInfo.AudioStreams == null || localEpisode.MediaInfo.AudioStreams.Count == 0)
            {
                _logger.Debug("No audio tracks found in file");

                return ImportSpecDecision.Reject(ImportRejectionReason.NoAudio, "No audio tracks detected");
            }

            return ImportSpecDecision.Accept();
        }
    }
}
