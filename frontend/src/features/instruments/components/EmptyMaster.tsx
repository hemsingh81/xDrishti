import { Card, CardContent, Stack, Typography } from '@mui/material';

/** Shown instead of the list while the master list has never been downloaded. */
export function EmptyMaster() {
  return (
    <Card>
      <CardContent>
        <Stack spacing={0.5} sx={{ alignItems: 'center', textAlign: 'center', py: 4 }}>
          <Typography variant="h2" component="h2">
            No master list yet — refresh it to get started
          </Typography>
          <Typography color="text.secondary" sx={{ maxWidth: 520 }}>
            Use <strong>Refresh master list</strong> above. It downloads Dhan&apos;s instrument file once (a minute or
            so) and fills the list you can then search.
          </Typography>
        </Stack>
      </CardContent>
    </Card>
  );
}
