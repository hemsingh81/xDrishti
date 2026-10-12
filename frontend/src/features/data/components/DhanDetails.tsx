import CloudIcon from '@mui/icons-material/CloudDownloadOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Typography } from '@mui/material';
import type { DhanOverview, DhanPhase, GapsOverview } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { runGapsText } from '../lib/gaps';
import { dhanResult } from '../lib/summaries';
import { isActive } from '../lib/phases';
import { Collapsible } from './Collapsible';
import { GapsSummary } from './GapsSummary';
import { PhaseNotice } from './PhaseNotice';
import { StepDetails } from './StepDetails';

interface DhanDetailsProps {
  dhan: DhanOverview;
  phase: DhanPhase | null;
  gaps: GapsOverview;
  /** Opens the Gaps tab (the popover closes first). */
  onViewGaps: () => void;
}

function Result({ phase }: { phase: DhanPhase }) {
  if (phase.status === 'failed' || phase.status === 'skipped') {
    return <PhaseNotice status={phase.status} message={phase.message} code={phase.code} />;
  }
  return (
    <>
      <Typography variant="body2">{dhanResult(phase)}</Typography>
      {phase.overlapMismatched > 0 && (
        <Typography variant="body2" sx={{ display: 'flex', gap: 0.75, alignItems: 'center', color: 'warning.main' }}>
          <WarningIcon fontSize="small" aria-hidden />
          {formatNumber(phase.overlapMismatched)} of {formatNumber(phase.overlapCompared)} overlapping bars differ from
          the stored ones
        </Typography>
      )}
      {runGapsText(phase) && (
        <Typography variant="body2" color="text.secondary">
          Gaps in this run: {runGapsText(phase)}
        </Typography>
      )}
      {phase.instrumentsFailed > 0 && (
        <Typography variant="body2" color="text.secondary">
          {formatNumber(phase.instrumentsFailed)} instruments could not be fetched
        </Typography>
      )}
      <Collapsible label={`${formatNumber(phase.errors.length)} errors`} items={phase.errors} />
    </>
  );
}

/** Step 2: the last successful fetch from Dhan's Data API, its gaps and, when it cannot run now, why. */
export function DhanDetails({ dhan, phase, gaps, onViewGaps }: DhanDetailsProps) {
  return (
    <StepDetails number={2} title="Dhan Data API" icon={CloudIcon} hue="sky" phase={phase}>
      {dhan.lastFetchAt ? (
        <Typography sx={{ fontWeight: 600 }}>
          Last fetch <time dateTime={dhan.lastFetchAt}>{formatIstDateTime(dhan.lastFetchAt)}</time>
        </Typography>
      ) : (
        <Typography sx={{ fontWeight: 600 }}>Nothing fetched yet</Typography>
      )}
      {dhan.lastFetchBars !== null && (
        <Typography variant="body2" color="text.secondary">
          {formatNumber(dhan.lastFetchBars)} bars added
        </Typography>
      )}
      {!dhan.canFetch && dhan.reason && (
        <Typography variant="body2" color="text.secondary">
          Cannot fetch now: {dhan.reason}
        </Typography>
      )}
      {phase && !isActive(phase.status) && <Result phase={phase} />}
      <GapsSummary gaps={gaps} onView={onViewGaps} />
    </StepDetails>
  );
}
