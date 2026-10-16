import { Alert, Button, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import type { ImportFilePreview } from '@/shared/api';
import { capitalise, formatInrPrecise } from '@/shared/lib/format';
import { countOf, replacedSentence, type RebuildTotals } from '../../lib/tradeText';

interface ImportFilesActionsProps {
  /** The previews of the files that would be imported now. */
  previews: readonly ImportFilePreview[];
  totals: RebuildTotals;
  rebuild: boolean;
  onRebuild: (on: boolean) => void;
  confirmed: boolean;
  onConfirmed: (confirmed: boolean) => void;
  /** Rebuilding is on and replaces lots the owner dated by hand: a tick is needed first. */
  needsConfirmation: boolean;
  /** The matches changed since the previews were read. */
  choicesChanged: boolean;
  /** The server refused the previews as out of date. */
  outdated: boolean;
  running: boolean;
  busy: boolean;
  onImport: () => void;
}

/** What importing the files would store, in one sentence. */
function willStoreText(previews: readonly ImportFilePreview[]): string {
  const trades = previews.reduce((sum, preview) => sum + (preview.trades?.file.newTrades ?? 0), 0);
  const charges = previews.reduce((sum, preview) => sum + (preview.costs?.counts.toStore ?? 0), 0);
  const amount = previews.reduce((sum, preview) => sum + (preview.costs?.amountToStore ?? 0), 0);
  const names = previews.reduce((sum, preview) => sum + (preview.demat?.names.new ?? 0), 0);
  const parts = [
    trades > 0 ? countOf(trades, 'trade') : null,
    charges > 0 ? `${countOf(charges, 'depository charge')} (${formatInrPrecise(amount)})` : null,
    names > 0 ? `${countOf(names, 'name')} remembered from a demat statement` : null,
  ].filter((part): part is string => part !== null);
  return parts.length === 0 ? 'Nothing will be stored.' : `${capitalise(parts.join(', '))} will be stored.`;
}

/**
 * The confirmation for all the files at once: what will be stored, whether to rebuild lots (a tick is needed first when
 * lots dated by hand would be replaced), and the one Import button, which sends each file's reviewed preview with it.
 */
export function ImportFilesActions({
  previews,
  totals,
  rebuild,
  onRebuild,
  confirmed,
  onConfirmed,
  needsConfirmation,
  choicesChanged,
  outdated,
  running,
  busy,
  onImport,
}: ImportFilesActionsProps) {
  const replaced = replacedSentence(totals.handDated, totals.sync);
  const count = previews.length;
  return (
    <Stack spacing={1.5} component="section" aria-label="Import the files">
      <Typography>{count === 0 ? 'There is nothing to import.' : willStoreText(previews)}</Typography>
      {totals.holdings > 0 && (
        <>
          <FormControlLabel
            control={
              <Checkbox
                checked={rebuild}
                disabled={busy}
                onChange={(event) => {
                  onRebuild(event.target.checked);
                }}
              />
            }
            label="Rebuild the lots from the trades"
          />
          {rebuild ? (
            <Alert severity={replaced ? 'warning' : 'info'}>
              {`Rebuilding replaces ${countOf(totals.lotsBefore, 'lot')} with ${countOf(totals.lotsAfter, 'lot')} in ${countOf(totals.holdings, 'holding')}.`}
              {replaced ? ` ${capitalise(replaced)}.` : ''}
            </Alert>
          ) : (
            <Typography variant="body2" color="text.secondary">
              The trades are stored and your lots stay as they are. Rebuild a holding’s lots from its Trades section
              when you are ready.
            </Typography>
          )}
        </>
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
      {choicesChanged && (
        <Typography variant="body2" color="text.secondary">
          You changed matches: preview again to apply them before importing.
        </Typography>
      )}
      {outdated && (
        <Typography variant="body2" color="text.secondary">
          The previews are out of date: press Preview to read the files again before importing.
        </Typography>
      )}
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button
          variant="contained"
          disabled={count === 0 || busy || choicesChanged || outdated || (needsConfirmation && !confirmed)}
          loading={running}
          onClick={onImport}
        >
          {count === 0 ? 'Import' : `Import ${countOf(count, 'file')}`}
        </Button>
      </Stack>
    </Stack>
  );
}
