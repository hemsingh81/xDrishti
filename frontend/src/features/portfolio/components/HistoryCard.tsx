import { Alert, Button, Card, CardContent, Stack, Typography, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import type { UseQueryResult } from '@tanstack/react-query';
import { useId, useMemo } from 'react';
import type { PortfolioHistory } from '@/shared/api';
import { formatCompact, formatInr } from '@/shared/lib/format';
import { TimeSeriesChart } from '@/shared/ui/chart';
import { problemText, scopeTooLarge } from '../lib/errors';
import { historySeries } from '../lib/historyView';
import type { HistoryRange } from '../lib/historyRange';
import { CoverageLine } from './CoverageLine';
import { RangeChips } from './RangeChips';

const axisInr = (value: number) => `₹${formatCompact(value)}`;

function EmptyHistory() {
  return (
    <Stack
      spacing={0.5}
      sx={{ alignItems: 'center', justifyContent: 'center', textAlign: 'center', minHeight: 200, px: 2 }}
    >
      <Typography sx={{ fontWeight: 600 }}>No history yet</Typography>
      <Typography variant="body2" color="text.secondary">
        The chart is built from stored daily prices. Track your holdings and run Update data to build them.
      </Typography>
    </Stack>
  );
}

interface HistoryCardProps {
  query: UseQueryResult<PortfolioHistory>;
  range: HistoryRange;
  onRangeChange: (range: HistoryRange) => void;
  /** Open lots on the import's default date (from the totals); undefined while the totals load. */
  defaultDateLots: number;
  scope: { account: string | null; bucket: string | null };
}

/** The History card under the totals: value, invested and the same cash flows in the benchmark, over a range. */
export function HistoryCard({ query, range, onRangeChange, defaultDateLots, scope }: HistoryCardProps) {
  const titleId = useId();
  const phone = useMediaQuery(useTheme().breakpoints.down('sm'));
  const history = query.data;
  const tooLarge = query.isError ? scopeTooLarge(query.error) : null;
  const series = useMemo(() => (history ? historySeries(history) : []), [history]);

  return (
    <Card component="section" aria-labelledby={titleId} sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent>
        <Stack
          direction="row"
          sx={{ mb: 1.5, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1 }}
        >
          <Typography id={titleId} variant="h2" component="h2">
            History
          </Typography>
          <RangeChips value={range} onChange={onRangeChange} />
        </Stack>
        {query.isError && (
          <Alert
            severity="error"
            sx={{ mb: 1.5 }}
            action={
              tooLarge === null && (
                <Button
                  color="inherit"
                  size="small"
                  onClick={() => {
                    void query.refetch();
                  }}
                >
                  Retry
                </Button>
              )
            }
          >
            {tooLarge ?? problemText(query.error, 'The history could not be loaded.')}
          </Alert>
        )}
        {!query.isError || history ? (
          <TimeSeriesChart
            label="Portfolio value history"
            series={series}
            valueFormatter={formatInr}
            axisFormatter={axisInr}
            height={phone ? 240 : 300}
            loading={query.isPending}
            busy={query.isPlaceholderData}
            empty={<EmptyHistory />}
            deferUntilVisible
          />
        ) : null}
        {history && (
          <CoverageLine
            history={history}
            defaultDateLots={defaultDateLots}
            scope={scope}
            stale={query.isPlaceholderData}
          />
        )}
      </CardContent>
    </Card>
  );
}
