import { Box, MenuItem, Stack, TextField } from '@mui/material';
import { istDayIso } from '@/shared/lib/format';
import type { Fields } from '../lib/complianceForm';
import { staticIpStates } from '../lib/labels';

interface StaticIpFieldsProps {
  fields: Fields;
  errors: Partial<Record<keyof Fields, string>>;
  set: <K extends keyof Fields>(name: K, value: Fields[K]) => void;
}

/** The fields that belong to the static_ip kind only: its state, the provider, the cost, the IP version and the day it was whitelisted (never the address). */
export function StaticIpFields({ fields, errors, set }: StaticIpFieldsProps) {
  const today = istDayIso();
  return (
    <Stack sx={{ gap: 2 }}>
      <TextField
        select
        label="State of the static IP"
        value={fields.staticIpState}
        onChange={(e) => {
          set('staticIpState', e.target.value);
        }}
      >
        {staticIpStates.map((s) => (
          <MenuItem key={s.value} value={s.value}>
            {s.label}
          </MenuItem>
        ))}
      </TextField>
      {fields.staticIpState === 'whitelisted' && (
        <TextField
          label="Whitelisted at Dhan on"
          type="date"
          value={fields.whitelistedAt}
          onChange={(e) => {
            set('whitelistedAt', e.target.value);
          }}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: today } }}
          error={Boolean(errors.whitelistedAt)}
          helperText={
            errors.whitelistedAt ??
            'Dhan locks the IP for 7 days after you set it; the end of the lock is worked out for you.'
          }
        />
      )}
      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '2fr 1fr 1fr' } }}>
        <TextField
          label="Provider (ISP)"
          value={fields.isp}
          onChange={(e) => {
            set('isp', e.target.value);
          }}
          error={Boolean(errors.isp)}
          helperText={errors.isp}
        />
        <TextField
          label="Cost a month (Rs)"
          value={fields.monthlyCost}
          onChange={(e) => {
            set('monthlyCost', e.target.value);
          }}
          slotProps={{ htmlInput: { inputMode: 'decimal' } }}
          error={Boolean(errors.monthlyCost)}
          helperText={errors.monthlyCost}
        />
        <TextField
          select
          label="IP version"
          value={fields.ipVersion}
          onChange={(e) => {
            set('ipVersion', e.target.value);
          }}
        >
          <MenuItem value="">Not stated</MenuItem>
          <MenuItem value="v4">IPv4</MenuItem>
          <MenuItem value="v6">IPv6</MenuItem>
        </TextField>
      </Box>
    </Stack>
  );
}
