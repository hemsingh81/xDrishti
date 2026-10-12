import { useId } from 'react';
import { Box, Chip, Stack, Typography } from '@mui/material';
import type { ActionsImportResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

function Count({ label, value }: { label: string; value: number }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" component="dt">
        {label}
      </Typography>
      <Typography component="dd" sx={{ m: 0, fontWeight: 600 }} className="numeric">
        {formatNumber(value)}
      </Typography>
    </Box>
  );
}

/** What a preview found: the counts, the symbols that match no instrument, and the first lines with a problem. */
export function ImportPreviewResult({ result }: { result: ActionsImportResult }) {
  const unknownId = useId();
  const errorsId = useId();
  return (
    <Stack component="section" aria-label="Import preview" spacing={1.5}>
      <Typography variant="h3">Preview — nothing is saved yet</Typography>
      <Stack component="dl" direction="row" sx={{ m: 0, gap: 3, flexWrap: 'wrap' }}>
        <Count label="Lines read" value={result.lines} />
        <Count label="Will be imported" value={result.importable} />
        <Count label="Duplicates (skipped)" value={result.duplicates} />
        <Count label="Invalid lines (skipped)" value={result.invalid} />
      </Stack>
      {result.unknownSymbols.length > 0 && (
        <Box>
          <Typography variant="body2" id={unknownId}>
            Unknown symbols — no instrument matches, they are never guessed:
          </Typography>
          <Stack
            component="ul"
            aria-labelledby={unknownId}
            direction="row"
            sx={{ m: 0, p: 0, gap: 0.5, flexWrap: 'wrap' }}
          >
            {result.unknownSymbols.map((symbol) => (
              <Box component="li" key={symbol} sx={{ listStyle: 'none' }}>
                <Chip size="small" variant="outlined" label={symbol} />
              </Box>
            ))}
          </Stack>
        </Box>
      )}
      {result.errors.length > 0 && (
        <Box>
          <Typography variant="body2" id={errorsId}>
            Lines with a problem{result.invalid > result.errors.length ? ' (the first ones)' : ''}:
          </Typography>
          <Box component="ul" aria-labelledby={errorsId} sx={{ m: 0, pl: 3, maxHeight: 160, overflowY: 'auto' }}>
            {result.errors.map((error) => (
              <li key={error.line}>
                Line {error.line}: {error.message}
              </li>
            ))}
          </Box>
        </Box>
      )}
    </Stack>
  );
}
