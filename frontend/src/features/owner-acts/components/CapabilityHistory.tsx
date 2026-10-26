import { List, ListItem, ListItemText, Typography } from '@mui/material';
import type { components } from '@/shared/api/schema';
import { formatIstDateTime } from '@/shared/lib/format';

type Row = components['schemas']['CapabilityHistoryView'];

/** The latest switches of all capabilities, newest first. Nothing here is edited. */
export function CapabilityHistory({ history }: { history: Row[] }) {
  if (history.length === 0) {
    return <Typography color="text.secondary">No switch yet.</Typography>;
  }
  return (
    <List dense disablePadding aria-label="Latest switches, newest first">
      {history.map((row) => (
        <ListItem key={row.id} disableGutters>
          <ListItemText
            primary={`${row.name}: ${row.enabled ? 'On' : 'Off'}`}
            secondary={`${formatIstDateTime(row.at)} by ${row.actor}. ${row.reason}`}
            slotProps={{ secondary: { sx: { overflowWrap: 'anywhere' } } }}
          />
        </ListItem>
      ))}
    </List>
  );
}
