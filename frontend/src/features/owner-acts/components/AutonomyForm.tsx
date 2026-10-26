import { Alert, Box, Button, FormControlLabel, Stack, Switch, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { ApiError } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { useAutonomyAct, type AutonomyRecord } from '../api/movesApi';
import { ActForm } from './ActForm';

const onOff = (on: boolean) => (on ? 'ON' : 'OFF (manual approval)');

interface Values {
  autoApproveTickets: boolean;
  promoteFromReserve: boolean;
  minPWin: string;
  maxAutoPerDay: string;
}

const valuesOf = (s: {
  autoApproveTickets: boolean;
  promoteFromReserve: boolean;
  minPWin: number;
  maxAutoPerDay: number;
}): Values => ({
  autoApproveTickets: s.autoApproveTickets,
  promoteFromReserve: s.promoteFromReserve,
  minPWin: String(s.minPWin),
  maxAutoPerDay: String(s.maxAutoPerDay),
});

/**
 * The autonomy switches as a form over the existing PUT (ADR 0091). It is loaded at one settings row and sends that row's id: another change meanwhile is a 409 and nothing is written.
 * Every change is a new row with a reason and a fresh password; re-using an old row only fills the fields, it is never an edit. No setting places an order.
 */
export function AutonomyForm({ settings, onReload }: { settings: AutonomyRecord; onReload: () => void }) {
  const act = useAutonomyAct();
  const [values, setValues] = useState<Values>(valuesOf(settings));
  const [stale, setStale] = useState(false);
  const min = Number(values.minPWin);
  const cap = Number(values.maxAutoPerDay);
  const valid = Number.isFinite(min) && min >= 0.5 && min <= 0.99 && Number.isInteger(cap) && cap >= 1 && cap <= 20;
  const set = <K extends keyof Values>(key: K, value: Values[K]) => {
    setValues((v) => ({ ...v, [key]: value }));
  };

  return (
    <Stack spacing={2}>
      <Typography>
        Now: automatic approval <strong>{onOff(settings.autoApproveTickets)}</strong>, reserve promotion{' '}
        <strong>{onOff(settings.promoteFromReserve)}</strong>, minimum win-rate bound {settings.minPWin}, daily cap{' '}
        {settings.maxAutoPerDay}.
      </Typography>
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        No setting lets anything place an order; an automatic approval only makes a ticket Approved.
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {settings.notice}
      </Typography>
      <ActForm
        label="Change the autonomy settings"
        consequence="Every change is a new row with your reason; the old rows stay. Turning a switch ON allows a rule to approve a ticket that meets every term; nothing here places an order."
        submitLabel="Save as a new row"
        fieldsReady={valid}
        run={(reason, password) =>
          act({
            autoApproveTickets: values.autoApproveTickets,
            promoteFromReserve: values.promoteFromReserve,
            minPWin: min,
            maxAutoPerDay: cap,
            expectedSettingsId: settings.id ?? 0,
            reason,
            password,
          })
        }
        done={(view) =>
          `Saved as settings row ${view.id}. Automatic approval is ${onOff(view.autoApproveTickets)}, reserve promotion ${onOff(view.promoteFromReserve)}.`
        }
        onRefused={(error) => {
          setStale(error instanceof ApiError && error.problem?.code === 'autonomy.stale');
        }}
      >
        <FormControlLabel
          control={
            <Switch
              checked={values.autoApproveTickets}
              onChange={(event) => {
                set('autoApproveTickets', event.target.checked);
              }}
            />
          }
          label={`Automatic approval: ${onOff(values.autoApproveTickets)}`}
        />
        <FormControlLabel
          control={
            <Switch
              checked={values.promoteFromReserve}
              onChange={(event) => {
                set('promoteFromReserve', event.target.checked);
              }}
            />
          }
          label={`Reserve promotion: ${onOff(values.promoteFromReserve)}`}
        />
        <TextField
          label="Minimum win-rate bound"
          type="number"
          value={values.minPWin}
          onChange={(event) => {
            set('minPWin', event.target.value);
          }}
          error={values.minPWin !== '' && !(min >= 0.5 && min <= 0.99)}
          helperText="0.5 to 0.99"
          slotProps={{ htmlInput: { min: 0.5, max: 0.99, step: 0.01 } }}
        />
        <TextField
          label="Daily cap of automatic approvals"
          type="number"
          value={values.maxAutoPerDay}
          onChange={(event) => {
            set('maxAutoPerDay', event.target.value);
          }}
          error={values.maxAutoPerDay !== '' && !(Number.isInteger(cap) && cap >= 1 && cap <= 20)}
          helperText="1 to 20"
          slotProps={{ htmlInput: { min: 1, max: 20, step: 1 } }}
        />
      </ActForm>
      {stale && (
        <Alert severity="warning" action={<Button onClick={onReload}>Read the settings again</Button>}>
          The settings were changed since this form was loaded. Read them again, then decide again.
        </Alert>
      )}
      {settings.history.length > 0 && (
        <Box component="section" aria-label="Earlier settings rows">
          <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
            Earlier rows
          </Typography>
          <Stack component="ul" spacing={1} sx={{ listStyle: 'none', m: 0, p: 0 }}>
            {settings.history.slice(0, 5).map((row) => (
              <Stack
                key={row.id}
                component="li"
                direction="row"
                spacing={1}
                sx={{ alignItems: 'center', flexWrap: 'wrap' }}
              >
                <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
                  {formatIstDateTime(row.changedAt)}: automatic approval {onOff(row.autoApproveTickets)}, reserve{' '}
                  {onOff(row.promoteFromReserve)}, bound {row.minPWin}, cap {row.maxAutoPerDay}. {row.reason}
                </Typography>
                <Button
                  size="small"
                  type="button"
                  onClick={() => {
                    setValues(valuesOf(row));
                  }}
                  aria-label={`Use the values of row ${row.id}`}
                >
                  Use these values
                </Button>
              </Stack>
            ))}
          </Stack>
        </Box>
      )}
    </Stack>
  );
}
