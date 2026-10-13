import { Box, Stack, Typography, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useId, useMemo } from 'react';
import type { HoldingGroups } from '@/shared/api';
import { formatCompact, formatInr } from '@/shared/lib/format';
import { CategoryChart } from '@/shared/ui/chart';
import { allocationDonut, exposureStack } from '../lib/allocation';
import type { GroupBy } from '../lib/filters';
import { groupNoun } from '../lib/groups';

const axisInr = (value: number) => `₹${formatCompact(value)}`;

/** One bar a row tall enough to read, plus the axis. */
const stackedHeight = (rows: number) => Math.max(200, rows * 34 + 56);

/** Why there is nothing to draw: no group at all (the filters leave nothing), or groups that have no price yet. */
function NothingToChart({ noGroups }: { noGroups: boolean }) {
  return (
    <Stack
      spacing={0.5}
      sx={{ alignItems: 'center', justifyContent: 'center', textAlign: 'center', minHeight: 200, px: 2 }}
    >
      <Typography sx={{ fontWeight: 600 }}>{noGroups ? 'No groups to chart' : 'Nothing to chart yet'}</Typography>
      <Typography variant="body2" color="text.secondary">
        {noGroups
          ? 'No group matches the filters, so there is nothing to chart.'
          : 'No group has a price yet, so there is no value to split.'}
      </Typography>
    </Stack>
  );
}

function ChartTitle({ id, children }: { id: string; children: string }) {
  return (
    <Typography id={id} variant="h3" component="h3" sx={{ mb: 1 }}>
      {children}
    </Typography>
  );
}

interface AllocationChartsProps {
  by: GroupBy;
  data: HoldingGroups | undefined;
  loading: boolean;
  /** The groups are the old ones while a new order or filter loads. */
  busy: boolean;
}

/**
 * The charts of the Charts card: the allocation donut of the current grouping (the eight largest groups and "Other") and,
 * for Account and Sector, the stacked bar of the other dimension (sectors by account, accounts by sector).
 */
export function AllocationCharts({ by, data, loading, busy }: AllocationChartsProps) {
  const phone = useMediaQuery(useTheme().breakpoints.down('sm'));
  const donutId = useId();
  const stackId = useId();
  const donut = useMemo(() => allocationDonut(data?.groups ?? []), [data]);
  const stacked = useMemo(() => (data ? exposureStack(data.groups) : null), [data]);
  const noGroups = data?.groups.length === 0;
  const noun = groupNoun[by].toLowerCase();
  const stackLabel = by === 'sector' ? 'Sector exposure by account' : 'Sector mix of each account';
  // The bar chart is for Account and Sector only, and only when the groups came with their parts and have a value.
  const showStack = (by === 'account' || by === 'sector') && (loading || stacked !== null);

  return (
    <Box
      sx={{
        display: 'grid',
        gap: 3,
        gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: showStack ? 'repeat(2, minmax(0, 1fr))' : 'minmax(0, 1fr)' },
      }}
    >
      <Box component="section" aria-labelledby={donutId} sx={{ minWidth: 0 }}>
        <ChartTitle id={donutId}>{`Allocation by ${noun}`}</ChartTitle>
        <CategoryChart
          label={`Allocation by ${noun}`}
          data={donut}
          valueFormatter={formatInr}
          height={phone ? 240 : 280}
          loading={loading}
          busy={busy}
          empty={<NothingToChart noGroups={noGroups} />}
          deferUntilVisible
        />
        {by === 'basket' && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
            Baskets overlap, so a holding counts in each of its baskets: shares are of the baskets&rsquo; combined
            value.
          </Typography>
        )}
      </Box>
      {showStack && (
        <Box component="section" aria-labelledby={stackId} sx={{ minWidth: 0 }}>
          <ChartTitle id={stackId}>{stackLabel}</ChartTitle>
          <CategoryChart
            label={stackLabel}
            data={stacked ?? { kind: 'stacked', categories: [], stacks: [] }}
            valueFormatter={formatInr}
            axisFormatter={axisInr}
            height={stackedHeight(stacked?.categories.length ?? 4)}
            loading={loading}
            busy={busy}
            empty={<NothingToChart noGroups={noGroups} />}
            deferUntilVisible
          />
        </Box>
      )}
    </Box>
  );
}
