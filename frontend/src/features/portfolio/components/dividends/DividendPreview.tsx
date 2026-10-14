import { Stack, Typography } from '@mui/material';
import { useId, type Ref } from 'react';
import type { DividendPreviewReport } from '@/shared/api';
import type { Drafts, MatchDraft } from '../../lib/dividendImport';
import { DividendNotices } from './DividendNotices';
import { DividendRows } from './DividendRows';
import { DividendSummary } from './DividendSummary';

interface DividendPreviewProps {
  report: DividendPreviewReport;
  account: { id: string; name: string };
  headingRef: Ref<HTMLHeadingElement>;
  drafts: Drafts;
  lines: ReadonlySet<number>;
  onChoose: (draft: MatchDraft) => void;
  onUnchoose: (key: string) => void;
  onInclude: (line: number, on: boolean) => void;
  onIncludeAll: (on: boolean) => void;
}

/** What importing the file would do: the counts, the notices, and the rows by status with the pickers. Nothing is stored yet. */
export function DividendPreview({ report, account, headingRef, ...choices }: DividendPreviewProps) {
  const headingId = useId();
  return (
    <Stack component="section" aria-labelledby={headingId} spacing={2}>
      <Typography ref={headingRef} id={headingId} component="h3" variant="h3" tabIndex={-1}>
        {`Preview of ${report.file.fileName} — nothing is stored yet`}
      </Typography>
      <DividendSummary report={report} />
      <DividendNotices report={report} account={account} />
      <DividendRows key={report.digest} report={report} accountId={account.id} {...choices} />
    </Stack>
  );
}
