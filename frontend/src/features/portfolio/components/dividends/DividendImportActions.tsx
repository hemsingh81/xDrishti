import { Button, Stack, Typography } from '@mui/material';
import type { DividendPreviewReport } from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';
import { countOf } from '../../lib/tradeText';
import { isOpen, type Drafts, willImportText } from '../../lib/dividendImport';

interface DividendImportActionsProps {
  report: DividendPreviewReport;
  drafts: Drafts;
  /** Possible duplicates the owner ticked. */
  lines: ReadonlySet<number>;
  running: boolean;
  onImport: () => void;
}

/** The confirmation: what will be stored (the ticked duplicates included), which names stay unallocated, and the Import button. */
export function DividendImportActions({ report, drafts, lines, running, onImport }: DividendImportActionsProps) {
  const ticked = report.items.filter((item) => item.status === 'possible_duplicate' && lines.has(item.line));
  const count = report.totals.count + ticked.length;
  const amount = report.totals.amount + ticked.reduce((sum, item) => sum + item.amount, 0);
  const openNames = new Set(report.items.filter(isOpen).map((item) => item.name));
  const unchosen = [...openNames].filter((name) => !drafts.has(name)).length;
  const remembered = [...drafts.values()].filter((draft) => draft.remember).length;
  return (
    <Stack spacing={1.5}>
      <Typography>
        {count === 0 ? 'There is nothing new to import.' : willImportText(count, amount)}
        {ticked.length > 0 &&
          ` ${countOf(ticked.length, 'possible duplicate')} included (${formatInrPrecise(ticked.reduce((sum, item) => sum + item.amount, 0))}).`}
      </Typography>
      {unchosen > 0 && (
        <Typography variant="body2" color="text.secondary">
          {`${countOf(unchosen, 'name')} without a choice will be stored unallocated, with the file’s name.`}
        </Typography>
      )}
      {remembered > 0 && (
        <Typography variant="body2" color="text.secondary">
          {`${countOf(remembered, 'match', 'matches')} will be remembered for later files.`}
        </Typography>
      )}
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button variant="contained" disabled={count === 0 || running} loading={running} onClick={onImport}>
          {count === 0 ? 'Import' : `Import ${countOf(count, 'dividend')}`}
        </Button>
      </Stack>
    </Stack>
  );
}
