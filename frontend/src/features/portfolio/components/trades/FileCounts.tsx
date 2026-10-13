import { Box, Chip, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import type { TradeFileSummary } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { skippedParts } from '../../lib/tradeText';
import { BadRows } from './BadRows';
import { Count } from './Count';

/** What was in the file: the counts, the rows left out (with the reasons) and the rows that could not be read. */
export function FileCounts({ file }: { file: TradeFileSummary }) {
  const reasonsId = useId();
  const skipped = skippedParts(file.skipped.byReason);
  return (
    <Stack spacing={1.5}>
      <Stack component="dl" direction="row" sx={{ m: 0, columnGap: 3, rowGap: 1.5, flexWrap: 'wrap' }}>
        <Count label="Rows read" value={file.rows} />
        <Count label="Valid trades" value={file.parsed} />
        <Count label="New" value={file.newTrades} />
        <Count label="Already imported" value={file.alreadyImported} />
        <Count label="Duplicates in the file" value={file.duplicates} />
        <Count label="Skipped" value={file.skipped.total} />
        <Count label="Bad rows" value={file.badTotal} />
      </Stack>
      {file.skipped.total > 0 && (
        <Box>
          <Typography variant="body2" id={reasonsId}>
            Skipped: these rows are not NSE equity trades and are never imported.
          </Typography>
          <Stack
            component="ul"
            aria-labelledby={reasonsId}
            direction="row"
            sx={{ m: 0, mt: 0.5, p: 0, gap: 0.5, flexWrap: 'wrap', listStyle: 'none' }}
          >
            {skipped.map((part) => (
              <li key={part.reason}>
                <Chip size="small" variant="outlined" label={`${part.label}: ${formatNumber(part.count)}`} />
              </li>
            ))}
          </Stack>
        </Box>
      )}
      <BadRows rows={file.bad} total={file.badTotal} />
    </Stack>
  );
}
