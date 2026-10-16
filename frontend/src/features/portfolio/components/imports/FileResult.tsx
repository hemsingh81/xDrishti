import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Alert, Stack, Typography } from '@mui/material';
import { useRef, useState, type Ref } from 'react';
import type { ImportFileResult } from '@/shared/api';
import { costsLine, tradesLine } from '../../lib/reportImport';
import { countOf } from '../../lib/tradeText';
import { UndoCostImport } from './UndoCostImport';
import { UndoTradeImport } from './UndoTradeImport';

const titles: Record<string, string> = {
  imported: 'Imported',
  nothing_new: 'Nothing new to import',
  not_supported: 'Not imported',
  rejected: 'Not imported',
};

/** What the import did for one file: the counts in words, the reason when it was refused, and Undo while the import counts. */
export function FileResult({ result, headingRef }: { result: ImportFileResult; headingRef?: Ref<HTMLDivElement> }) {
  const [undone, setUndone] = useState(false);
  const statusRef = useRef<HTMLDivElement>(null);
  const failed = result.status === 'rejected' || result.status === 'not_supported';
  const lines = [
    result.trades ? tradesLine(result.trades) : null,
    result.costs ? costsLine(result.costs) : null,
    result.demat
      ? `${countOf(result.demat.namesLearned, 'name')} remembered (${result.demat.namesKnown} known already). No holding was changed.`
      : null,
    result.aliasesSaved > 0 ? `${countOf(result.aliasesSaved, 'match', 'matches')} remembered for later files.` : null,
  ].filter((line): line is string => line !== null);
  const tradeBatch = result.trades?.batch ?? null;
  const costBatch = result.costs?.batch ?? null;
  // The Undo button that opened the dialog is gone: the keyboard goes to the line that says what happened.
  const afterUndo = () => {
    setUndone(true);
    requestAnimationFrame(() => {
      statusRef.current?.focus();
    });
  };
  return (
    <Stack component="section" aria-label={`Result for ${result.fileName}`} spacing={1}>
      <Stack ref={headingRef} direction="row" sx={{ alignItems: 'center', gap: 1 }} tabIndex={-1}>
        {failed ? <InfoIcon color="warning" aria-hidden /> : <CheckCircleIcon color="success" aria-hidden />}
        <Typography component="h5" variant="h4">
          {titles[result.status] ?? result.status}
        </Typography>
      </Stack>
      {failed && (
        <Alert severity="warning" role="alert">
          {result.error?.message ?? result.message ?? 'The file was not imported.'}
        </Alert>
      )}
      {!failed && result.status === 'nothing_new' && result.message && (
        <Typography color="text.secondary">{result.message}</Typography>
      )}
      <Stack ref={statusRef} tabIndex={-1} spacing={0.5}>
        {undone ? (
          <Typography>This import was undone: what it stored no longer counts.</Typography>
        ) : (
          lines.map((line) => <Typography key={line}>{line}</Typography>)
        )}
      </Stack>
      {tradeBatch && !undone && (
        <Stack direction="row" sx={{ gap: 1 }}>
          <UndoTradeImport batch={tradeBatch} onUndone={afterUndo} />
        </Stack>
      )}
      {costBatch && !undone && (
        <Stack direction="row" sx={{ gap: 1 }}>
          <UndoCostImport batch={costBatch} onUndone={afterUndo} />
        </Stack>
      )}
    </Stack>
  );
}
