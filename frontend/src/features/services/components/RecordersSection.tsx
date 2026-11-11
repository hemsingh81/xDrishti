import { Box, Typography } from '@mui/material';
import { RecorderCard } from './RecorderCard';

/** The depth, trade tick, option chain, expired-options backfill and opening-minute bars recorders: the owner's switches, loaded on demand. */
export function RecordersSection() {
  return (
    <Box id="recorders-section" component="section" aria-labelledby="recorders-heading" sx={{ mt: 2 }}>
      <Typography id="recorders-heading" component="h2" variant="h6" sx={{ mb: 1 }}>
        Recorders
      </Typography>
      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' } }}>
        <RecorderCard kind="depth" title="Depth recorder" />
        <RecorderCard kind="ticks" title="Trade tick recorder" />
        <RecorderCard kind="chain" title="Option chain recorder" />
        <RecorderCard kind="backfill" title="Expired-options backfill" />
        <RecorderCard kind="opening" title="Opening-minute bars recorder" />
      </Box>
    </Box>
  );
}
