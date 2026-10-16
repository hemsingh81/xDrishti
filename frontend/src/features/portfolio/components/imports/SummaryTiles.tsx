import { Stack, Typography } from '@mui/material';
import type { ImportSummary } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { SummaryTile as Tile } from './SummaryTile';

/** What was in the file: the counts every kind has, as tiles, and the period the report says it covers. */
export function SummaryTiles({ summary }: { summary: ImportSummary }) {
  return (
    <Stack spacing={0.75}>
      <Stack component="dl" direction="row" sx={{ m: 0, gap: 1, flexWrap: 'wrap' }}>
        <Tile label="Rows read" value={summary.rows} />
        <Tile label="Valid" value={summary.valid} />
        <Tile label="To add" value={summary.toAdd} />
        <Tile label="Already imported" value={summary.alreadyImported} />
        <Tile label="Left out" value={summary.skipped} />
        <Tile label="Bad rows" value={summary.bad} />
      </Stack>
      {summary.from && summary.to && (
        <Typography variant="body2" color="text.secondary">
          {'Period: '}
          <time dateTime={summary.from}>{formatDate(summary.from)}</time>
          {' to '}
          <time dateTime={summary.to}>{formatDate(summary.to)}</time>
        </Typography>
      )}
    </Stack>
  );
}
