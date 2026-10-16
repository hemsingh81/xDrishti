import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Box, Button, Collapse, Typography } from '@mui/material';
import { useId, useState } from 'react';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import type { DropsSummary } from '../../lib/dropReasons';

/**
 * "Details" under a sync line that dropped rows: where the first few were (window, page, row), so the owner can look them up
 * in Dhan's own statement. Locations and reasons only; the API never sends a row's content, and the note says so.
 */
export function BrokerDropDetails({ name, drops }: { name: string; drops: DropsSummary }) {
  const [open, setOpen] = useState(false);
  const listId = useId();
  if (drops.locations.length === 0) return null;
  const partial = drops.total > drops.locations.length;
  return (
    <Box sx={{ pl: 3.5 }}>
      <Button
        size="small"
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        aria-label={`Details of the ${name} rows that could not be read`}
        endIcon={open ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        onClick={() => {
          setOpen((was) => !was);
        }}
      >
        Details
      </Button>
      <Collapse in={open} unmountOnExit id={listId}>
        <Box component="ul" sx={{ m: 0, mt: 0.5, pl: 2.5 }}>
          {drops.locations.map((place, index) => (
            <Typography key={index} component="li" variant="body2" sx={numeric}>
              {place}
            </Typography>
          ))}
        </Box>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
          {partial ? `The first ${formatNumber(drops.locations.length)} of ${formatNumber(drops.total)} rows. ` : ''}
          Only where a row is and why it was left out are shown here, never its names or amounts.
        </Typography>
      </Collapse>
    </Box>
  );
}
