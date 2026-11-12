import {
  Alert,
  Box,
  Button,
  Checkbox,
  FormControl,
  FormControlLabel,
  FormGroup,
  FormHelperText,
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
import { replayChargeSheetQuery, replayProfilesQuery, useStartReplay } from '../../api/replayApi';
import { formProblem, initialForm, refusalText, toBody, type ReplayForm, type ReplayModes } from '../../lib/replayForm';

const grid = {
  display: 'grid',
  gap: 1.5,
  gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' },
};

/** Starts a replay. The refusals of the API (replay.invalid, replay.busy, replay.holdout) are shown as they are worded. */
export function ReplayStartForm({ onStarted }: { onStarted: (runId: string) => void }) {
  const [form, setForm] = useState<ReplayForm>(initialForm);
  const baskets = useBasketNames();
  const profiles = useQuery(replayProfilesQuery);
  const sheet = useQuery(replayChargeSheetQuery);
  const start = useStartReplay();
  const set = (patch: Partial<ReplayForm>) => {
    setForm((current) => ({ ...current, ...patch }));
  };
  const problem = formProblem(form);
  const sheetId = sheet.data?.sheet?.id;
  return (
    <Box
      component="form"
      aria-label="Start a replay"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        if (problem) return;
        start.mutate(toBody(form), {
          onSuccess: (started) => {
            const first = started.runIds[0];
            if (first) onStarted(first);
          },
        });
      }}
      sx={{ display: 'grid', gap: 2 }}
    >
      <Box sx={grid}>
        <TextField
          size="small"
          type="date"
          label="From"
          required
          value={form.from}
          onChange={(e) => {
            set({ from: e.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          type="date"
          label="To"
          helperText="Empty: the last allowed session."
          value={form.to}
          onChange={(e) => {
            set({ to: e.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          label="Capital (₹)"
          inputMode="numeric"
          value={form.capital}
          onChange={(e) => {
            set({ capital: e.target.value.replace(/[^0-9.]/g, '') });
          }}
        />
        <TextField
          select
          size="small"
          label="Scope"
          value={form.scope}
          onChange={(e) => {
            set({ scope: e.target.value as ReplayForm['scope'] });
          }}
        >
          <MenuItem value="auto" sx={{ minHeight: 44 }}>
            The auto scope (baskets as of the start date)
          </MenuItem>
          <MenuItem value="baskets" sx={{ minHeight: 44 }}>
            Chosen baskets
          </MenuItem>
        </TextField>
        {form.scope === 'baskets' && (
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
        )}
        <TextField
          select
          size="small"
          label="Risk profile"
          value={form.riskProfileId}
          onChange={(e) => {
            set({ riskProfileId: e.target.value });
          }}
        >
          <MenuItem value="" sx={{ minHeight: 44 }}>
            The standard profile
          </MenuItem>
          {(profiles.data ?? []).map((profile) => (
            <MenuItem key={profile.id} value={profile.id} sx={{ minHeight: 44 }}>
              {profile.name}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          select
          size="small"
          label="Costs"
          value={form.costs}
          onChange={(e) => {
            set({ costs: e.target.value });
          }}
        >
          <MenuItem value="gross" sx={{ minHeight: 44 }}>
            Gross (before costs)
          </MenuItem>
          {sheetId && (
            <MenuItem value={sheetId} sx={{ minHeight: 44 }}>
              {`Charge sheet ${sheet.data?.sheet?.label ?? sheetId}`}
            </MenuItem>
          )}
        </TextField>
        <TextField
          select
          size="small"
          label="Versions rule"
          value={form.versions}
          onChange={(e) => {
            set({ versions: e.target.value as ReplayForm['versions'] });
          }}
        >
          <MenuItem value="start" sx={{ minHeight: 44 }}>
            Today&apos;s active versions (hindsight, default)
          </MenuItem>
          <MenuItem value="asOf" sx={{ minHeight: 44 }}>
            As of each day
          </MenuItem>
        </TextField>
      </Box>
      <FormControl>
        <FormLabel id="replay-modes">Modes</FormLabel>
        <RadioGroup
          row
          aria-labelledby="replay-modes"
          value={form.modes}
          onChange={(e) => {
            set({ modes: e.target.value as ReplayModes });
          }}
        >
          <FormControlLabel value="frozen" control={<Radio />} label="Frozen" />
          <FormControlLabel value="learning" control={<Radio />} label="Learning" />
          <FormControlLabel value="both" control={<Radio />} label="Both (compared)" />
        </RadioGroup>
      </FormControl>
      <FormGroup>
        <FormControlLabel
          control={
            <Checkbox
              checked={form.filtersWithoutHistory}
              onChange={(e) => {
                set({ filtersWithoutHistory: e.target.checked });
              }}
            />
          }
          label="Skip event / F&O-ban / surveillance filters where no history exists (adds a hindsight label)"
        />
        <FormHelperText sx={{ mt: -0.5, ml: 4 }}>
          Adds a hindsight label: filters that have no stored history are applied as they are today.
        </FormHelperText>
        <FormControlLabel
          control={
            <Checkbox
              checked={form.includeResearchCells}
              onChange={(e) => {
                set({ includeResearchCells: e.target.checked });
              }}
            />
          }
          label="Let research-stage cells trade (adds a hindsight label)"
        />
        <FormHelperText sx={{ mt: -0.5, ml: 4 }}>
          Adds a hindsight label: research cells are included, though they were not live then.
        </FormHelperText>
      </FormGroup>
      {start.isError && (
        <Alert severity="error" role="alert">
          {refusalText(start.error)}
        </Alert>
      )}
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
        <Button type="submit" variant="contained" disabled={start.isPending} sx={{ minHeight: 44 }}>
          Start replay
        </Button>
        {problem && (
          <Typography variant="body2" color="text.secondary">
            {problem}
          </Typography>
        )}
      </Stack>
    </Box>
  );
}
