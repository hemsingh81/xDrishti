import { List, ListItem, ListItemText, Typography } from '@mui/material';
import type { ChargeSheetStatusView } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { kindWords } from '../lib/chargeSheetWords';

/** Every act, newest first. A revoke is a row of its own; nothing here is edited. */
export function ChargeSheetHistory({ history }: { history: ChargeSheetStatusView['history'] }) {
  if (history.length === 0) {
    return <Typography color="text.secondary">No act yet.</Typography>;
  }
  return (
    <List dense disablePadding aria-label="Charge sheet acts, newest first">
      {history.map((row) => (
        <ListItem key={row.id} disableGutters>
          <ListItemText
            primary={`${kindWords[row.kind] ?? row.kind}${row.sheetId ? `: ${row.sheetId}` : ''}`}
            secondary={`${formatIstDateTime(row.at)} by ${row.actor}. ${row.reason}`}
            slotProps={{ secondary: { sx: { overflowWrap: 'anywhere' } } }}
          />
        </ListItem>
      ))}
    </List>
  );
}
