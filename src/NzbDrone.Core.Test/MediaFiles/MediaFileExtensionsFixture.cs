using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class MediaFileExtensionsFixture : CoreTest
    {
        [TestCase(".m4b")]
        [TestCase(".m4a")]
        [TestCase(".mp3")]
        public void should_recognize_audiobook_extension(string extension)
        {
            MediaFileExtensions.Extensions.Should().Contain(extension);
            MediaFileExtensions.GetQualityForExtension(extension).Should().Be(Quality.SDTV);
        }
    }
}
