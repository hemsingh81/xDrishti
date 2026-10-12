import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Stack, Typography } from '@mui/material';

/** What a job noted on the way (for example the other files whose bars an undo removed); nothing when it noted nothing. */
export function JobWarnings({ warnings }: { warnings: readonly string[] }) {
  if (warnings.length === 0) return null;
  return (
    <Stack direction="row" sx={{ gap: 0.75, alignItems: 'flex-start', mt: 0.5 }}>
      <WarningIcon fontSize="small" color="warning" aria-hidden />
      <Box component="ul" aria-label="Warnings" sx={{ m: 0, p: 0, listStyle: 'none' }}>
        {warnings.map((warning) => (
          <Typography key={warning} component="li" variant="body2" sx={{ overflowWrap: 'anywhere' }}>
            {warning}
          </Typography>
        ))}
      </Box>
    </Stack>
  );
}
