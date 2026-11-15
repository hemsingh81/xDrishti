import {
  Alert,
  Box,
  Button,
  Chip,
  FormControl,
  FormControlLabel,
  FormLabel,
  MenuItem,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useBasketNames } from '@/features/reports';
import { Typeahead } from '@/shared/ui/Typeahead';
import { simInstrumentSearchQuery, simProblemText, useStartSimDays } from '../../api/simDaysApi';
import { initialSimDaysForm, simDaysProblem, toSimDaysBody, type SimDaysForm } from '../../lib/simDaysForm';

const grid = {
  display: 'grid',
  gap: 1.5,
  gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' },
};

/** Starts a day-by-day simulation. The API's refusals (sim.invalid, sim.busy) are shown as they are worded. */
export function SimDaysStartForm({ onStarted }: { onStarted: (id: string) => void }) {
  const [form, setForm] = useState<SimDaysForm>(initialSimDaysForm);
  const [tried, setTried] = useState(false);
  const [text, setText] = useState('');
  const baskets = useBasketNames();
  const found = useQuery(simInstrumentSearchQuery(text));
  const start = useStartSimDays();
  const set = (patch: Partial<SimDaysForm>) => {
    setForm((current) => ({ ...current, ...patch }));
  };
  const problem = simDaysProblem(form);
  const options = (found.data ?? [])
    .filter((row) => !form.stocks.some((s) => s.id === row.id))
    .map((row) => ({ value: row.id, label: row.symbol, hint: row.name }));
  return (
    <Box
      component="form"
      aria-label="Start a day-by-day simulation"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        setTried(true);
        if (problem) return;
        start.mutate(toSimDaysBody(form), {
          onSuccess: (started) => {
            onStarted(started.id);
          },
        });
      }}
      sx={{ display: 'grid', gap: 2 }}
    >
      <Box sx={grid}>
        <TextField
          size="small"
          type="date"
          label="Start"
          required
          value={form.start}
          onChange={(e) => {
            set({ start: e.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          type="date"
          label="End"
          helperText="Empty: the last complete stored session."
          value={form.end}
          onChange={(e) => {
            set({ end: e.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          label="Capital (₹)"
          inputMode="numeric"
          helperText="Default ₹10,00,000."
          value={form.capital}
          onChange={(e) => {
            set({ capital: e.target.value.replace(/[^0-9.]/g, '') });
          }}
        />
        <TextField
          select
          size="small"
          label="Baskets"
          value={form.basketIds}
          slotProps={{ select: { multiple: true } }}
          onChange={(e) => {
            const value = e.target.value as unknown;
            set({ basketIds: typeof value === 'string' ? value.split(',') : (value as string[]) });
          }}
        >
          {baskets.options.map((basket) => (
            <MenuItem key={basket.id} value={basket.id} sx={{ minHeight: 44 }}>
              {basket.name}
            </MenuItem>
          ))}
        </TextField>
        <Box>
          <Typeahead
            label="Add a stock"
            placeholder="Search by symbol or name"
            options={options}
            value={null}
            onSearch={setText}
            onChange={(id) => {
              const option = options.find((o) => o.value === id);
              if (option) set({ stocks: [...form.stocks, { id: option.value, symbol: option.label }] });
            }}
            loading={found.isFetching}
            noOptionsText={text.trim() === '' ? 'Type a symbol or a name' : 'No stock matches'}
            minWidth={0}
          />
          {form.stocks.length > 0 && (
            <Stack direction="row" aria-label="Chosen stocks" role="list" sx={{ gap: 0.5, flexWrap: 'wrap', mt: 1 }}>
              {form.stocks.map((stock) => (
                <Chip
                  key={stock.id}
                  role="listitem"
                  label={stock.symbol}
                  onDelete={() => {
                    set({ stocks: form.stocks.filter((s) => s.id !== stock.id) });
                  }}
                />
              ))}
            </Stack>
          )}
        </Box>
      </Box>
      <FormControl>
        <FormLabel id="sim-days-mode">Mode</FormLabel>
        <RadioGroup
          row
          aria-labelledby="sim-days-mode"
          value={form.mode}
          onChange={(e) => {
            set({ mode: e.target.value as SimDaysForm['mode'] });
          }}
        >
          <FormControlLabel value="learning" control={<Radio />} label="Learning (learns each evening, default)" />
          <FormControlLabel value="frozen" control={<Radio />} label="Frozen" />
        </RadioGroup>
      </FormControl>
      <Typography variant="body2" color="text.secondary">
        Costs: gross (before costs). Versions are today&apos;s active ones (hindsight). Nothing real is touched.
      </Typography>
      {start.isError && (
        <Alert severity="error" role="alert">
          {simProblemText(start.error, 'The simulation could not be started.')}
        </Alert>
      )}
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
        <Button type="submit" variant="contained" disabled={start.isPending} sx={{ minHeight: 44 }}>
          Start simulation
        </Button>
        {problem && (
          <Typography variant="body2" color={tried ? 'error' : 'text.secondary'} role={tried ? 'alert' : undefined}>
            {problem}
          </Typography>
        )}
      </Stack>
    </Box>
  );
}
