import { Box, Typography } from '@mui/material';
import { DataTable, formatCount, formatRate, NonValue, NOT_AVAILABLE } from '@/shared/analytics';
import type { LoopArm, LoopReport } from '../api/confidenceQueries';
import { armLabel, armVerdictLabel } from '../lib/confidenceText';
import { figure, formatFineSigned as formatSigned } from '../lib/figure';
import { Panel } from './Panel';

/** One of the two comparisons the report names; `arm` is null when the server did not compute it. */
interface Comparison {
  name: string;
  arm: LoopArm | null;
}

/**
 * The two comparisons. A comparison the server did not compute keeps its row and reads "Not available (not computed)", so
 * it never disappears. Missing figures are `unavailable`; the interval is two figures, each in its honest state.
 */
function Comparisons({ rows }: { rows: readonly Comparison[] }) {
  return (
    <DataTable
      label="Arm comparisons"
      rows={rows}
      rowKey={(row) => row.name}
      columns={[
        { kind: 'text', id: 'name', header: 'Comparison', render: (row) => armLabel(row.name) },
        {
          kind: 'text',
          id: 'verdict',
          header: 'Verdict',
          render: (row) => (
            <strong>{row.arm ? armVerdictLabel(row.arm.verdict) : 'Not available (not computed)'}</strong>
          ),
        },
        {
          kind: 'text',
          id: 'candidates',
          header: 'Candidates',
          render: (row) => (row.arm ? formatCount(row.arm.candidates) : NOT_AVAILABLE),
          align: 'right',
        },
        {
          kind: 'text',
          id: 'days',
          header: 'Days',
          render: (row) => (row.arm ? formatCount(row.arm.days) : NOT_AVAILABLE),
          align: 'right',
        },
        {
          kind: 'metric',
          id: 'diff',
          header: 'Mean difference per candidate',
          metric: (row) => figure(row.arm?.meanDifference, 'not computed'),
          format: formatSigned,
        },
        {
          kind: 'metric',
          id: 'low',
          header: 'Interval, low',
          metric: (row) => figure(row.arm?.low, 'not computed'),
          format: formatSigned,
        },
        {
          kind: 'metric',
          id: 'high',
          header: 'Interval, high',
          metric: (row) => figure(row.arm?.high, 'not computed'),
          format: formatSigned,
        },
        {
          kind: 'metric',
          id: 'better',
          header: 'P(first arm better)',
          metric: (row) => figure(row.arm?.probabilityBetter, 'not computed'),
          format: formatRate,
        },
        {
          kind: 'metric',
          id: 'worse',
          header: 'P(first arm worse)',
          metric: (row) => figure(row.arm?.probabilityWorse, 'not computed'),
          format: formatRate,
        },
        { kind: 'text', id: 'text', header: 'In words', render: (row) => row.arm?.text ?? '' },
      ]}
    />
  );
}

/** The three arms on the same candidates and the two paired comparisons. */
export function LoopArmsPanel({ arms }: { arms: LoopReport['arms'] }) {
  const comparisons: Comparison[] = [
    { name: 'retrained_vs_frozen', arm: arms.retrainedVsFrozen },
    { name: 'retrained_vs_no_ml', arm: arms.retrainedVsNoMl },
  ];
  return (
    <Panel title="The three arms">
      {arms.state !== 'available' ? (
        <NonValue state={{ kind: 'unavailable', reason: arms.reason ?? 'no reason given' }} />
      ) : (
        <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
          <Typography variant="body2">
            The same {formatCount(arms.candidates)} candidates scored by each arm.
          </Typography>
          <DataTable
            label="Mean value per candidate by arm"
            rows={[
              { id: 'frozen', name: 'A: frozen model', mean: arms.frozenMean },
              { id: 'retrained', name: 'B: retrained model (the champion now)', mean: arms.retrainedMean },
              { id: 'no-ml', name: 'C: no model (take every candidate)', mean: arms.noMlMean },
            ]}
            rowKey={(row) => row.id}
            columns={[
              { kind: 'text', id: 'name', header: 'Arm', render: (row) => row.name },
              {
                kind: 'metric',
                id: 'mean',
                header: 'Mean per candidate',
                metric: (row) => figure(row.mean, 'not computed'),
                format: formatSigned,
              },
            ]}
          />
          {comparisons.every((row) => row.arm === null) ? (
            <NonValue state={{ kind: 'unavailable', reason: 'no comparison was computed' }} />
          ) : (
            <Comparisons rows={comparisons} />
          )}
        </Box>
      )}
    </Panel>
  );
}
