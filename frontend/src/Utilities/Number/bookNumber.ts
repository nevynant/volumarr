// Volumarr book-number encodings, decoded here for display/sorting. Both
// are deliberately self-identifying (no real series ever reaches these
// ranges), which is what lets the UI decode with no extra data:
//
// - Fractional books (novellas, "Book 8.5") slot at floor*1000+500 -> 8500.
//   Backend: SkyHookProxy.ResolveBookPositions (metadata) and Parser.cs's
//   audiobookfractionalbook marker (file parsing).
// - Ebook editions (Season 2) have their ABSOLUTE numbers offset by
//   1,000,000 so .epub files can't steal audiobook slots during matching.
//   Backend: Parser.EbookAbsoluteEpisodeOffset / SkyHookProxy.MapEbookEpisode.
//   (Season 2 EpisodeNumbers are NOT offset -- only absolutes are.)

const EBOOK_ABSOLUTE_OFFSET = 1000000;

export function decodeBookNumber(episodeNumber: number): number {
  let n = episodeNumber;

  if (n >= EBOOK_ABSOLUTE_OFFSET) {
    n -= EBOOK_ABSOLUTE_OFFSET;
  }

  if (n >= 500 && n % 1000 === 500) {
    return Math.floor(n / 1000) + 0.5;
  }

  return n;
}

export function formatBookNumber(episodeNumber: number): string {
  return String(decodeBookNumber(episodeNumber));
}
