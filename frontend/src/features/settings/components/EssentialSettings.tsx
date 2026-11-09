import { List, Typography } from '@mui/material';
import { ErrorState, LoadingState } from '@/shared/ui';
import { essentialsQuery } from '../api/essentialsApi';
import { essentialSettings } from '../lib/essentials';
import { EssentialRow } from './EssentialRow';
import { useQuery } from '@tanstack/react-query';

/** The five settings that matter, in plain words. A setting whose change is not offered here yet shows no control. */
export function EssentialSettings() {
  const essentials = useQuery(essentialsQuery);
  if (essentials.isPending) return <LoadingState label="Loading your settings" />;
  if (essentials.isError) return <ErrorState error={essentials.error} />;

  const rows = essentialSettings.flatMap((row) => {
    const item = essentials.data.items.find((i) => i.key === row.key);
    return item ? [{ row, item }] : [];
  });
  if (rows.length === 0) return <Typography>No settings to show yet.</Typography>;

  return (
    <List aria-label="Essential settings" disablePadding sx={{ mb: 2.5 }}>
      {rows.map(({ row, item }) => (
        <EssentialRow key={row.key} row={row} item={item} />
      ))}
    </List>
  );
}
