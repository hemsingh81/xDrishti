import { Card, TableCell, TableRow, Typography } from '@mui/material';
import type { HoldingGroup } from '@/shared/api';
import { formatInrPrecise } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { figureCellSx, groupColumns } from './groupColumns';
import { GroupDividends } from './GroupFigures';
import { Figure } from './GroupCard';

/** What the row says about itself: not a group of holdings, the dividends and costs that belong to no group. */
function Explanation({ group }: { group: HoldingGroup }) {
  const charges = group.income?.charges ?? 0;
  return (
    <>
      <Typography component="span" sx={{ fontWeight: 600, display: 'block' }}>
        {group.name}
      </Typography>
      <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block', ...numeric }}>
        {`Dividends and costs that belong to no group${charges === 0 ? '' : ` · costs ${formatInrPrecise(charges)}`}`}
      </Typography>
    </>
  );
}

/**
 * The extra last row of a grouping (ADR 0026): the dividends and costs that belong to no group, so that the Dividends column
 * adds up to the total. It is not a holding group: no button, nothing to open, no figure but the dividends. Styled apart
 * (italic name, dashed top border) so it is never taken for a group.
 */
export function UnallocatedRow({ group }: { group: HoldingGroup }) {
  return (
    <TableRow
      aria-label={group.name}
      sx={{ '& > td, & > th': { borderTop: 1, borderTopStyle: 'dashed', borderColor: 'divider' } }}
    >
      <TableCell component="th" scope="row" sx={{ paddingInline: 1.25, verticalAlign: 'top', fontStyle: 'italic' }}>
        <Explanation group={group} />
      </TableCell>
      {groupColumns.map((column) => (
        <TableCell key={column.id} sx={figureCellSx(column)}>
          {column.id === 'dividends' ? <GroupDividends group={group} /> : null}
        </TableCell>
      ))}
    </TableRow>
  );
}

/** The same on a phone: a card with the explanation and the dividends, no figures of holdings, nothing to open. */
export function UnallocatedCard({ group }: { group: HoldingGroup }) {
  return (
    <Card component="li" variant="outlined" sx={{ listStyle: 'none', p: 1.5, borderStyle: 'dashed' }}>
      <Explanation group={group} />
      <Figure label="Dividends">
        <GroupDividends group={group} align="flex-start" />
      </Figure>
    </Card>
  );
}
