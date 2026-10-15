import type { ImportFilePreview } from '@/shared/api';
import type { NameChoices } from '../../lib/reportImport';
import { BillBlock } from './BillBlock';
import { CostsBlock } from './CostsBlock';
import { DematBlock } from './DematBlock';
import { TradesBlock } from './TradesBlock';

interface KindBlockProps {
  preview: ImportFilePreview;
  account: { id: string; name: string };
  names: NameChoices;
  busy: boolean;
}

/** What the kind of the file shows: the transaction report's names and trades, the DP lines, the demat check or the trade list. */
export function KindBlock({ preview, account, names, busy }: KindBlockProps) {
  if (preview.bill) return <BillBlock preview={preview} accountId={account.id} busy={busy} {...names} />;
  if (preview.trades) return <TradesBlock report={preview.trades} />;
  if (preview.costs) return <CostsBlock costs={preview.costs} />;
  if (preview.demat) return <DematBlock demat={preview.demat} />;
  return null;
}
