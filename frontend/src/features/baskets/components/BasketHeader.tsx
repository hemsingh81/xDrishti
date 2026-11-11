import ArchiveIcon from '@mui/icons-material/ArchiveOutlined';
import EditIcon from '@mui/icons-material/EditOutlined';
import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import LockIcon from '@mui/icons-material/LockOutlined';
import { Button, Chip, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { Basket } from '@/shared/api';
import { AsOfField } from './AsOfField';
import { isStored } from '../lib/system';
import { BasketDot } from './BasketDot';

interface BasketHeaderProps {
  basket: Basket;
  asOf: string | null;
  onAsOfChange: (value: string | null) => void;
  onEdit: () => void;
  onHistory: () => void;
  onArchive: () => void;
}

export const portfolioNote =
  "Portfolio — follows the account's holdings; read only. Copy instruments to one of your baskets.";
const systemNote = (kind: string) =>
  `System ${kind} basket: kept by the system and read only. Copy instruments to one of your baskets.`;
export const manualNote = 'Manual — you add and remove instruments.';

/**
 * The title row of the members card (prototype `.card-h`): colour dot and name on the left, the actions on the right
 * (View as of, History, Edit, Archive), then one muted line saying what kind of basket it is. A portfolio basket has
 * no actions, only the "Portfolio · read only" chip.
 */
export function BasketHeader({ basket, asOf, onAsOfChange, onEdit, onHistory, onArchive }: BasketHeaderProps) {
  const system = basket.system;
  const stored = isStored(basket);
  return (
    <Stack spacing={0.75} sx={{ px: 2, pt: 1.5, pb: 1.25 }}>
      <Stack
        direction="row"
        sx={{ gap: 1.5, rowGap: 1, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}
      >
        <Stack direction="row" sx={{ gap: 1, alignItems: 'center', minWidth: 0 }}>
          <BasketDot colour={basket.colour} size={12} />
          <Typography variant="h3" component="h2" sx={{ overflowWrap: 'anywhere' }}>
            {basket.name}
          </Typography>
          <Button component={RouterLink} to={`/baskets/${basket.id}/profile`} size="small" variant="outlined">
            Performance
          </Button>
        </Stack>
        {system && !stored && (
          <Chip size="small" variant="outlined" icon={<LockIcon />} label="Portfolio · read only" />
        )}
        {stored && (
          <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
            <AsOfField value={asOf} onChange={onAsOfChange} />
            <Button size="small" variant="outlined" startIcon={<HistoryIcon />} onClick={onHistory}>
              History
            </Button>
            <Chip size="small" variant="outlined" icon={<LockIcon />} label={`${basket.kind} · read only`} />
          </Stack>
        )}
        {!system && (
          <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
            <AsOfField value={asOf} onChange={onAsOfChange} />
            <Button size="small" variant="outlined" startIcon={<HistoryIcon />} onClick={onHistory}>
              History
            </Button>
            <Button size="small" variant="outlined" startIcon={<EditIcon />} onClick={onEdit}>
              Edit
            </Button>
            <Button size="small" variant="outlined" color="error" startIcon={<ArchiveIcon />} onClick={onArchive}>
              Archive
            </Button>
          </Stack>
        )}
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
        {stored
          ? basket.description?.trim()
            ? basket.description
            : systemNote(basket.kind)
          : system
            ? portfolioNote
            : basket.description?.trim()
              ? basket.description
              : manualNote}
      </Typography>
    </Stack>
  );
}
