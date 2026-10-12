import ArchiveIcon from '@mui/icons-material/ArchiveOutlined';
import EditIcon from '@mui/icons-material/EditOutlined';
import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import LockIcon from '@mui/icons-material/LockOutlined';
import PlaylistAddIcon from '@mui/icons-material/PlaylistAddOutlined';
import { Button, Chip, Stack, Typography } from '@mui/material';
import type { Basket } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { purposeLabel } from '../lib/labels';
import { AsOfField } from './AsOfField';
import { BasketDot } from './BasketDot';

interface BasketHeaderProps {
  basket: Basket;
  asOf: string | null;
  onAsOfChange: (value: string | null) => void;
  onAdd: () => void;
  onEdit: () => void;
  onHistory: () => void;
  onArchive: () => void;
}

export const portfolioNote = "Portfolio baskets follow the account's holdings and can't be archived or edited.";

/** The title block of a portfolio basket: name, badge, account and count, and why there are no actions. */
function PortfolioHeader({ basket }: { basket: Basket }) {
  return (
    <Stack spacing={1.5} sx={{ p: 2 }}>
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
        <BasketDot colour={basket.colour} size={14} />
        <Typography variant="h2" sx={{ overflowWrap: 'anywhere' }}>
          {basket.name}
        </Typography>
        <Chip size="small" variant="outlined" icon={<LockIcon />} label="Portfolio · read only" />
        {basket.accountName && <Typography color="text.secondary">{basket.accountName}</Typography>}
        <Typography color="text.secondary">
          {`${formatNumber(basket.memberCount)} ${basket.memberCount === 1 ? 'instrument' : 'instruments'}`}
        </Typography>
      </Stack>
      <Typography color="text.secondary">{portfolioNote}</Typography>
    </Stack>
  );
}

/** The selected basket's title block: name, purpose, count, description and the actions on the basket. */
export function BasketHeader({ basket, asOf, onAsOfChange, onAdd, onEdit, onHistory, onArchive }: BasketHeaderProps) {
  if (basket.system) return <PortfolioHeader basket={basket} />;
  return (
    <Stack spacing={1.5} sx={{ p: 2 }}>
      <Stack direction="row" sx={{ gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
        <BasketDot colour={basket.colour} size={14} />
        <Typography variant="h2" sx={{ overflowWrap: 'anywhere' }}>
          {basket.name}
        </Typography>
        <Chip size="small" variant="outlined" label={purposeLabel(basket.purpose)} />
        <Typography color="text.secondary">
          {`${formatNumber(basket.memberCount)} ${basket.memberCount === 1 ? 'instrument' : 'instruments'}`}
        </Typography>
      </Stack>
      {basket.description && <Typography color="text.secondary">{basket.description}</Typography>}
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
          <Button
            variant="contained"
            size="small"
            startIcon={<PlaylistAddIcon />}
            disabled={asOf !== null}
            onClick={onAdd}
          >
            Add instruments
          </Button>
          <Button size="small" variant="outlined" startIcon={<EditIcon />} onClick={onEdit}>
            Edit
          </Button>
          <Button size="small" variant="outlined" startIcon={<HistoryIcon />} onClick={onHistory}>
            History
          </Button>
          <Button size="small" variant="outlined" color="error" startIcon={<ArchiveIcon />} onClick={onArchive}>
            Archive
          </Button>
        </Stack>
        <AsOfField value={asOf} onChange={onAsOfChange} />
      </Stack>
    </Stack>
  );
}
