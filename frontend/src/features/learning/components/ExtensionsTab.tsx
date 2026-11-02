import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { extensionFamilies, extensionsQuery, type ExtensionFamily } from '../api/extensionQueries';
import { familyTitles, latestPerKey } from '../lib/extensions';
import { ExtensionCard } from './ExtensionCard';
import { Panel } from './Panel';

function Family({ family }: { family: ExtensionFamily }) {
  const query = useQuery(extensionsQuery(family));
  const records = query.data ? latestPerKey(query.data.items) : [];
  const holdOutRead = records.some((r) => r.holdOutRead);
  return (
    <Panel title={familyTitles[family]}>
      {query.isPending && <LoadingState label={`Loading ${family}`} />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && records.length === 0 && (
        <NonValue state={{ kind: 'empty', reason: 'no evaluation stored yet (run xd learn extension-eval)' }} />
      )}
      {records.length > 0 && (
        <Box sx={{ display: 'grid', gap: 1.5 }}>
          {records.map((e) => (
            <ExtensionCard key={e.id} family={family} e={e} />
          ))}
          {!holdOutRead && (
            <Typography variant="body2" color="text.secondary">
              The hold-out has not been read for this family yet.
            </Typography>
          )}
        </Box>
      )}
    </Panel>
  );
}

/** Fundamentals and events: the stored verdict of each family with its evidence and what is missing. Read only. */
export function ExtensionsTab() {
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Typography variant="body2" color="text.secondary">
        Advisory research records. A development KEEP changes nothing; only a hold-out-confirmed KEEP for the filter,
        with approval set to auto, can turn the filter on. A number that is not available is shown as a word, never as a
        zero.
      </Typography>
      {extensionFamilies.map((family) => (
        <Family key={family} family={family} />
      ))}
    </Box>
  );
}
