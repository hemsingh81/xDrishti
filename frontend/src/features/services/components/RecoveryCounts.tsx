import { Card, CardContent, Grid, Typography } from '@mui/material';
import type { RecoveryCounts as Counts } from '@/shared/api/types';

const tiles = [
  { key: 'missed', label: 'Missed' },
  { key: 'recovered', label: 'Recovered' },
  { key: 'stillFailing', label: 'Still failing' },
] as const;

/** The three headline counts. Missed reads Unknown when the plan could not be read (the count would be a guess). */
export function RecoveryCounts({ counts, missedKnown = true }: { counts: Counts; missedKnown?: boolean }) {
  return (
    <Grid container spacing={2} component="dl" sx={{ m: 0, mb: 2 }}>
      {tiles.map(({ key, label }) => (
        <Grid key={key} size={{ xs: 4 }}>
          <Card variant="outlined">
            <CardContent sx={{ '&:last-child': { pb: 2 } }}>
              <Typography component="dt" variant="body2" color="text.secondary">
                {label}
              </Typography>
              <Typography component="dd" variant="h2" sx={{ m: 0 }}>
                {key === 'missed' && !missedKnown ? 'Unknown' : counts[key]}
              </Typography>
            </CardContent>
          </Card>
        </Grid>
      ))}
    </Grid>
  );
}
