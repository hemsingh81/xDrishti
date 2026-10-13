import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import { Button, Stack, Typography } from '@mui/material';
import type { Ref } from 'react';
import type { TradeImportResult } from '@/shared/api';
import { countOf } from '../../lib/tradeText';

interface ImportDoneProps {
  result: TradeImportResult;
  headingRef: Ref<HTMLHeadingElement>;
  onHistory: () => void;
  onAnother: () => void;
}

/** What the import did, in words, with the way on: see it in History, or import another file. */
export function ImportDone({ result, headingRef, onHistory, onAnother }: ImportDoneProps) {
  return (
    <Stack component="section" aria-label="Import result" spacing={1.5}>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
        <CheckCircleIcon color="success" aria-hidden />
        <Typography ref={headingRef} component="h3" variant="h3" tabIndex={-1}>
          {result.batch === null ? 'Nothing new to import' : 'Import done'}
        </Typography>
      </Stack>
      <Typography>
        {result.batch === null
          ? `The ${countOf(result.alreadyImported, 'trade')} of the file were stored already.`
          : `${countOf(result.imported, 'trade')} stored${result.alreadyImported > 0 ? `, ${countOf(result.alreadyImported, 'trade')} were stored already` : ''}.`}
      </Typography>
      <Typography color="text.secondary">
        {result.rebuilt
          ? `The lots of ${countOf(result.holdingsRebuilt, 'holding')} were rebuilt from the trades (${countOf(result.lotsReplaced, 'lot')} replaced). Holdings that did not match were left as they were.`
          : 'Your lots were not changed. Rebuild a holding’s lots from its Trades section when you are ready.'}
      </Typography>
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        <Button variant="contained" onClick={onHistory}>
          See it in History
        </Button>
        <Button onClick={onAnother}>Import another file</Button>
      </Stack>
    </Stack>
  );
}
