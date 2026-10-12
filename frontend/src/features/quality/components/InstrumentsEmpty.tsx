import { Box, Button, Typography } from '@mui/material';

interface InstrumentsEmptyProps {
  hasActiveFilters: boolean;
  onClear: () => void;
}

/** The table has no rows: nothing matches the filters, or no tracked instrument has bars yet. */
export function InstrumentsEmpty({ hasActiveFilters, onClear }: InstrumentsEmptyProps) {
  return (
    <Box sx={{ p: 4, textAlign: 'center' }}>
      <Typography sx={{ fontWeight: 600 }}>
        {hasActiveFilters ? 'No instruments match these filters.' : 'No tracked instrument has bars to check yet.'}
      </Typography>
      {hasActiveFilters && (
        <Button size="small" onClick={onClear} sx={{ mt: 1 }}>
          Clear filters
        </Button>
      )}
    </Box>
  );
}
