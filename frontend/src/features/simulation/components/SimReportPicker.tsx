import { Box, MenuItem, Stack, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { trainingReportTypes, useBasketNames, type TrainingReportType } from '@/features/reports';
import { choiceProblem, type SimChoice } from '../lib/choice';

interface Props {
  value: SimChoice;
  onChange: (next: SimChoice) => void;
}

/** Stock or basket, a date range and the report type. Everything is validated before a request leaves. */
export function SimReportPicker({ value, onChange }: Props) {
  const baskets = useBasketNames();
  const set = (patch: Partial<SimChoice>) => {
    onChange({ ...value, ...patch });
  };
  const problem = choiceProblem(value);
  return (
    <Stack sx={{ gap: 1.5 }}>
      <ToggleButtonGroup
        exclusive
        size="small"
        aria-label="Scope"
        value={value.scope}
        onChange={(_, scope: SimChoice['scope'] | null) => {
          if (scope) set({ scope });
        }}
      >
        <ToggleButton value="all" sx={{ minHeight: 44 }}>
          Everything
        </ToggleButton>
        <ToggleButton value="stock" sx={{ minHeight: 44 }}>
          Stock
        </ToggleButton>
        <ToggleButton value="basket" sx={{ minHeight: 44 }}>
          Basket
        </ToggleButton>
      </ToggleButtonGroup>
      <Box
        sx={{
          display: 'grid',
          gap: 1.5,
          gridTemplateColumns: {
            xs: 'minmax(0, 1fr)',
            sm: 'repeat(2, minmax(0, 1fr))',
            lg: 'repeat(4, minmax(0, 1fr))',
          },
        }}
      >
        {value.scope === 'stock' && (
          <TextField
            size="small"
            label="Stock symbol"
            value={value.symbol}
            onChange={(event) => {
              set({ symbol: event.target.value.trim().toUpperCase() });
            }}
          />
        )}
        {value.scope === 'basket' && (
          <TextField
            select
            size="small"
            label="Basket"
            value={value.basket}
            onChange={(event) => {
              set({ basket: event.target.value });
            }}
          >
            {baskets.options.map((basket) => (
              <MenuItem key={basket.id} value={basket.id} sx={{ minHeight: 44 }}>
                {basket.name}
              </MenuItem>
            ))}
          </TextField>
        )}
        <TextField
          size="small"
          type="date"
          label="From"
          value={value.from}
          onChange={(event) => {
            set({ from: event.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          type="date"
          label="To"
          value={value.to}
          onChange={(event) => {
            set({ to: event.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          select
          size="small"
          label="Report"
          value={value.type}
          onChange={(event) => {
            set({ type: event.target.value as TrainingReportType });
          }}
        >
          {trainingReportTypes.map((type) => (
            <MenuItem key={type.id} value={type.id} sx={{ minHeight: 44 }}>
              {type.label}
            </MenuItem>
          ))}
        </TextField>
      </Box>
      {problem && (
        <Box role="alert" sx={{ color: 'error.main', fontSize: '0.875rem' }}>
          {problem}
        </Box>
      )}
    </Stack>
  );
}
