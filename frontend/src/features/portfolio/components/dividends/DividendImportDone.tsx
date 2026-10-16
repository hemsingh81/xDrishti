import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import { Button, Stack, Typography } from '@mui/material';
import { useRef, useState, type Ref } from 'react';
import type { DividendImportResult } from '@/shared/api';
import { formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { useToast } from '@/shared/ui/toastContext';
import { undoneText } from '../../lib/dividendImport';
import { countOf } from '../../lib/tradeText';
import { Figure } from './Figure';
import { UndoDividendDialog } from './UndoDividendDialog';

interface DividendImportDoneProps {
  result: DividendImportResult;
  headingRef: Ref<HTMLHeadingElement>;
  onHistory: () => void;
  onAnother: () => void;
}

/** What the import did: the counts, with Undo while the import counts, and the way on (History, or another file). */
export function DividendImportDone({ result, headingRef, onHistory, onAnother }: DividendImportDoneProps) {
  const { notify } = useToast();
  const [asking, setAsking] = useState(false);
  const [undone, setUndone] = useState(false);
  const statusRef = useRef<HTMLParagraphElement>(null);
  const { batch } = result;
  return (
    <Stack component="section" aria-label="Import result" spacing={1.5}>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
        <CheckCircleIcon color="success" aria-hidden />
        <Typography ref={headingRef} component="h3" variant="h3" tabIndex={-1}>
          {batch === null ? 'Nothing new to import' : 'Import done'}
        </Typography>
      </Stack>
      {batch === null ? (
        <Typography>
          {`The ${countOf(result.alreadyImported, 'dividend')} of the file ${result.alreadyImported === 1 ? 'was' : 'were'} stored already.`}
        </Typography>
      ) : (
        <Typography ref={statusRef} tabIndex={-1}>
          {undone
            ? `This import was undone: its ${countOf(batch.imported, 'dividend')} no longer count.`
            : `${countOf(result.imported, 'dividend')} stored (${formatInrPrecise(result.amount)}).`}
        </Typography>
      )}
      <Stack component="dl" direction="row" sx={{ m: 0, columnGap: 3, rowGap: 1.5, flexWrap: 'wrap' }}>
        <Figure label="Imported" value={formatNumber(result.imported)} />
        <Figure label="Amount" value={formatInrPrecise(result.amount)} />
        <Figure label="Allocated to a holding" value={formatNumber(result.allocated)} />
        <Figure label="Instrument only" value={formatNumber(result.instrumentOnly)} />
        <Figure label="Unallocated" value={formatNumber(result.unallocated)} />
        <Figure label="Already imported" value={formatNumber(result.alreadyImported)} />
        <Figure label="Duplicates skipped" value={formatNumber(result.possibleDuplicatesSkipped)} />
        <Figure label="Matches remembered" value={formatNumber(result.aliasesSaved)} />
      </Stack>
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        {batch !== null && !undone && (
          <Button
            variant="outlined"
            color="error"
            onClick={() => {
              setAsking(true);
            }}
          >
            Undo this import
          </Button>
        )}
        <Button variant="contained" onClick={onHistory}>
          See it in History
        </Button>
        <Button onClick={onAnother}>Import another file</Button>
      </Stack>
      {asking && batch !== null && (
        <UndoDividendDialog
          batch={{ id: batch.id, fileName: batch.fileName, activeDividends: batch.activeDividends }}
          onClose={() => {
            setAsking(false);
          }}
          onUndone={(undoResult) => {
            notify(undoneText(undoResult));
            setUndone(true);
            setAsking(false);
            // The Undo button that opened the dialog is gone: the keyboard goes to the line that says what happened.
            requestAnimationFrame(() => {
              statusRef.current?.focus();
            });
          }}
        />
      )}
    </Stack>
  );
}
