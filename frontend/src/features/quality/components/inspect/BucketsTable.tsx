import { Box, Chip, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import type { QualityBucketRow, QualityBucketValues } from '@/shared/api';
import { formatIstDate, formatIstDayAndTime, formatNumber, formatPrice } from '@/shared/lib/format';
import { MiniTable, type MiniColumn } from './MiniTable';

/** The table never lists more than this many buckets at once; the owner narrows it by timeframe or differences. */
export const bucketRowLimit = 300;

type Field = keyof QualityBucketValues;
const fields: readonly Field[] = ['open', 'high', 'low', 'close', 'volume', 'bars'];

const columns: readonly MiniColumn[] = [
  { header: 'Bucket (IST)' },
  { header: 'Open', numeric: true },
  { header: 'High', numeric: true },
  { header: 'Low', numeric: true },
  { header: 'Close', numeric: true },
  { header: 'Volume', numeric: true },
  { header: '1m bars', numeric: true },
  { header: 'Result' },
];

const show = (field: Field, value: number) =>
  field === 'volume' || field === 'bars' ? formatNumber(value) : formatPrice(value);
const isDayBucket = (timeframe: string) => timeframe === '1D' || timeframe === '1W' || timeframe === '1M';

/** A value that matches shows once; one that differs shows the stored value struck through and the recomputed one. */
function ValueCell({ row, field }: { row: QualityBucketRow; field: Field }) {
  const stored = row.stored?.[field];
  const recomputed = row.recomputed?.[field];
  if (row.fields.includes(field) && stored !== undefined && recomputed !== undefined) {
    return (
      <>
        <Box component="del" sx={{ display: 'block' }}>
          <Box component="span" sx={visuallyHidden}>
            stored{' '}
          </Box>
          {show(field, stored)}
        </Box>
        <Box component="ins" sx={{ display: 'block', textDecoration: 'none', fontWeight: 700 }}>
          <Box component="span" sx={visuallyHidden}>
            recomputed{' '}
          </Box>
          {show(field, recomputed)}
        </Box>
      </>
    );
  }
  const value = stored ?? recomputed;
  return <>{value === undefined ? '—' : show(field, value)}</>;
}

function ResultChip({ row }: { row: QualityBucketRow }) {
  if (!row.differs) return <Chip size="small" variant="outlined" color="success" label="Matches" />;
  const label = row.fields.includes('missing')
    ? 'Missing from the store'
    : row.fields.includes('unexpected')
      ? 'Not expected'
      : `Differs: ${row.fields.join(', ')}`;
  return <Chip size="small" variant="outlined" color="error" label={label} />;
}

/** Stored and recomputed buckets side by side; the fields that differ show both values, in words as well as colour. */
export function BucketsTable({ items }: { items: readonly QualityBucketRow[] }) {
  const shown = items.slice(0, bucketRowLimit);
  return (
    <>
      <MiniTable
        label="Stored vs recomputed"
        columns={columns}
        rows={shown.map((row) => ({
          key: `${row.timeframe}:${row.ts}`,
          flagged: row.differs,
          cells: [
            <time key="t" dateTime={row.ts}>
              {row.timeframe} ·{' '}
              {isDayBucket(row.timeframe) ? formatIstDate(row.ts) : formatIstDayAndTime(row.ts, { weekday: false })}
            </time>,
            ...fields.map((field) => <ValueCell key={field} row={row} field={field} />),
            <ResultChip key="r" row={row} />,
          ],
        }))}
      />
      {items.length > bucketRowLimit && (
        <Typography variant="body2" color="text.secondary">
          Showing the first {formatNumber(bucketRowLimit)} of {formatNumber(items.length)} buckets. Choose a timeframe
          or &quot;Differences only&quot; to narrow the list.
        </Typography>
      )}
    </>
  );
}
