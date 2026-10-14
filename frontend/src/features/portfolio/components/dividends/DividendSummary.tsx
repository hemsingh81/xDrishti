import { Stack, Typography } from '@mui/material';
import type { DividendPreviewReport } from '@/shared/api';
import { formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { Figure } from './Figure';

/** What was in the file and what importing it would add: the counts as a list of figures, and the totals by year. */
export function DividendSummary({ report }: { report: DividendPreviewReport }) {
  const { file, totals } = report;
  return (
    <Stack spacing={1.5}>
      <Stack component="dl" direction="row" sx={{ m: 0, columnGap: 3, rowGap: 1.5, flexWrap: 'wrap' }}>
        <Figure label="Rows read" value={formatNumber(file.rows)} />
        <Figure label="To add" value={formatNumber(file.toAdd)} />
        <Figure label="Amount to add" value={formatInrPrecise(totals.amount)} />
        <Figure label="Already imported" value={formatNumber(file.alreadyImported)} />
        <Figure label="Possible duplicates" value={formatNumber(file.possibleDuplicates)} />
        <Figure label="Needs a match" value={formatNumber(file.needsMatch)} />
        {file.ambiguous > 0 && <Figure label="Ambiguous" value={formatNumber(file.ambiguous)} />}
        <Figure label="Bad rows" value={formatNumber(file.badTotal)} />
      </Stack>
      {totals.byYear.length > 0 && (
        <Stack spacing={0.5}>
          <Typography component="h4" variant="h4">
            To add, by year
          </Typography>
          <Stack
            component="ul"
            aria-label="Dividends to add by year"
            sx={{ m: 0, p: 0, columnGap: 3, rowGap: 0.5, flexWrap: 'wrap', listStyle: 'none' }}
            direction="row"
          >
            {totals.byYear.map((year) => (
              <Typography component="li" key={year.year} variant="body2" className="numeric">
                {`${String(year.year)}: ${formatNumber(year.count)} · ${formatInrPrecise(year.amount)}`}
              </Typography>
            ))}
          </Stack>
        </Stack>
      )}
    </Stack>
  );
}
