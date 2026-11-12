import { Alert, Box, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatDate } from '@/shared/lib/format';
import { replayChargeSheetQuery, replayProfilesQuery, useStartSweep } from '../../api/replayApi';
import {
  initialSweepForm,
  maxSweepStarts,
  minSweepStarts,
  sweepProblem,
  sweepRefusal,
  sweepStarts,
  toSweepBody,
  type SweepForm,
} from '../../lib/sweepForm';

const grid = {
  display: 'grid',
  gap: 1.5,
  gridTemplateColumns: { xs: 'minmax(0, 1fr)', sm: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(3, minmax(0, 1fr))' },
};

/**
 * Starts a start-date sweep: 2 to 12 starts that share one end date, frozen mode, the auto scope. Each start is one more replay
 * look in the trial ledger; the form says so before anything is sent.
 */
export function SweepStartForm({ onStarted }: { onStarted: (sweepId: string) => void }) {
  const [form, setForm] = useState<SweepForm>(initialSweepForm);
  const profiles = useQuery(replayProfilesQuery);
  const sheet = useQuery(replayChargeSheetQuery);
  const start = useStartSweep();
  const set = (patch: Partial<SweepForm>) => {
    setForm((current) => ({ ...current, ...patch }));
  };
  const problem = sweepProblem(form);
  const starts = problem ? [] : sweepStarts(form);
  const sheetId = sheet.data?.sheet?.id;
  return (
    <Box
      component="form"
      aria-label="Start a sweep"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        if (problem) return;
        start.mutate(toSweepBody(form), {
          onSuccess: (started) => {
            if (started.sweepId) onStarted(started.sweepId);
          },
        });
      }}
      sx={{ display: 'grid', gap: 2 }}
    >
      <Typography variant="body2" color="text.secondary">
        Runs the same replay from several start dates that share one end date, one after another, to see whether the
        answer depends on the start date. Frozen mode, the selection arm, the auto scope and today&apos;s active
        versions.
      </Typography>
      <Box sx={grid}>
        <TextField
          select
          size="small"
          label="Start dates"
          value={form.mode}
          onChange={(e) => {
            set({ mode: e.target.value as SweepForm['mode'] });
          }}
        >
          <MenuItem value="quarters" sx={{ minHeight: 44 }}>
            Every quarter × n
          </MenuItem>
          <MenuItem value="dates" sx={{ minHeight: 44 }}>
            Explicit dates
          </MenuItem>
        </TextField>
        {form.mode === 'quarters' ? (
          <>
            <TextField
              size="small"
              type="date"
              label="First start"
              required
              value={form.first}
              onChange={(e) => {
                set({ first: e.target.value });
              }}
              slotProps={{ inputLabel: { shrink: true } }}
            />
            <TextField
              size="small"
              label="Number of starts (n)"
              inputMode="numeric"
              value={form.count}
              helperText={`${String(minSweepStarts)} to ${String(maxSweepStarts)}.`}
              onChange={(e) => {
                set({ count: e.target.value.replace(/[^0-9]/g, '') });
              }}
            />
          </>
        ) : (
          <TextField
            size="small"
            label="Dates (yyyy-mm-dd)"
            multiline
            minRows={2}
            required
            value={form.dates}
            helperText="Separate them with commas or new lines."
            onChange={(e) => {
              set({ dates: e.target.value });
            }}
            sx={{ gridColumn: { sm: 'span 2' } }}
          />
        )}
        <TextField
          size="small"
          type="date"
          label="Shared end date"
          helperText="Empty: the last allowed session."
          value={form.to}
          onChange={(e) => {
            set({ to: e.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
        <TextField
          size="small"
          label="Sweep capital (₹)"
          inputMode="numeric"
          value={form.capital}
          onChange={(e) => {
            set({ capital: e.target.value.replace(/[^0-9.]/g, '') });
          }}
        />
        <TextField
          select
          size="small"
          label="Sweep risk profile"
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
          label="Sweep costs"
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
      </Box>
      {starts.length > 0 && (
        <Alert severity="warning" role="note">
          {`This sweep adds ${String(starts.length)} replay looks (one for each start: ${starts.map(formatDate).join(', ')}). They are counted in the trial ledger and cannot be taken back.`}
        </Alert>
      )}
      {start.isError && (
        <Alert severity="error" role="alert">
          {sweepRefusal(start.error)}
        </Alert>
      )}
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
        <Button type="submit" variant="contained" disabled={start.isPending} sx={{ minHeight: 44 }}>
          Start sweep
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
