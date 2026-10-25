import CloseIcon from '@mui/icons-material/CloseOutlined';
import FilterListIcon from '@mui/icons-material/FilterListOutlined';
import { Box, Button, Chip, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useState } from 'react';
import type { RunsView } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { activeChips, type FilterPatch, type ReportFilter } from '../lib/reportFilter';
import { FilterControls } from './FilterControls';

interface FilterBarProps {
  runs: RunsView;
  filter: ReportFilter;
  set: (patch: FilterPatch) => void;
}

/**
 * The filter bar. From `md` up the controls are always shown; below it they collapse into one "Filters (n)" button, and the
 * active filters stay visible as chips that remove themselves. The filters change the address, nothing else.
 */
export function FilterBar({ runs, filter, set }: FilterBarProps) {
  const theme = useTheme();
  const small = useMediaQuery(theme.breakpoints.down('md'));
  const [open, setOpen] = useState(false);
  const baskets = useBasketNames();
  const chips = activeChips(filter, baskets.name);
  return (
    <Box component="section" aria-label="Filters" sx={{ display: 'grid', gap: 1.5, mb: 2 }}>
      {small && (
        <Button
          variant="outlined"
          startIcon={<FilterListIcon aria-hidden />}
          aria-expanded={open}
          onClick={() => {
            setOpen((value) => !value);
          }}
          sx={{ minHeight: 44, justifySelf: 'start' }}
        >
          {`Filters (${String(chips.length)})`}
        </Button>
      )}
      {(!small || open) && <FilterControls runs={runs} filter={filter} set={set} />}
      {chips.length > 0 && (
        <Box
          component="ul"
          aria-label="Active filters"
          sx={{ listStyle: 'none', m: 0, p: 0, display: 'flex', flexWrap: 'wrap', gap: 1 }}
        >
          {chips.map((chip) => (
            <li key={chip.id}>
              <Chip
                label={chip.text}
                icon={<CloseIcon aria-hidden />}
                aria-label={`Remove filter ${chip.text}`}
                onClick={() => {
                  set(chip.clear);
                }}
                sx={{ minHeight: { xs: 44, md: 32 } }}
              />
            </li>
          ))}
        </Box>
      )}
    </Box>
  );
}
