import BarChartIcon from '@mui/icons-material/BarChartOutlined';
import { Alert, Box, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { runsQuery, whatWorksQuery } from '../api/reportQueries';
import { useReportFilter } from '../hooks/useReportFilter';
import { queryString, toQuery, type ReportView } from '../lib/reportFilter';
import { BasketComparison } from './BasketComparison';
import { Catalogue } from './Catalogue';
import { DataUsedPanel } from './DataUsedPanel';
import { DistributionPanel } from './DistributionPanel';
import { EquityPanel } from './EquityPanel';
import { FilterBar } from './FilterBar';
import { GroupBars } from './GroupBars';
import { InsightsList } from './InsightsList';
import { KpiTiles } from './KpiTiles';
import { Panel } from './Panel';
import { PivotBuilder } from './PivotBuilder';
import { StrategyTimeframePanel } from './StrategyTimeframePanel';
import { WhatWorksCard } from './WhatWorksCard';
import { WhatWorksView } from './WhatWorksView';
import { formatR, formatRate } from '@/shared/analytics';

const panelTitles = [
  'Key figures',
  'Auto-insights',
  'Equity and drawdown',
  'Monthly total R',
  'Strategy by timeframe',
  'Expectancy by basket',
  'R distribution',
  'Win rate by weekday',
];
const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const dayIndex = (value: string | undefined) => {
  const at = weekdays.indexOf(value ?? '');
  return at < 0 ? weekdays.length : at;
};

/**
 * Reports (P8-08): what works, in which basket, on which timeframe, with which exit, and the Overview behind it. Read only:
 * the filters and the run picker change the address, nothing else. Every figure is in-sample and gross unless the server says
 * otherwise; "nothing has enough evidence yet" is a correct answer.
 */
export function ReportsPage() {
  const { view, filter, set } = useReportFilter();
  const runs = useQuery(runsQuery);
  const net = runs.data?.net;
  const netRefused = filter.basis === 'net' && net?.state !== 'value';
  const query = toQuery(filter, filter.basis === 'net' && !netRefused ? 'net' : 'gross');
  const hasRuns = (runs.data?.runs.length ?? 0) > 0;
  const answer = useQuery({ ...whatWorksQuery(query), enabled: hasRuns });
  const min = answer.data?.minTrades ?? null;

  // "Report updated" is announced once when the filters or the view change (not on the first render).
  const key = `${view}|${queryString(query)}`;
  const [seen, setSeen] = useState(key);
  const [updates, setUpdates] = useState(0);
  if (seen !== key) {
    setSeen(key);
    setUpdates((count) => count + 1);
  }

  const open = (next: ReportView) => {
    set({ view: next === 'overview' ? null : next });
  };

  const header = (
    <PageHeader
      title="Reports"
      icon={BarChartIcon}
      hue="reports"
      description="What the stored simulation runs show. In-sample, modelled fills, gross R unless stated: a description of the past, not a forecast."
    />
  );
  if (runs.isPending) {
    return (
      <>
        {header}
        <LoadingState label="Loading the stored runs…" />
      </>
    );
  }
  if (runs.isError) {
    return (
      <>
        {header}
        <ErrorState error={runs.error} />
      </>
    );
  }
  if (!hasRuns) {
    return (
      <>
        {header}
        <Alert severity="info" sx={{ mb: 2 }}>
          No stored simulation runs yet. Make one with <code>xd simulate</code>.
        </Alert>
        <Box sx={{ display: 'grid', gap: 2 }}>
          {panelTitles.map((title) => (
            <Panel key={title} title={title}>
              <NonValue state={{ kind: 'empty', reason: 'No stored simulation runs.' }} />
            </Panel>
          ))}
        </Box>
      </>
    );
  }

  const panel = { query, min };
  return (
    <>
      {header}
      <Box role="status" aria-live="polite" sx={visuallyHidden}>
        {/* A new node per change, so the same words are announced again for every change. */}
        {updates > 0 && <span key={updates}>Report updated</span>}
      </Box>
      <FilterBar runs={runs.data} filter={filter} set={set} />
      {netRefused && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {net?.reason ?? 'Net R is not available.'} The figures stay gross.
        </Alert>
      )}
      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '240px minmax(0, 1fr)' } }}>
        <Box sx={{ order: { xs: 2, md: 0 } }}>
          <Catalogue view={view} onOpen={open} />
        </Box>
        <Box sx={{ display: 'grid', gap: 2, minWidth: 0, alignContent: 'start' }}>
          {view === 'what-works' ? (
            <WhatWorksView query={query} />
          ) : view === 'baskets' ? (
            <BasketComparison {...panel} />
          ) : view === 'pivot' ? (
            <PivotBuilder {...panel} />
          ) : (
            <>
              <WhatWorksCard
                query={query}
                onOpen={() => {
                  open('what-works');
                }}
              />
              <KpiTiles {...panel} />
              <InsightsList {...panel} />
              <EquityPanel {...panel} />
              <Box
                sx={{
                  display: 'grid',
                  gap: 2,
                  gridTemplateColumns: { xs: 'minmax(0, 1fr)', xl: 'repeat(2, minmax(0, 1fr))' },
                }}
              >
                <GroupBars {...panel} title="Monthly total R" lens="month" figure={(g) => g.totalR} format={formatR} />
                <StrategyTimeframePanel {...panel} />
                <GroupBars
                  {...panel}
                  title="Expectancy by basket"
                  lens="basket"
                  figure={(g) => g.expectancyR}
                  format={formatR}
                />
                <DistributionPanel {...panel} />
                <GroupBars
                  {...panel}
                  title="Win rate by weekday"
                  lens="weekday"
                  figure={(g) => g.winRate}
                  format={formatRate}
                  diverging={false}
                  sort={(a, b) => dayIndex(a.parts[0]?.value ?? undefined) - dayIndex(b.parts[0]?.value ?? undefined)}
                />
              </Box>
            </>
          )}
          <DataUsedPanel runs={runs.data} query={query} filter={filter} set={set} />
          <Typography variant="body2" color="text.secondary">
            Every figure is in-sample on replayed history; looking at many cells and taking the best is a selection
            effect.
          </Typography>
        </Box>
      </Box>
    </>
  );
}
