import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { Box, Button, Collapse } from '@mui/material';
import { useId, useState } from 'react';
import type { RefreshRun } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { Facts } from '@/shared/ui';
import { typeLabel } from '../lib/labels';

/** The full counts of the last run, folded away by default. */
export function RefreshDetails({ run }: { run: RefreshRun }) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const kept = Object.entries(run.keptByType)
    .map(([type, count]) => `${typeLabel(type)} ${formatNumber(count)}`)
    .join(' · ');

  return (
    <Box>
      <Button
        size="small"
        aria-expanded={open}
        aria-controls={panelId}
        endIcon={<ExpandMoreIcon sx={{ transform: open ? 'rotate(180deg)' : 'none' }} />}
        onClick={() => {
          setOpen((value) => !value);
        }}
      >
        {open ? 'Hide details' : 'Show details'}
      </Button>
      <Collapse in={open} id={panelId} unmountOnExit>
        <Box sx={{ pt: 1 }}>
          <Facts
            items={[
              { label: 'Rows read', value: formatNumber(run.rowsRead) },
              { label: 'Kept by type', value: kept || '—' },
              { label: 'Outside the scope', value: formatNumber(run.skippedByScope) },
              { label: 'Unchanged', value: formatNumber(run.unchanged) },
              {
                label: 'Sector matched',
                value: `${formatNumber(run.sectorMatched)} (${formatNumber(run.sectorUnmatched)} without)`,
              },
              { label: 'F&O underlyings', value: formatNumber(run.fnoUnderlyings) },
              { label: 'Symbol collisions', value: formatNumber(run.collisions) },
              { label: 'Started by', value: run.requestedBy },
            ]}
          />
        </Box>
      </Collapse>
    </Box>
  );
}
