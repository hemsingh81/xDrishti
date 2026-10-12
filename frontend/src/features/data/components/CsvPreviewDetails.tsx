import { Box, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import type { CsvFilePreview, CsvPreviewComparison } from '@/shared/api';
import { formatIstDateTime, formatNumber, formatPrice } from '@/shared/lib/format';
import { Facts } from '@/shared/ui';
import { numeric } from '@/theme';

function comparisonText(c: CsvPreviewComparison): string {
  return [
    `would insert ${formatNumber(c.wouldInsert)}`,
    `update ${formatNumber(c.wouldUpdate)}`,
    `keep existing Dhan ${formatNumber(c.keepExistingDhan)}`,
    `unchanged ${formatNumber(c.unchanged)}`,
  ].join(' · ');
}

function Rejections({ file }: { file: CsvFilePreview }) {
  const reasons = Object.entries(file.rejections);
  if (file.rejected === 0 && file.repaired === 0) return <>None</>;
  return (
    <Stack>
      <span>
        {formatNumber(file.repaired)} repaired, {formatNumber(file.rejected)} rejected
      </span>
      {reasons.length > 0 && (
        <Box component="ul" aria-label="Rejected rows by reason" sx={{ m: 0, pl: 2.5 }}>
          {reasons.map(([reason, count]) => (
            <li key={reason}>
              {reason}: {formatNumber(count)}
            </li>
          ))}
        </Box>
      )}
      {file.examples.length > 0 && (
        <Box component="ul" aria-label="Example lines" sx={{ m: 0, pl: 2.5, color: 'text.secondary' }}>
          {file.examples.map((example) => (
            <li key={example}>{example}</li>
          ))}
        </Box>
      )}
    </Stack>
  );
}

function FirstRows({ file }: { file: CsvFilePreview }) {
  if (file.firstRows.length === 0) return null;
  return (
    <Box sx={{ overflowX: 'auto' }}>
      <Table size="small" aria-label={`First rows of ${file.name}`}>
        <TableHead>
          <TableRow>
            <TableCell>Time (IST)</TableCell>
            {['Open', 'High', 'Low', 'Close', 'Volume'].map((name) => (
              <TableCell key={name} align="right">
                {name}
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {file.firstRows.map((bar) => (
            <TableRow key={bar.time}>
              <TableCell>{formatIstDateTime(bar.time)}</TableCell>
              {[bar.open, bar.high, bar.low, bar.close].map((value, index) => (
                <TableCell key={index} align="right" sx={numeric}>
                  {formatPrice(value)}
                </TableCell>
              ))}
              <TableCell align="right" sx={numeric}>
                {formatNumber(bar.volume)}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}

/** What a file that passes would do: its counts, the rejected rows with examples, the range, the first rows and the comparison with the stored bars. */
export function CsvPreviewDetails({ file }: { file: CsvFilePreview }) {
  const c = file.comparison;
  return (
    <Stack spacing={1.5}>
      <Facts
        items={[
          { label: 'Instrument', value: file.instrument?.symbol ?? file.symbol ?? '—' },
          { label: 'Rows', value: formatNumber(file.rows) },
          { label: 'Accepted', value: formatNumber(file.accepted) },
          { label: 'Repaired / rejected', value: <Rejections file={file} /> },
          { label: 'Trading days', value: formatNumber(file.tradingDays) },
          {
            label: 'First to last bar (IST)',
            value:
              file.firstBar && file.lastBar
                ? `${formatIstDateTime(file.firstBar)} to ${formatIstDateTime(file.lastBar)}`
                : '—',
          },
          {
            label: 'Against stored bars',
            value: c ? (
              <Stack>
                <span>{comparisonText(c)}</span>
                {c.sampled && (
                  <Typography variant="caption" color="text.secondary">
                    Sampled: only {formatNumber(c.comparedRows)} rows were compared, so these counts are of that sample,
                    not of the whole file.
                  </Typography>
                )}
              </Stack>
            ) : (
              'Not compared'
            ),
          },
        ]}
      />
      <FirstRows file={file} />
    </Stack>
  );
}
