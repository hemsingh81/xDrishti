import { Stack } from '@mui/material';
import type { ImportFilePreview } from '@/shared/api';
import type { BillDraft, BillDrafts } from '../../lib/reportImport';
import { BillChargesView } from './BillChargesView';
import { BillCoverageView } from './BillCoverageView';
import { BillNames } from './BillNames';
import { TradesBlock } from './TradesBlock';

interface BillBlockProps {
  preview: ImportFilePreview;
  accountId: string;
  drafts: BillDrafts;
  choicesChanged: boolean;
  busy: boolean;
  onChoose: (draft: BillDraft) => void;
  onUnchoose: (key: string) => void;
  onPreviewAgain: () => void;
}

/** A global transaction report: its names, what Dhan's own trades cover, the charges check and the trades with their rebuild preview. */
export function BillBlock({ preview, ...names }: BillBlockProps) {
  const { bill, trades } = preview;
  return (
    <Stack spacing={2}>
      {bill && <BillNames bill={bill} {...names} />}
      {bill && <BillCoverageView coverage={bill.coverage} />}
      {bill && <BillChargesView charges={bill.charges} />}
      {trades && <TradesBlock report={trades} />}
    </Stack>
  );
}
