import { Box, Typography } from '@mui/material';

/** One figure of a `dl`: its label above and the value below (a number or an amount, already formatted). */
export function Figure({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" component="dt">
        {label}
      </Typography>
      <Typography component="dd" className="numeric" sx={{ m: 0, fontWeight: 600 }}>
        {value}
      </Typography>
    </Box>
  );
}
