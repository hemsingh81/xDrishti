import { Alert, Stack, Typography } from '@mui/material';
import { Facts } from '@/shared/ui';
import { formatNumber } from '@/shared/lib/format';
import type { Preview } from '../api/experimentApi';

/** What a start would mean, before the owner clicks: every number is the server's. Read only. */
export function PreviewFacts({ preview }: { preview: Preview }) {
  const b = preview.baseline;
  const left = Math.max(0, preview.jobsPerDay - preview.jobsToday);
  return (
    <Stack spacing={1.5} aria-label="What this replay would mean" role="group">
      <Facts
        items={[
          {
            label: 'Baseline',
            value: `${b.strategyId}, ${b.from} to ${b.to}, ${formatNumber(b.instruments)} instruments`,
          },
          { label: 'Costs', value: b.costed ? 'With costs' : 'Gross, without costs' },
          { label: 'Exit', value: b.exit ?? 'Not recorded' },
          { label: 'Next trial', value: `Trial number ${preview.nextTrial}` },
          { label: 'Required improvement', value: `${preview.requiredImprovementR} R per trade` },
          {
            label: 'Folds',
            value: `${preview.foldsAvailable} available, ${preview.minFoldsForBetter} needed to call it better`,
          },
          { label: 'Replay time', value: preview.replayEstimate.text },
          { label: 'Starts left today', value: `${left} of ${preview.jobsPerDay}` },
        ]}
      />
      <Alert role="note" severity={preview.bestPossibleOutcome === 'Inconclusive' ? 'warning' : 'info'}>
        Best possible outcome: {preview.bestPossibleOutcome === 'Inconclusive' ? 'Inconclusive' : 'Better'}.{' '}
        {preview.bestPossibleSentence ?? ''}
      </Alert>
      {preview.limits.length > 0 && (
        <ul style={{ margin: 0, paddingLeft: '1.25rem' }} aria-label="Limits">
          {preview.limits.map((text) => (
            <li key={text}>
              <Typography variant="body2">{text}</Typography>
            </li>
          ))}
        </ul>
      )}
    </Stack>
  );
}
