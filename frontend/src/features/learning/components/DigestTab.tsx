import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue, useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { digestsQuery } from '../api/learningQueries';
import { DigestSections } from './DigestSections';
import { DigestWeekList } from './DigestWeekList';
import { Panel } from './Panel';

/** The weekly digest: the stored weeks, and the chosen week (`?week=2026-W43`, else the newest) as text. */
export function DigestTab() {
  const url = useUrlState();
  const query = useQuery(digestsQuery);
  const week = url.text('week') ?? query.data?.[0]?.isoWeek ?? null;
  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(0, 1fr) minmax(0, 2fr)' },
        gap: 2,
        alignItems: 'start',
      }}
    >
      <Panel title="Weeks">
        {query.isPending && <LoadingState label="Loading the digests…" />}
        {query.isError && <ErrorState error={query.error} />}
        {query.data?.length === 0 && (
          <NonValue
            state={{
              kind: 'empty',
              reason: 'The weekly service builds one each week once it is switched on (Services).',
            }}
          />
        )}
        {query.data && query.data.length > 0 && (
          <DigestWeekList
            digests={query.data}
            selected={week ?? ''}
            onSelect={(next) => {
              url.update({ week: next });
            }}
          />
        )}
      </Panel>
      {week !== null && (
        <Panel title={`Digest ${week}`}>
          <DigestSections week={week} />
        </Panel>
      )}
    </Box>
  );
}
