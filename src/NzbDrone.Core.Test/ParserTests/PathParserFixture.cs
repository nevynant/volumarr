using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class PathParserFixture : CoreTest
    {
        [TestCase(@"z:\tv shows\series title (2003)\Season 3\S03E05 - Title.mkv", 3, 5)]
        [TestCase(@"z:\tv showsseries title\Season 16\S16E03 - The Title.mkv", 16, 3)]
        [TestCase(@"z:\tv shows\series title\Specials\S00E16 - Dear Title - SD TV.avi", 0, 16)]
        [TestCase(@"D:\shares\TV Shows\series title\Season 2\S02E21 - 94 Title - 720p TV.mkv", 2, 21)]
        [TestCase(@"D:\shares\TV Shows\Series (2003)\Season 2\S02E21.avi", 2, 21)]
        [TestCase("C:/Test/TV/Series.4x05.HDTV.XviD-LOL", 4, 5)]
        [TestCase(@"P:\TV Shows\Series\Season 6\S06E13 - 5 to 9 - 720p BluRay.mkv", 6, 13)]
        [TestCase(@"S:\TV Drop\Series - 10x11 - Title [SDTV]\1011 - Title.avi", 10, 11)]
        [TestCase(@"/TV Drop/Series - 10x11 - Title [SDTV]/1011 - Title.avi", 10, 11)]
        [TestCase(@"S:\TV Drop\Series Title - 10x12 - 24 Hours of Development [SDTV]\1012 - Hours of Development.avi", 10, 12)]
        [TestCase(@"/TV Drop/Series Title - 10x12 - 24 Hours of Development [SDTV]/1012 - Hours of Development.avi", 10, 12)]
        [TestCase(@"S:\TV Drop\Series Title - 10x12 - 24 Hours of Development [SDTV]\Hours of Development.avi", 10, 12)]
        [TestCase(@"/TV Drop/Series Title - 10x12 - 24 Hours of Development [SDTV]/Hours of Development.avi", 10, 12)]
        [TestCase(@"E:\Downloads\tv\Series.Title.S01E01.720p.HDTV\ajifajjjeaeaeqwer_eppj.avi", 1, 1)]
        [TestCase(@"C:\Test\Unsorted\Series.Title.S01E01.720p.HDTV\tbbt101.avi", 1, 1)]
        [TestCase(@"C:\Test\Unsorted\Series.Title.S02E19.720p.BluRay.x264-SiNNERS-RP\ba27283b17c00d01193eacc02a8ba98eeb523a76.mkv", 2, 19)]
        [TestCase(@"C:\Test\Unsorted\Series.Title.S02E18.720p.BluRay.x264-SiNNERS-RP\45a55debe3856da318cc35882ad07e43cd32fd15.mkv", 2, 18)]
        [TestCase(@"C:\Test\Series\Season 01\01 Pilot (1080p HD).mkv", 1, 1)]
        [TestCase(@"C:\Test\Series\Season 01\1 Pilot (1080p HD).mkv", 1, 1)]
        [TestCase(@"C:\Test\Series\Season 1\02 Honor Thy Father (1080p HD).m4v", 1, 2)]
        [TestCase(@"C:\Test\Series\Season 1\2 Honor Thy Developer (1080p HD).m4v", 1, 2)]
        [TestCase(@"C:\Test\Series\Season 2 - Total Series Action\01. Total Series Action - Episode 1 - Monster Cash.mkv", 2, 1)]
        [TestCase(@"C:\Test\Series\Season 2\01. Total Series Action - Episode 1 - Monster Cash.mkv", 2, 1)]
        [TestCase(@"C:\Test\Series\Season 1\02.04.24 - S01E01 - The Rabbit Hole", 1, 1)]
        [TestCase(@"C:\Test\Series\Season 1\8 Series Rules - S01E01 - Pilot", 1, 1)]

        // [TestCase(@"C:\series.state.S02E04.720p.WEB-DL.DD5.1.H.264\73696S02-04.mkv", 2, 4)] //Gets treated as S01E04 (because it gets parsed as anime); 2020-01 broken test case: Expected result.EpisodeNumbers to contain 1 item(s), but found 0
        public void should_parse_from_path(string path, int season, int episode)
        {
            var result = Parser.Parser.ParsePath(path.AsOsAgnostic());

            result.EpisodeNumbers.Should().HaveCount(1);
            result.SeasonNumber.Should().Be(season);
            result.EpisodeNumbers[0].Should().Be(episode);
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeFalse();

            ExceptionVerification.IgnoreWarns();
        }

        // Audiobook ebook editions: .epub absolute numbers are offset by
        // 1,000,000 (Parser.EbookAbsoluteEpisodeOffset) to route them to
        // their Season 2 episodes instead of stealing the audiobook (.m4b)
        // slots -- file matching is absolute-number-based and
        // season-agnostic. Same filename shapes, different extension.
        [TestCase(@"C:\Audiobooks\Dungeon Crawler Carl\Dungeon Crawler Carl - Book 001 - The Apocalypse Will be Televised.epub", new[] { 1000001 })]
        [TestCase(@"C:\Audiobooks\The Beginning After the End\The Beginning After The End - Book 001, 002 - Early Years, New Heights.epub", new[] { 1000001, 1000002 })]
        [TestCase(@"C:\Audiobooks\The Beginning After the End\The Beginning After The End - Book 008.5 - Amongst the Fallen.epub", new[] { 1008500 })]

        // Folder-fallback: unnumbered epub inside a "Book N - Subtitle"
        // folder (real gap found live: DCC book 8 was the only one of 8 not
        // matched, because the spaced dash after the number defeated every
        // title-before-Book pattern).
        [TestCase(@"C:\Audiobooks\Dungeon Crawler Carl\Book 8 - A Parade of Horribles\A Parade of Horribles.epub", new[] { 1000008 })]
        public void should_offset_epub_absolute_numbers(string path, int[] absoluteEpisodes)
        {
            var result = Parser.Parser.ParsePath(path.AsOsAgnostic());

            result.AbsoluteEpisodeNumbers.Should().BeEquivalentTo(absoluteEpisodes);

            ExceptionVerification.IgnoreWarns();
        }

        // Same filename as an epub case above but audio extension: absolute
        // numbers must stay UN-offset (Season 1 audiobook slots).
        [TestCase(@"C:\Audiobooks\Dungeon Crawler Carl\Dungeon Crawler Carl - Book 001 - The Apocalypse Will be Televised.m4b", new[] { 1 })]
        public void should_not_offset_audio_absolute_numbers(string path, int[] absoluteEpisodes)
        {
            var result = Parser.Parser.ParsePath(path.AsOsAgnostic());

            result.AbsoluteEpisodeNumbers.Should().BeEquivalentTo(absoluteEpisodes);

            ExceptionVerification.IgnoreWarns();
        }

        [TestCase("01-03\\The Series Title (2010) - 1x01-02-03 - Episode Title HDTV-720p Proper", "The Series Title (2010)", 1, new[] { 1, 2, 3 })]
        [TestCase("Season 2\\E05-06 - Episode Title HDTV-720p Proper", "", 2, new[] { 5, 6 })]
        public void should_parse_multi_episode_from_path(string path, string title, int season, int[] episodes)
        {
            var result = Parser.Parser.ParsePath(path.AsOsAgnostic());

            result.SeriesTitle.Should().Be(title);
            result.EpisodeNumbers.Should().HaveCount(episodes.Length);
            result.SeasonNumber.Should().Be(season);
            result.EpisodeNumbers.Should().BeEquivalentTo(episodes);
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
            result.FullSeason.Should().BeFalse();

            ExceptionVerification.IgnoreWarns();
        }
    }
}
