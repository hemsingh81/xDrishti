import { Box, Button, ButtonGroup, MenuItem, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type SyntheticEvent } from 'react';
import { dimensionReasonQuery, type RunsView } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { dataEndOf, datePresets, presetFrom } from '../lib/datePresets';
import { joined, sides, timeframes, type FilterPatch, type ReportFilter } from '../lib/reportFilter';

interface ControlsProps {
  runs: RunsView;
  filter: ReportFilter;
  set: (patch: FilterPatch) => void;
}

const distinct = (values: readonly (string | null)[]): string[] =>
  [...new Set(values.filter((value): value is string => value !== null))].sort();

interface ChoiceProps {
  label: string;
  value: string | null;
  options: readonly string[];
  onChange: (value: string | null) => void;
}

function Choice({ label, value, options, onChange }: ChoiceProps) {
  return (
    <TextField
      select
      size="small"
      label={label}
      value={value ?? ''}
      onChange={(event) => {
        onChange(event.target.value === '' ? null : event.target.value);
      }}
      slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
    >
      <option value="">Any</option>
      {options.map((option) => (
        <option key={option} value={option}>
          {option}
        </option>
      ))}
    </TextField>
  );
}

/** Comma separated symbols, applied on Enter or when the field is left; upper-cased because the server matches exactly. */
function SymbolField({ value, onApply }: { value: string; onApply: (symbols: string[]) => void }) {
  const [text, setText] = useState(value);
  const apply = (event?: SyntheticEvent) => {
    event?.preventDefault();
    onApply(
      text
        .split(',')
        .map((item) => item.trim().toUpperCase())
        .filter((item) => item !== ''),
    );
  };
  return (
    <Box component="form" onSubmit={apply} noValidate>
      <TextField
        size="small"
        label="Symbols"
        placeholder="TCS, INFY"
        value={text}
        onChange={(event) => {
          setText(event.target.value.toUpperCase());
        }}
        onBlur={() => {
          apply();
        }}
        slotProps={{ inputLabel: { shrink: true } }}
        fullWidth
      />
    </Box>
  );
}

function DisabledDimension({ label, dimension }: { label: string; dimension: 'regime' | 'account' }) {
  const reason = useQuery(dimensionReasonQuery(dimension));
  return (
    <TextField
      size="small"
      disabled
      label={label}
      value=""
      helperText={reason.data ?? (reason.isError ? 'Not available.' : 'Reading the reason…')}
      slotProps={{ inputLabel: { shrink: true } }}
    />
  );
}

/** The filter controls: date presets from the end of the data, baskets, strategy, style, exit, timeframe, side, symbols, basis. */
export function FilterControls({ runs, filter, set }: ControlsProps) {
  const baskets = useBasketNames();
  const dataEnd = dataEndOf(runs.runs.map((run) => run.windowEnd));
  return (
    <Box
      sx={{
        display: 'grid',
        gap: 1.5,
        gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' },
        '& .MuiButton-root, & .MuiInputBase-root': { minHeight: { xs: 44, md: 40 } },
      }}
    >
      <ButtonGroup aria-label="Date range, measured back from the end of the data" sx={{ gridColumn: '1 / -1' }}>
        {datePresets.map((preset) => {
          const from = dataEnd ? presetFrom(dataEnd, preset.days) : null;
          const pressed = preset.days === null ? !filter.from && !filter.to : filter.from === from && !filter.to;
          return (
            <Button
              key={preset.id}
              variant={pressed ? 'contained' : 'outlined'}
              aria-pressed={pressed}
              disabled={!dataEnd}
              onClick={() => {
                set({ from, to: null });
              }}
            >
              {preset.label}
            </Button>
          );
        })}
      </ButtonGroup>
      <TextField
        select
        size="small"
        label="Baskets"
        value={[...filter.basket]}
        onChange={(event) => {
          const picked = event.target.value as unknown as string[];
          set({ basket: joined(picked) });
        }}
        slotProps={{
          select: {
            multiple: true,
            displayEmpty: true,
            renderValue: (selected) => {
              const ids = selected as string[];
              return ids.length === 0 ? 'Any' : ids.map(baskets.name).join(', ');
            },
          },
          inputLabel: { shrink: true },
        }}
      >
        {baskets.options.map((basket) => (
          <MenuItem key={basket.id} value={basket.id}>
            {basket.name}
          </MenuItem>
        ))}
      </TextField>
      <Choice
        label="Strategy"
        value={filter.strategy}
        options={distinct(runs.runs.map((run) => run.strategyId))}
        onChange={(value) => {
          set({ strategy: value });
        }}
      />
      <Choice
        label="Style"
        value={filter.style}
        options={distinct(runs.runs.map((run) => run.style))}
        onChange={(value) => {
          set({ style: value });
        }}
      />
      <Choice
        label="Exit"
        value={filter.exit}
        options={distinct(runs.runs.map((run) => run.exit))}
        onChange={(value) => {
          set({ exit: value });
        }}
      />
      <Choice
        label="Timeframe"
        value={filter.timeframe}
        options={timeframes}
        onChange={(value) => {
          set({ timeframe: value });
        }}
      />
      <Choice
        label="Side"
        value={filter.side}
        options={sides}
        onChange={(value) => {
          set({ side: value });
        }}
      />
      <SymbolField
        key={filter.symbol.join(',')}
        value={filter.symbol.join(', ')}
        onApply={(symbols) => {
          set({ symbol: joined(symbols) });
        }}
      />
      <TextField
        select
        size="small"
        label="Basis"
        value={filter.basis}
        onChange={(event) => {
          set({ basis: event.target.value === 'net' ? 'net' : null });
        }}
        slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
      >
        <option value="gross">Gross R</option>
        <option value="net">Net R</option>
      </TextField>
      <DisabledDimension label="Regime" dimension="regime" />
      <DisabledDimension label="Account" dimension="account" />
    </Box>
  );
}
