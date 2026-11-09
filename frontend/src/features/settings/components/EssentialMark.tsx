import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import EditIcon from '@mui/icons-material/EditOutlined';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Stack, Typography } from '@mui/material';
import type { EssentialSettingView } from '@/shared/api/types';
import { plainValue } from '../lib/essentials';

/** The mark of a value: the default, changed from it, or not known here. Text and icon, never colour alone. */
export function EssentialMark({ item }: { item: EssentialSettingView }) {
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
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      {item.isDefault ? (
        <CheckCircleIcon fontSize="small" color="success" aria-hidden />
      ) : (
        <EditIcon fontSize="small" color="action" aria-hidden />
      )}
      <Typography variant="body2">
        {plainValue(item.value)} · {item.isDefault ? 'default' : 'changed from default'}
      </Typography>
    </Stack>
  );
}
