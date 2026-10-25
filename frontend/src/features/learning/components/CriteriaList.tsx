import { Box, Typography } from '@mui/material';
import { VerdictChip } from '@/shared/analytics';
import { verdictOf } from '../lib/metricView';

interface Criterion {
  number: number;
  name: string;
  verdict: string;
  detail: string;
  caveat?: string | null;
}

/** The criteria of a phase check as the server judged them: a chip, the detail and the caveat. MET only when the server says MET. */
export function CriteriaList({ label, criteria }: { label: string; criteria: readonly Criterion[] }) {
  return (
    <Box component="ol" aria-label={label} sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 1.5 }}>
      {criteria.map((c) => (
        <li key={c.number}>
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
            <VerdictChip verdict={verdictOf(c.verdict)} />
            <Typography sx={{ fontWeight: 600 }}>
              {c.number}. {c.name}
            </Typography>
          </Box>
          <Typography variant="body2">{c.detail}</Typography>
          {c.caveat && (
            <Typography variant="body2" color="text.secondary">
              Caveat: {c.caveat}
            </Typography>
          )}
        </li>
      ))}
    </Box>
  );
}
