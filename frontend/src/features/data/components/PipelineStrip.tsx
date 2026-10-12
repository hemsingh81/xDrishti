import { Box, Popover } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { useState } from 'react';
import CloudIcon from '@mui/icons-material/CloudDownloadOutlined';
import FolderIcon from '@mui/icons-material/FolderOpenOutlined';
import LayersIcon from '@mui/icons-material/LayersOutlined';
import type { DataOverview, StartDataJobInput } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { currentPhase, phaseJob, phaseTitles, type PhaseKey } from '../lib/phases';
import { aggregatesSummary, csvSummary, dhanSummary, stepUnits } from '../lib/steps';
import { AggregatesDetails } from './AggregatesDetails';
import { CsvDetails } from './CsvDetails';
import { CsvReviewDialog } from './CsvReviewDialog';
import { DhanDetails } from './DhanDetails';
import { StepChip } from './StepChip';

const doing: Readonly<Record<PhaseKey, string>> = {
  csv: 'loading the CSV files',
  dhan: 'fetching from the Dhan Data API',
  aggregate: 'building the aggregates',
};

function liveLine(overview: DataOverview): string {
  const job = overview.runningJob;
  if (!job) return '';
  const phase = job.currentPhase as PhaseKey | null;
  const what = phase && phase in doing ? doing[phase] : 'starting';
  return `Update in progress, ${what}. Started ${formatIstDateTime(job.startedAt)}.`;
}

/** What the CSV chip flags: files waiting and what the last job noted (an undo names the other files it took bars from). */
function csvFlags(pending: number, warnings: number): string[] {
  return [
    ...(pending > 0 ? [`${formatNumber(pending)} pending`] : []),
    ...(warnings > 0 ? [`${formatNumber(warnings)} ${warnings === 1 ? 'warning' : 'warnings'}`] : []),
  ];
}

interface PipelineStripProps {
  overview: DataOverview;
  /** A data job is running or was just started. */
  busy: boolean;
  onStart: (body: StartDataJobInput) => void;
  /** Opens the Gaps tab. */
  onViewGaps: () => void;
}

/**
 * The three steps (CSV files, Dhan Data API, aggregates) as one row of compact chips with their key figures, outcome and
 * last run; a step that runs shows its progress bar. Each opens a popover with the full facts and the actions (Review
 * files, View gaps). On a phone the chips stack as a list.
 */
export function PipelineStrip({ overview, busy, onStart, onViewGaps }: PipelineStripProps) {
  const [open, setOpen] = useState<{ key: PhaseKey; anchor: HTMLElement } | null>(null);
  const [reviewing, setReviewing] = useState(false);
  const { csv, dhan, aggregates } = overview;
  const phases = {
    csv: currentPhase(overview, 'csv'),
    dhan: currentPhase(overview, 'dhan'),
    aggregate: currentPhase(overview, 'aggregate'),
  };
  const csvWarnings = phaseJob(overview, 'csv')?.warnings ?? [];
  const close = () => {
    setOpen(null);
  };
  const opener = (key: PhaseKey) => (anchor: HTMLElement) => {
    setOpen({ key, anchor });
  };

  return (
    <Box component="section" aria-label="Pipeline" sx={{ mb: 1.5, flexShrink: 0 }}>
      {/* Always on the page, so a screen reader announces the step changes of a running update. */}
      <Box role="status" aria-label="Update progress" sx={visuallyHidden}>
        {liveLine(overview)}
      </Box>
      <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center', flexDirection: { xs: 'column', md: 'row' } }}>
        <Box
          component="ol"
          aria-label="Pipeline steps"
          sx={{
            flex: 1,
            width: '100%',
            m: 0,
            p: 0,
            display: 'grid',
            gap: 1.5,
            gridTemplateColumns: { xs: '1fr', md: 'repeat(3, minmax(0, 1fr))' },
          }}
        >
          <StepChip
            phaseKey="csv"
            number={1}
            title={phaseTitles.csv}
            icon={FolderIcon}
            hue="data"
            phase={phases.csv}
            unit={stepUnits.csv}
            summary={csvSummary(csv)}
            flags={csvFlags(csv.filesPending, csvWarnings.length)}
            expanded={open?.key === 'csv'}
            onOpen={opener('csv')}
          />
          <StepChip
            phaseKey="dhan"
            number={2}
            title={phaseTitles.dhan}
            icon={CloudIcon}
            hue="sky"
            phase={phases.dhan}
            unit={stepUnits.dhan}
            summary={dhanSummary(dhan, overview.gaps)}
            expanded={open?.key === 'dhan'}
            onOpen={opener('dhan')}
          />
          <StepChip
            phaseKey="aggregate"
            number={3}
            title={phaseTitles.aggregate}
            icon={LayersIcon}
            hue="violet"
            phase={phases.aggregate}
            unit={stepUnits.aggregate}
            summary={aggregatesSummary(aggregates)}
            flags={aggregates.instrumentsPending > 0 ? [`${formatNumber(aggregates.instrumentsPending)} waiting`] : []}
            expanded={open?.key === 'aggregate'}
            onOpen={opener('aggregate')}
          />
        </Box>
      </Box>
      <Popover
        open={open !== null}
        anchorEl={open?.anchor}
        onClose={close}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        slotProps={{
          paper: {
            role: 'dialog',
            'aria-label': open ? `${phaseTitles[open.key]} details` : undefined,
            sx: { p: 2, width: 460, maxWidth: 'calc(100vw - 32px)', maxHeight: 'calc(100dvh - 120px)' },
          },
        }}
      >
        {open?.key === 'csv' && (
          <CsvDetails
            csv={csv}
            phase={phases.csv}
            warnings={csvWarnings}
            onReview={() => {
              close();
              setReviewing(true);
            }}
          />
        )}
        {open?.key === 'dhan' && (
          <DhanDetails
            dhan={dhan}
            phase={phases.dhan}
            gaps={overview.gaps}
            onViewGaps={() => {
              close();
              onViewGaps();
            }}
          />
        )}
        {open?.key === 'aggregate' && <AggregatesDetails aggregates={aggregates} phase={phases.aggregate} />}
      </Popover>
      {reviewing && (
        <CsvReviewDialog
          busy={busy}
          onStart={onStart}
          onClose={() => {
            setReviewing(false);
          }}
        />
      )}
    </Box>
  );
}
