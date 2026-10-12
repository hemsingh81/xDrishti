import { Button, Stack, Typography } from '@mui/material';
import type { InstrumentTab } from '../lib/filters';

interface ResultsEmptyProps {
  tab: InstrumentTab;
  hasActiveFilters: boolean;
  onClear: () => void;
  onShowMaster: () => void;
  onPaste: () => void;
}

/** What an empty list says: filters that match nothing, or a Tracked tab with nothing tracked yet. */
export function ResultsEmpty({ tab, hasActiveFilters, onClear, onShowMaster, onPaste }: ResultsEmptyProps) {
  if (tab === 'tracked' && !hasActiveFilters) {
    return (
      <Stack spacing={1} sx={{ alignItems: 'center', py: 5, px: 2, textAlign: 'center' }}>
        <Typography sx={{ fontWeight: 600 }}>No instruments are tracked yet</Typography>
        <Typography color="text.secondary" sx={{ maxWidth: 520 }}>
          Track instruments from the master list, or paste a list of symbols. Only tracked instruments get history,
          scans and analytics. History collection starts with the EOD service (P1-12).
        </Typography>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', justifyContent: 'center' }}>
          <Button variant="contained" onClick={onShowMaster}>
            Browse the master list
          </Button>
          <Button onClick={onPaste}>Paste symbols</Button>
        </Stack>
      </Stack>
    );
  }
  return (
    <Stack spacing={1} sx={{ alignItems: 'center', py: 5 }}>
      <Typography sx={{ fontWeight: 600 }}>
        {tab === 'tracked' ? 'No tracked instruments match these filters' : 'No instruments match these filters'}
      </Typography>
      {hasActiveFilters && <Button onClick={onClear}>Clear filters</Button>}
    </Stack>
  );
}
