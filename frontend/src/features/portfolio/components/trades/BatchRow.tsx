import { Button, Stack, Typography } from '@mui/material';
import type { TradeBatch } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { countOf } from '../../lib/tradeText';
import { BatchStatusChip } from './BatchStatusChip';

interface BatchRowProps {
  batch: TradeBatch;
  onUndo: (batch: TradeBatch) => void;
}

/** One imported file: its name, account and time, what was in it, how many of its trades still count, and Undo while it is imported. */
export function BatchRow({ batch, onUndo }: BatchRowProps) {
  const undone = batch.status === 'undone';
  const counts = [
    countOf(batch.parsed, 'valid trade'),
    `${countOf(batch.imported, 'trade')} stored`,
    batch.duplicates > 0 ? countOf(batch.duplicates, 'duplicate') : null,
    batch.skipped > 0 ? `${countOf(batch.skipped, 'row')} skipped` : null,
    batch.bad > 0 ? countOf(batch.bad, 'bad row') : null,
  ].filter((part): part is string => part !== null);
  return (
    <Stack
      component="li"
      direction="row"
      sx={{ gap: 1.5, py: 1.5, alignItems: 'flex-start', justifyContent: 'space-between' }}
    >
      <Stack spacing={0.5} sx={{ minWidth: 0, flex: 1 }}>
        <Stack direction="row" sx={{ alignItems: 'center', columnGap: 1, rowGap: 0.5, flexWrap: 'wrap' }}>
          <Typography component="span" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
            {batch.fileName}
          </Typography>
          <BatchStatusChip undone={undone} />
        </Stack>
        <Typography variant="body2" color="text.secondary">
          {`${batch.accountName} · ${formatIstDateTime(batch.createdAt)} · by ${batch.createdBy}`}
        </Typography>
        <Typography variant="body2" className="numeric">
          {counts.join(' · ')}
        </Typography>
        <Typography variant="body2" className="numeric" color="text.secondary">
          {undone && batch.undoneAt
            ? `Undone ${formatIstDateTime(batch.undoneAt)}; none of its trades count now.`
            : `${countOf(batch.activeTrades, 'trade')} in ${countOf(batch.securities, 'security', 'securities')} still count.`}
        </Typography>
      </Stack>
      {!undone && (
        <Button
          size="small"
          variant="outlined"
          color="error"
          aria-label={`Undo the import of ${batch.fileName}`}
          onClick={() => {
            onUndo(batch);
          }}
        >
          Undo
        </Button>
      )}
    </Stack>
  );
}
