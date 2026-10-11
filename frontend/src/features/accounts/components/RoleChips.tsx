import { Chip, Stack } from '@mui/material';
import type { AccountSummary } from '@/shared/api';
import { enabledRoles, modeLabel, roleInfo, type RoleKey } from '../lib/labels';

function chipLabel(role: RoleKey, roles: AccountSummary['roles']): string {
  const base = roleInfo[role].label;
  if (role === 'data') {
    return `${base} · ${roles.data.primary ? 'primary' : 'standby'}`;
  }
  if (role === 'trading') {
    return `${base} · ${modeLabel(roles.trading.mode)}`;
  }
  return base;
}

/** One chip per active role (the text says what it is — colour only groups them). */
export function RoleChips({ roles }: { roles: AccountSummary['roles'] }) {
  const active = enabledRoles({ roles });
  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.5 }}>
      {active.length === 0 && <Chip size="small" color="warning" variant="outlined" label="no roles" />}
      {active.map((role) => (
        <Chip
          key={role}
          size="small"
          color={roleInfo[role].chipColor}
          variant="outlined"
          label={chipLabel(role, roles)}
        />
      ))}
    </Stack>
  );
}
