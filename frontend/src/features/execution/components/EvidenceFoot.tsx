import { Box, Typography } from '@mui/material';

/** The basis of the numbers at the foot of every tab (criterion 13): the server's own `limits[]`, never page prose. */
export function EvidenceFoot({ limits, date }: { limits: readonly string[] | undefined; date?: string }) {
  return (
    <Box component="footer" aria-label="Evidence" sx={{ mt: 3, pt: 1.5, borderTop: 1, borderColor: 'divider' }}>
      <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', fontWeight: 600 }}>
        Evidence{date ? `, ${date}` : ''}
      </Typography>
      {limits === undefined ? (
        <Typography variant="caption" sx={{ color: 'text.secondary' }}>
          The basis of these numbers is not available until the overview is read.
        </Typography>
      ) : (
        limits.map((line) => (
          <Typography key={line} variant="caption" sx={{ color: 'text.secondary', display: 'block' }}>
            {line}
          </Typography>
        ))
      )}
    </Box>
  );
}
