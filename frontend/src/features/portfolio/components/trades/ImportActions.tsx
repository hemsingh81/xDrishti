import { Alert, Button, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import type { TradePreviewReport } from '@/shared/api';
import { capitalise } from '@/shared/lib/format';
import { countOf, rebuildTotals, replacedSentence } from '../../lib/tradeText';

interface ImportActionsProps {
  report: TradePreviewReport;
  confirmed: boolean;
  onConfirmed: (confirmed: boolean) => void;
  /** Which import is running: the other button waits. */
  running: 'rebuild' | 'only' | null;
  onImport: (rebuild: boolean) => void;
}

/**
 * The two ways to import. "Import only" stores the trades and leaves the lots alone. "Import and rebuild lots" also replaces
 * all lots of every holding marked OK — hand-dated and sync-added ones too — so when hand-dated lots are among them the owner
 * must tick a box first.
 */
export function ImportActions({ report, confirmed, onConfirmed, running, onImport }: ImportActionsProps) {
  const totals = rebuildTotals(report.securities);
  const replaced = replacedSentence(totals.handDated, totals.sync);
  const needsConfirmation = totals.handDated > 0;
  const nothingToStore = report.file.newTrades === 0;
  const busy = running !== null;
  const canRebuild = totals.holdings > 0 && (!needsConfirmation || confirmed);
  return (
    <Stack spacing={1.5}>
      <Typography variant="body2" color="text.secondary">
        Import only stores the new trades; your lots stay as they are. Import and rebuild lots also replaces all the
        lots of each holding marked OK with lots built from the trades.
      </Typography>
      {totals.holdings > 0 && (
        <Alert severity={replaced ? 'warning' : 'info'}>
          {`Rebuilding replaces ${countOf(totals.lotsBefore, 'lot')} with ${countOf(totals.lotsAfter, 'lot')} in ${countOf(totals.holdings, 'holding')}.`}
          {replaced ? ` ${capitalise(replaced)}.` : ''}
        </Alert>
      )}
      {needsConfirmation && (
        <FormControlLabel
          control={
            <Checkbox
              checked={confirmed}
              disabled={busy}
              onChange={(event) => {
                onConfirmed(event.target.checked);
              }}
            />
          }
          label={`I understand that ${countOf(totals.handDated, 'lot')} I dated by hand will be replaced by lots built from the trades.`}
        />
      )}
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button
          variant="contained"
          disabled={!canRebuild || busy}
          loading={running === 'rebuild'}
          onClick={() => {
            onImport(true);
          }}
        >
          Import and rebuild lots
        </Button>
        <Button
          variant="outlined"
          disabled={nothingToStore || busy}
          loading={running === 'only'}
          onClick={() => {
            onImport(false);
          }}
        >
          Import only
        </Button>
      </Stack>
      {nothingToStore && (
        <Typography variant="body2" color="text.secondary">
          Every trade of the file is stored already, so there is nothing to import only.
        </Typography>
      )}
      {totals.holdings === 0 && (
        <Typography variant="body2" color="text.secondary">
          No holding can be rebuilt from this file (see the status of each security).
        </Typography>
      )}
    </Stack>
  );
}
