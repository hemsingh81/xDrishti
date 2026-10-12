import FolderIcon from '@mui/icons-material/FolderOpenOutlined';
import RuleIcon from '@mui/icons-material/FactCheckOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Alert, Button, Typography } from '@mui/material';
import type { CsvOverview, CsvPhase } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { csvResult, repairLine } from '../lib/summaries';
import { isActive } from '../lib/phases';
import { Collapsible } from './Collapsible';
import { JobWarnings } from './JobWarnings';
import { PhaseNotice } from './PhaseNotice';
import { StepDetails } from './StepDetails';

interface CsvDetailsProps {
  csv: CsvOverview;
  /** The running job's phase, else the latest finished one. */
  phase: CsvPhase | null;
  /** The warnings of the job that ran the step (an undo says which other files' bars it removed). */
  warnings: readonly string[];
  /** Opens the review dialog (the popover closes first). */
  onReview: () => void;
}

function Result({ phase }: { phase: CsvPhase }) {
  if (phase.status === 'failed' || phase.status === 'skipped') {
    return <PhaseNotice status={phase.status} message={phase.message} code={phase.code} />;
  }
  // The job that undid the load of one file reports what it removed instead of files loaded.
  if (phase.barsRemoved !== null && phase.barsRemoved !== undefined) {
    return (
      <Typography variant="body2">
        {phase.message ?? `CSV load undone, ${formatNumber(phase.barsRemoved)} bars removed`}
      </Typography>
    );
  }
  const unknown = phase.unknownSymbols.length;
  const ambiguous = phase.ambiguousSymbols.length;
  return (
    <>
      <Typography variant="body2">{csvResult(phase)}</Typography>
      <Typography variant="body2" color="text.secondary">
        {repairLine(phase)}
      </Typography>
      <Collapsible label="Rejected rows (examples)" items={phase.rejected > 0 ? phase.rejectionExamples : []} />
      <Collapsible
        label={`${formatNumber(unknown)} unknown ${unknown === 1 ? 'symbol' : 'symbols'}`}
        items={phase.unknownSymbols}
      />
      <Collapsible
        label={`${formatNumber(ambiguous)} ambiguous ${ambiguous === 1 ? 'symbol' : 'symbols'}`}
        items={phase.ambiguousSymbols}
      />
      <Collapsible label={`${formatNumber(phase.failedFiles.length)} files failed`} items={phase.failedFiles} />
    </>
  );
}

/** Step 1: how many files of the CSV folder are loaded, the rows, what is pending and how the last load went. */
export function CsvDetails({ csv, phase, warnings, onReview }: CsvDetailsProps) {
  const pending = csv.filesPending;
  return (
    <StepDetails number={1} title="CSV files" icon={FolderIcon} hue="data" phase={phase}>
      <Typography sx={{ fontWeight: 600 }}>
        {formatNumber(csv.filesLoaded)} of {formatNumber(csv.filesTotal)} files loaded
      </Typography>
      {!csv.folderExists && (
        <Alert severity="warning" icon={<WarningIcon />} sx={{ py: 0 }}>
          The CSV folder {csv.folder} was not found. Check that data/Stocks_CSV exists and is mounted into the API.
        </Alert>
      )}
      <Typography variant="body2" color="text.secondary">
        {formatNumber(csv.rows)} rows · {formatNumber(pending)} pending
      </Typography>
      {csv.lastLoadAt && (
        <Typography variant="body2" color="text.secondary">
          Last load <time dateTime={csv.lastLoadAt}>{formatIstDateTime(csv.lastLoadAt)}</time>
        </Typography>
      )}
      <Button
        size="small"
        variant="outlined"
        startIcon={<RuleIcon />}
        aria-label={pending > 0 ? `Review files, ${formatNumber(pending)} to review` : 'Review files'}
        onClick={onReview}
        sx={{ justifySelf: 'start' }}
      >
        Review files
      </Button>
      {phase && !isActive(phase.status) && <Result phase={phase} />}
      {phase && !isActive(phase.status) && <JobWarnings warnings={warnings} />}
    </StepDetails>
  );
}
