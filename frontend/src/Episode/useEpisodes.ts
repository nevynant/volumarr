import { useEffect, useMemo } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import clientSideFilterAndSort from 'Utilities/Filter/clientSideFilterAndSort';
import { decodeBookNumber } from 'Utilities/Number/bookNumber';
import Episode from './Episode';
import { useEpisodeOptions } from './episodeOptionsStore';
import { setEpisodeQueryKey } from './useEpisode';

const DEFAULT_EPISODES: Episode[] = [];

interface SeriesEpisodes {
  seriesId: number;
}

interface SeasonEpisodes {
  seriesId: number | undefined;
  seasonNumber: number | undefined;
  isSelection: boolean;
}

interface EpisodeIds {
  episodeIds: number[];
}

interface EpisodeFileId {
  episodeFileId: number;
}

export type EpisodeFilter =
  | SeriesEpisodes
  | SeasonEpisodes
  | EpisodeIds
  | EpisodeFileId;

const useEpisodes = (params: EpisodeFilter) => {
  const setQueryKey = !('isSelection' in params);

  const { isPlaceholderData, queryKey, ...result } = useApiQuery<Episode[]>({
    path: '/episode',
    queryParams:
      'isSelection' in params
        ? {
            seriesId: params.seriesId,
            seasonNumber: params.seasonNumber,
          }
        : { ...params },
    queryOptions: {
      enabled:
        ('seriesId' in params && params.seriesId !== undefined) ||
        ('episodeIds' in params && params.episodeIds?.length > 0) ||
        ('episodeFileId' in params && params.episodeFileId !== undefined),
    },
  });

  useEffect(() => {
    if (setQueryKey && !isPlaceholderData) {
      setEpisodeQueryKey('episodes', queryKey);
    }
  }, [setQueryKey, isPlaceholderData, queryKey]);

  return {
    ...result,
    queryKey,
    data: result.data ?? DEFAULT_EPISODES,
  };
};

export default useEpisodes;

export const useSeasonEpisodes = (seriesId: number, seasonNumber: number) => {
  const { data, ...result } = useEpisodes({ seriesId });
  const { sortKey, sortDirection } = useEpisodeOptions();

  const seasonEpisodes = useMemo(() => {
    const { data: seasonEpisodes } = clientSideFilterAndSort(
      data.filter((episode) => episode.seasonNumber === seasonNumber),
      {
        sortKey,
        sortDirection,
        // Fractional books are stored as floor*1000+500 (see
        // Utilities/Number/bookNumber) -- sort on the decoded value so
        // book 8.5 lands between 8 and 9 instead of after book 8500.
        sortPredicates: {
          episodeNumber: (episode: Episode) =>
            decodeBookNumber(episode.episodeNumber),
        },
      }
    );

    return seasonEpisodes;
  }, [data, seasonNumber, sortKey, sortDirection]);

  return {
    ...result,
    data: seasonEpisodes,
  };
};
