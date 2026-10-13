import { Stack, Typography } from '@mui/material';
import { useId, type Ref } from 'react';
import type { TradePreviewReport } from '@/shared/api';
import { FileCounts } from './FileCounts';
import { HeldNotes } from './HeldNotes';
import { SecuritiesTable } from './SecuritiesTable';

/** What importing the file would do: the counts, the rows left out or unreadable, and each security with its rebuild result. */
export function PreviewReport({
  report,
  headingRef,
}: {
  report: TradePreviewReport;
  headingRef: Ref<HTMLHeadingElement>;
}) {
  const headingId = useId();
  const matched = report.securities.filter((security) => security.matched);
  const unmatched = report.securities.filter((security) => !security.matched);
  return (
    <Stack component="section" aria-labelledby={headingId} spacing={2}>
      <Typography ref={headingRef} id={headingId} component="h3" variant="h3" tabIndex={-1}>
        {`Preview of ${report.file.fileName} — nothing is stored yet`}
      </Typography>
      <FileCounts file={report.file} />
      <Typography component="h4" variant="h4">
        Securities
      </Typography>
      <SecuritiesTable securities={matched} />
      <HeldNotes report={report} unmatched={unmatched} />
    </Stack>
  );
}
