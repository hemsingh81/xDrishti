import { Alert, Box } from '@mui/material';

/** What every paper response says about itself: the basis, and the honest limits. Always on the screen. */
export function BasisNote({ basis, limits }: { basis: string; limits: readonly string[] }) {
  return (
    <Alert severity="info" sx={{ mb: 2 }}>
      {basis}
      {limits.length > 0 && (
        <Box component="ul" aria-label="Limits" sx={{ m: 0, mt: 0.5, pl: 2.5 }}>
          {limits.map((limit) => (
            <li key={limit}>{limit}</li>
          ))}
        </Box>
      )}
    </Alert>
  );
}
