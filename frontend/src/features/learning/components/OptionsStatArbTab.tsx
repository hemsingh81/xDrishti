import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { extensionsQuery } from '../api/extensionQueries';
import { p14Families } from '../api/p14Queries';
import { latestPerKey } from '../lib/extensions';
import { ExpressionRunsPanel } from './ExpressionRunsPanel';
import { ExtensionCard } from './ExtensionCard';
import { PairFormationsPanel } from './PairFormationsPanel';
import { Panel } from './Panel';

function Family({ family }: { family: string }) {
  const query = useQuery(extensionsQuery(family));
  const records = query.data ? latestPerKey(query.data.items) : [];
  return (
    <Panel title={family}>
      {query.isPending && <LoadingState label={`Loading ${family}`} />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && records.length === 0 && (
        <NonValue state={{ kind: 'empty', reason: 'none stored: no evaluation of this family yet' }} />
      )}
      {records.length > 0 && (
        <Box sx={{ display: 'grid', gap: 1.5 }}>
          {records.map((e) => (
            <ExtensionCard key={e.id} family={family} e={e} />
          ))}
        </Box>
      )}
    </Panel>
  );
}

/** Options and stat-arb: the stored verdict of each P14 family with its evidence and what is missing, the expression runs and the pair formations. Read only. */
export function OptionsStatArbTab() {
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Typography variant="body2" color="text.secondary">
        Advisory research records; nothing here places an order or changes a setting. A figure that is not available is
        shown as a word, never as a zero.
      </Typography>
      {p14Families.map((family) => (
        <Family key={family} family={family} />
      ))}
      <ExpressionRunsPanel />
      <PairFormationsPanel />
    </Box>
  );
}
