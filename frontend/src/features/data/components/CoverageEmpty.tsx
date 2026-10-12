import { Box, Button, Typography } from '@mui/material';

interface CoverageEmptyProps {
  hasActiveFilters: boolean;
  onClear: () => void;
}

/** The coverage table has no rows: nothing matches the filters, or no instrument has data yet. */
export function CoverageEmpty({ hasActiveFilters, onClear }: CoverageEmptyProps) {
  return (
    <Box sx={{ p: 4, textAlign: 'center' }}>
      <Typography sx={{ fontWeight: 600 }}>
        {hasActiveFilters ? 'No instruments match these filters.' : 'No instruments with data yet.'}
      </Typography>
      {hasActiveFilters && (
        <Button size="small" onClick={onClear} sx={{ mt: 1 }}>
          Clear filters
        </Button>
      )}
    </Box>
  );
}
