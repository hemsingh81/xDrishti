import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import EditIcon from '@mui/icons-material/EditOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Box, Link, List, ListItem, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useSettingsCatalog } from '../api/settingsCatalogQuery';
import { essentialSettings, plainValue } from '../lib/essentials';

interface Item {
  label: string;
  value: string | null;
  default: string | null;
}

/** The mark of a value: the default, changed from it, or not known here. Text and icon, never colour alone. */
function Mark({ item }: { item: Item }) {
  if (item.value === null) {
    return (
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
        <InfoIcon fontSize="small" color="action" aria-hidden />
        <Typography variant="body2">
          Not changeable here yet{item.default === null ? '' : ` · starts as ${plainValue(item.default)}`}
        </Typography>
      </Stack>
    );
  }
  const changed = item.value !== item.default;
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      {changed ? (
        <EditIcon fontSize="small" color="action" aria-hidden />
      ) : (
        <CheckCircleIcon fontSize="small" color="success" aria-hidden />
      )}
      <Typography variant="body2">
        {plainValue(item.value)} · {changed ? 'changed from default' : 'default'}
      </Typography>
    </Stack>
  );
}

/** The five settings that matter, in plain words. A setting whose change is not offered here yet shows no control. */
export function EssentialSettings() {
  const catalog = useSettingsCatalog();
  if (catalog.isPending) return <LoadingState label="Loading your settings" />;
  if (catalog.isError) return <ErrorState error={catalog.error} />;

  const rows = essentialSettings.flatMap((row) => {
    const item = catalog.data.items.find((i) => i.key === row.key);
    return item ? [{ row, item }] : [];
  });
  if (rows.length === 0) return <Typography>No settings to show yet.</Typography>;

  return (
    <List aria-label="Essential settings" disablePadding sx={{ mb: 2.5 }}>
      {rows.map(({ row, item }) => (
        <ListItem key={row.key} divider sx={{ display: 'block', py: 1.5 }}>
          <Box sx={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', gap: 1 }}>
            <Typography component="h2" variant="subtitle1">
              {item.label}
            </Typography>
            {'to' in row ? (
              <Link component={RouterLink} to={row.to}>
                {row.linkLabel}
              </Link>
            ) : (
              <Mark item={item} />
            )}
          </Box>
          <Typography variant="body2" color="text.secondary">
            {row.help}
          </Typography>
        </ListItem>
      ))}
    </List>
  );
}
