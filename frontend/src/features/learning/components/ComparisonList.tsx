import { Box, Typography } from '@mui/material';
import { formatR, MetricValue } from '@/shared/analytics';
import type { DecisionsReport } from '../api/learningQueries';

type Comparison = DecisionsReport['comparisons'][number];

const verdictWords: Record<string, string> = {
  first_better: 'first better',
  second_better: 'second better',
  equal: 'equal',
  not_enough_data: 'not enough data',
};

const wordOf = (verdict: string) => verdictWords[verdict] ?? verdict.replaceAll('_', ' ');

/** The four comparisons with the server's verdict word and note. A null difference is not evaluable and shows no number. */
export function ComparisonList({ comparisons }: { comparisons: readonly Comparison[] }) {
  return (
    <Box component="ul" aria-label="Comparisons" sx={{ m: 0, pl: 2.5, display: 'grid', gap: 1 }}>
      {comparisons.map((c) => (
        <li key={c.question}>
          <Typography component="span" sx={{ fontWeight: 600 }}>
            {c.question}
          </Typography>{' '}
          {c.left} against {c.right}: {wordOf(c.verdict)}.{' '}
          {c.meanDifference === null ? (
            <MetricValue state={{ kind: 'not_evaluable', reason: 'No difference can be stated' }} />
          ) : (
            <>
              Mean difference <MetricValue state={{ kind: 'value', value: c.meanDifference }} format={formatR} />.
            </>
          )}{' '}
          <Typography component="span" variant="body2" color="text.secondary">
            {c.note}
          </Typography>
        </li>
      ))}
    </Box>
  );
}
