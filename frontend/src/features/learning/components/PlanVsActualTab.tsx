import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { EvidenceBlock, formatCount, MetricTile, type MetricState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { planVsActualQuery } from '../api/learningQueries';
import { useRange } from '../lib/useRange';
import { MissedAndNotDecomposed } from './MissedAndNotDecomposed';
import { Panel } from './Panel';
import { RangeFilter } from './RangeFilter';
import { StrategyActualTable } from './StrategyActualTable';
import { WaterfallList } from './WaterfallList';

/** Plan versus actual: where the edge goes between the plan and the fills. The server computes every figure. */
export function PlanVsActualTab() {
  const { range, setRange } = useRange();
  const query = useQuery(planVsActualQuery(range));
  const filter = <RangeFilter range={range} onChange={setRange} />;
  if (query.isPending) return <LoadingState label="Loading plan versus actual…" />;
  if (query.isError)
    return (
      <Panel title="Plan versus actual" actions={filter}>
        <ErrorState error={query.error} />
      </Panel>
    );
  const v = query.data;
  const c = v.counts;
  const count = (value: number): MetricState =>
    v.plans === 0 ? { kind: 'empty', reason: 'No plans in the range' } : { kind: 'value', value };
  const approvals = (kind: string) => count(v.approvals.find((a) => a.kind === kind)?.count ?? 0);
  const tiles: { label: string; state: MetricState; hint?: string }[] = [
    { label: 'Approved ticket lines', state: count(c.approvedLines) },
    { label: 'Without fills', state: count(c.withoutFills) },
    { label: 'Graded on fills', state: count(c.gradedOnFills) },
    {
      label: 'Left out of the chain',
      state: count(c.uncertain + c.noPlanLevels + c.notFinished),
      hint: `uncertain ${formatCount(c.uncertain)}, no plan levels ${formatCount(c.noPlanLevels)}, not finished ${formatCount(c.notFinished)}`,
    },
    { label: 'Approved by the owner', state: approvals('owner') },
    { label: 'Approved automatically', state: approvals('auto') },
  ];
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Panel title="From plan to actual" actions={filter}>
        {c.gradedOnFills === 0 && (
          <Typography sx={{ mb: 1 }}>
            No ticket has been graded on fills yet; fills arrive with paper and live trading, P10/P11.
          </Typography>
        )}
        <WaterfallList chain={v.chain} />
        <Box sx={{ mt: 1.5 }}>
          <EvidenceBlock
            label="Plan versus actual evidence"
            n={v.evidence.n}
            range={
              v.evidence.firstDate && v.evidence.lastDate
                ? { from: v.evidence.firstDate, to: v.evidence.lastDate }
                : null
            }
            basis="unknown"
            basisText={v.evidence.basis}
            provisional={v.evidence.provisional}
            limits={v.evidence.limits}
          />
        </Box>
      </Panel>
      <Panel title="Counts">
        <Box
          sx={{
            display: 'grid',
            gap: 1.5,
            gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(3, minmax(0, 1fr))' },
          }}
        >
          {tiles.map((tile) => (
            <MetricTile key={tile.label} label={tile.label} state={tile.state} format={formatCount} hint={tile.hint} />
          ))}
        </Box>
      </Panel>
      <Panel title="By strategy">
        <StrategyActualTable rows={v.byStrategy} />
      </Panel>
      <Panel title="Outside the chain">
        <MissedAndNotDecomposed view={v} />
      </Panel>
    </Box>
  );
}
