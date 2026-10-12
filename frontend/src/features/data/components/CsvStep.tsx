import FolderIcon from '@mui/icons-material/FolderOpenOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Alert, Typography } from '@mui/material';
import type { CsvOverview, CsvPhase } from '@/shared/api';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { csvResult, repairLine } from '../lib/summaries';
import { isActive } from '../lib/phases';
import { Collapsible } from './Collapsible';
import { PhaseNotice } from './PhaseNotice';
import { PipelineStep } from './PipelineStep';
import { StepProgress } from './StepProgress';

interface CsvStepProps {
  csv: CsvOverview;
  /** The running job's phase, else the latest finished one. */
  phase: CsvPhase | null;
}

function Result({ phase }: { phase: CsvPhase }) {
  if (phase.status === 'failed' || phase.status === 'skipped') {
    return <PhaseNotice status={phase.status} message={phase.message} code={phase.code} />;
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
export function CsvStep({ csv, phase }: CsvStepProps) {
  return (
    <PipelineStep number={1} title="CSV files" icon={FolderIcon} hue="data" phase={phase}>
      <Typography sx={{ fontWeight: 600 }}>
        {formatNumber(csv.filesLoaded)} of {formatNumber(csv.filesTotal)} files loaded
      </Typography>
      {!csv.folderExists && (
        <Alert severity="warning" icon={<WarningIcon />} sx={{ py: 0 }}>
          The CSV folder {csv.folder} was not found. Check that data/Stocks_CSV exists and is mounted into the API.
        </Alert>
      )}
      <Typography variant="body2" color="text.secondary">
        {formatNumber(csv.rows)} rows · {formatNumber(csv.filesPending)} pending
      </Typography>
      {csv.lastLoadAt && (
        <Typography variant="body2" color="text.secondary">
          Last load <time dateTime={csv.lastLoadAt}>{formatIstDateTime(csv.lastLoadAt)}</time>
        </Typography>
      )}
      {phase && isActive(phase.status) && (
        <StepProgress
          title="CSV files"
          status={phase.status === 'pending' ? 'pending' : 'running'}
          done={phase.done}
          total={phase.total}
          unit="files"
          message={phase.message}
        />
      )}
      {phase && !isActive(phase.status) && <Result phase={phase} />}
    </PipelineStep>
  );
}
