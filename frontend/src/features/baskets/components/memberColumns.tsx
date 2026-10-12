import { Chip, Link } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { BasketMember } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';
import { typeLabel } from '../lib/labels';
import { MemberActions } from './MemberActions';
import { MemberHandle } from './MemberHandle';

type Column = GridColumn<BasketMember>;

const dash = '—';

const orderColumn: Column = {
  id: 'order',
  header: 'Order',
  kind: 'handle',
  width: 64,
  render: (m) => <MemberHandle id={m.instrument.id} symbol={m.instrument.symbol} />,
};

const symbolColumn: Column = {
  id: 'symbol',
  header: 'Symbol',
  render: (m) => (
    <Link
      component={RouterLink}
      to={`/instruments?id=${m.instrument.id}`}
      underline="hover"
      sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}
    >
      {m.instrument.symbol}
    </Link>
  ),
};

const nameColumn: Column = {
  id: 'name',
  header: 'Name',
  grow: true,
  minWidth: 180,
  accessor: (m) => m.instrument.displayName,
};

const typeColumn: Column = {
  id: 'type',
  header: 'Type',
  render: (m) => <Chip size="small" variant="outlined" label={typeLabel(m.instrument.type)} />,
};

const sectorColumn: Column = {
  id: 'sector',
  header: 'Sector',
  hideBelow: 'md',
  accessor: (m) => m.instrument.sector ?? dash,
};

/** Tracked / not tracked / retired as text (chips), never colour alone. Retired but still tracked says both (P1-05). */
const statusColumn: Column = {
  id: 'status',
  header: 'Status',
  minWidth: 200,
  render: (m) => {
    const { instrument } = m;
    if (instrument.tracked.retiredButTracked) {
      return <Chip size="small" variant="outlined" color="warning" label="Tracked · no longer in Dhan's list" />;
    }
    if (instrument.retired) return <Chip size="small" label="Retired" />;
    return instrument.tracked.active ? (
      <Chip size="small" variant="outlined" color="success" label="Tracked" />
    ) : (
      <Chip size="small" variant="outlined" label="Not tracked" />
    );
  },
};

const addedColumn: Column = {
  id: 'added',
  header: 'Added',
  hideBelow: 'sm',
  accessor: (m) => formatDate(m.validFrom),
};

/** Only in the look-back view: members that were removed after the chosen date. */
const removedColumn: Column = {
  id: 'removed',
  header: 'Removed later',
  hideBelow: 'sm',
  accessor: (m) => (m.validTo ? formatDate(m.validTo) : dash),
};

const actionsColumn: Column = {
  id: 'actions',
  header: 'Actions',
  kind: 'handle',
  width: 96,
  render: (m) => <MemberActions member={m} />,
};

const shared = [symbolColumn, nameColumn, typeColumn, sectorColumn, statusColumn, addedColumn];

/** The columns of the current members: with the drag grip first and copy / move / remove last. */
export const editableColumns: readonly Column[] = [orderColumn, ...shared, actionsColumn];

/** The columns of a look-back view: read only, so no grip and no actions. */
export const lookBackColumns: readonly Column[] = [...shared, removedColumn];
