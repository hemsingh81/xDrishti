import { Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue } from '@/shared/analytics';
import { formatIstDate } from '@/shared/lib/format';
import { comparisonsQuery, type ComparisonItem } from '../api/reportQueries';
import { sameRunSet } from '../lib/runSet';
import { useReportFilter } from '../hooks/useReportFilter';
import { QueryPanel } from './Panel';

const keysOf = (c: ComparisonItem) => c.members.map((m) => m.runKey);

/** R5: the stored comparisons of exits on identical entries. Choosing one puts its member runs in the address (`runs=`); nothing is written. */
export function ComparisonPicker() {
  const { filter, set } = useReportFilter();
  const result = useQuery(comparisonsQuery);
  return (
    <QueryPanel title="Stored exit comparisons" query={result}>
      {(view) =>
        view.comparisons.length === 0 ? (
          <NonValue
            state={{ kind: 'empty', reason: 'No exit comparison is stored. Make one with xd simulate --exit.' }}
          />
        ) : (
          <Box sx={{ display: 'grid', gap: 1 }}>
            <Box component="ul" sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 1 }}>
              {view.comparisons.map((c) => {
                const inUse = sameRunSet(keysOf(c), filter.runs);
                const exits = c.members.map((m) => m.candidateId).join(' | ');
                return (
                  <Box
                    component="li"
                    key={c.comparisonKey}
                    sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}
                  >
                    <Typography variant="body2" sx={{ flex: '1 1 16rem', minWidth: 0, overflowWrap: 'anywhere' }}>
                      {`${c.strategyId}: ${exits} (${c.unit}, stored ${formatIstDate(c.createdAt)})`}
                    </Typography>
                    <Button
                      size="small"
                      variant={inUse ? 'contained' : 'outlined'}
                      aria-pressed={inUse}
                      aria-label={`Use the runs of the ${c.strategyId} comparison of ${exits}`}
                      onClick={() => {
                        set({ runs: inUse ? null : keysOf(c).join(',') });
                      }}
                      sx={{ minHeight: 44 }}
                    >
                      {inUse ? 'In use' : 'Use these runs'}
                    </Button>
                  </Box>
                );
              })}
            </Box>
            {view.limits.map((line) => (
              <Typography key={line} variant="body2" color="text.secondary">
                {line}
              </Typography>
            ))}
          </Box>
        )
      }
    </QueryPanel>
  );
}
