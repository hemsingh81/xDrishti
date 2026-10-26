import { Alert, Box, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { MetricTile, VerdictChip, formatCount, formatSigned, type Verdict } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { paperCompareQuery, type BacktestVsPaper, type CompareVerdict } from '../api/compareQueries';
import { toMetric } from '../lib/metric';
import { BasisNote } from './BasisNote';
import { CompareCells } from './CompareCells';
import { CompareFan } from './CompareFan';

const verdicts = new Set<string>(['met', 'not_met', 'not_evaluable']);
const asVerdict = (value: string): Verdict => (verdicts.has(value) ? (value as Verdict) : 'not_evaluable');

const bandWords: Record<string, string> = {
  below: 'below the backtest band',
  within: 'inside the backtest band',
  above: 'above the backtest band (luck or a leak is possible)',
};
const fallWords: Record<string, string> = {
  within: 'within the backtest 90th percentile',
  above: 'worse than the backtest 90th percentile',
};

/** The Backtest vs paper tab (R14): the paper line over the backtest fan, the band, the largest fall and the two exit statistics. Read only. */
export function CompareTab() {
  const compare = useQuery(paperCompareQuery);
  if (compare.isPending) return <LoadingState label="Comparing paper with the backtest…" />;
  if (compare.isError) return <ErrorState error={compare.error} />;
  return <Body view={compare.data} />;
}

function Body({ view }: { view: BacktestVsPaper }) {
  const { counts } = view;
  return (
    <Stack spacing={3}>
      <BasisNote basis={view.evidence.statement} limits={view.limits} />
      {view.earlyWarning && <Alert severity="warning">{view.earlyWarning}</Alert>}
      <Typography>
        {formatCount(view.n)} counted trades (R basis: {view.basis}). Set apart: {formatCount(counts.reserve)} reserve,{' '}
        {formatCount(counts.straddle)} straddle, {formatCount(counts.open)} open, {formatCount(counts.otherEpochs)} in
        other epochs.
        {view.chargeSheet.id &&
          ` Charge sheet ${view.chargeSheet.id}: ${view.chargeSheet.confirmedOn ? `confirmed on ${view.chargeSheet.confirmedOn}` : 'not confirmed'}.`}
      </Typography>
      <Box
        sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(3, 1fr)' } }}
      >
        <MetricTile
          label="Paper mean R"
          state={toMetric(view.paper.mean)}
          format={formatSigned}
          hint={<Interval view={view} />}
        />
        <MetricTile
          label="Backtest band, 5th to 95th percentile"
          state={toMetric(view.band.p05)}
          format={formatSigned}
          hint={<BandHint view={view} />}
        />
        <MetricTile
          label="Largest fall (paper), R"
          state={toMetric(view.paper.maxDrawdownR)}
          format={formatSigned}
          hint={<FallHint view={view} />}
        />
      </Box>
      <Box component="section" aria-labelledby="paper-criteria-heading">
        <Typography variant="h2" id="paper-criteria-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
          The two exit statistics
        </Typography>
        <Criterion name="Positive expectancy after costs, within the band" verdict={view.criteria.expectancy} />
        <Criterion name="Largest fall within the 90th percentile" verdict={view.criteria.drawdown} />
      </Box>
      <Box component="section" aria-labelledby="paper-fan-heading">
        <Typography variant="h2" id="paper-fan-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
          Paper line over the backtest fan
        </Typography>
        <CompareFan fan={view.fan} />
      </Box>
      <Box component="section" aria-labelledby="paper-cells-heading">
        <Typography variant="h2" id="paper-cells-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
          Cells
        </Typography>
        <CompareCells cells={view.cells} />
      </Box>
      <Typography>
        {view.sensitivityAllProposals.label}: n {formatCount(view.sensitivityAllProposals.n)},{' '}
        {view.sensitivityAllProposals.mean.number === null
          ? `${view.sensitivityAllProposals.mean.state.replace('_', ' ')}${view.sensitivityAllProposals.mean.reason ? `, ${view.sensitivityAllProposals.mean.reason}` : ''}`
          : `mean ${formatSigned(view.sensitivityAllProposals.mean.number)}R`}
        . Shown beside; it is never used by the statistics above.
      </Typography>
    </Stack>
  );
}

function Criterion({ name, verdict }: { name: string; verdict: CompareVerdict }) {
  return (
    <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'baseline', flexWrap: 'wrap', mb: 1 }}>
      <VerdictChip verdict={asVerdict(verdict.verdict)} />
      <Typography component="span" sx={{ fontWeight: 600 }}>
        {name}:
      </Typography>
      <Typography component="span">{verdict.label}</Typography>
    </Box>
  );
}

const figure = (m: { number: number | null; state: string; reason: string | null }) =>
  m.number === null ? `${m.state.replace('_', ' ')}${m.reason ? ` (${m.reason})` : ''}` : `${formatSigned(m.number)}R`;

function Interval({ view }: { view: BacktestVsPaper }) {
  const { lo, hi } = view.paper.ci90;
  return <>90% interval: {lo.number === null || hi.number === null ? figure(lo) : `${figure(lo)} to ${figure(hi)}`}</>;
}

function BandHint({ view }: { view: BacktestVsPaper }) {
  const { band } = view;
  if (band.p05.number === null || band.p95.number === null || !band.position) {
    return <>Not a figure: {figure(band.p05)}</>;
  }
  return (
    <>
      {figure(band.p05)} to {figure(band.p95)}; paper is {bandWords[band.position] ?? band.position}
      {band.rank.number !== null && `, rank ${formatCount(band.rank.number)} of 100`}. {formatCount(band.draws)} draws,
      seed {band.seed}.
    </>
  );
}

function FallHint({ view }: { view: BacktestVsPaper }) {
  const { drawdown } = view;
  if (drawdown.p90.number === null || !drawdown.position) return <>Backtest reference: {figure(drawdown.p90)}</>;
  return (
    <>
      Backtest 90th percentile at this n: {figure(drawdown.p90)}; paper is{' '}
      {fallWords[drawdown.position] ?? drawdown.position}.
    </>
  );
}
