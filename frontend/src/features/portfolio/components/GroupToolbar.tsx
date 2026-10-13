import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import type { GroupBy, SortState } from '../lib/filters';
import { groupNoun, groupSortOptions } from '../lib/groups';

/** Most groups for which "Expand all" is offered: more would load too many lists at once. */
export const expandAllLimit = 20;

export const basketNote = 'Baskets overlap — totals do not add up to the portfolio';

interface GroupToolbarProps {
  by: GroupBy;
  /** Number of groups; the buttons appear only up to `expandAllLimit`. */
  groupCount: number;
  /** How many of the groups listed are open. */
  openCount: number;
  onExpandAll: () => void;
  onCollapseAll: () => void;
  /** Groups are not additive (baskets): say so. */
  overlapping: boolean;
  /** A phone has no column titles to sort by: a small control takes their place. */
  sortControl: { sort: SortState; onChange: (sort: SortState) => void } | null;
}

const optionValue = (field: string, descending: boolean) => `${descending ? '-' : ''}${field}`;

/** Above the groups: the title, the overlap note for baskets, Expand all / Collapse all, and on a phone the sort control. */
export function GroupToolbar({
  by,
  groupCount,
  openCount,
  onExpandAll,
  onCollapseAll,
  overlapping,
  sortControl,
}: GroupToolbarProps) {
  const canBulk = groupCount > 0 && groupCount <= expandAllLimit;
  return (
    <Stack spacing={1} sx={{ px: 2, pt: 1.5, pb: 1, flexShrink: 0 }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1 }}>
        <Typography variant="h2" component="h2">
          {`Holdings by ${groupNoun[by].toLowerCase()}`}
        </Typography>
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          {sortControl && (
            <TextField
              select
              size="small"
              label="Sort groups by"
              value={optionValue(sortControl.sort.field, sortControl.sort.descending)}
              onChange={(event) => {
                const option = groupSortOptions.find((o) => optionValue(o.field, o.descending) === event.target.value);
                if (option) sortControl.onChange({ field: option.field, descending: option.descending });
              }}
              slotProps={{ select: { native: true } }}
              sx={{ minWidth: 190 }}
            >
              {groupSortOptions.map((o) => (
                <option key={optionValue(o.field, o.descending)} value={optionValue(o.field, o.descending)}>
                  {o.label}
                </option>
              ))}
            </TextField>
          )}
          {canBulk && (
            <>
              <Button
                size="small"
                aria-disabled={openCount === groupCount}
                className={openCount === groupCount ? 'Mui-disabled' : undefined}
                onClick={() => {
                  if (openCount !== groupCount) onExpandAll();
                }}
              >
                Expand all
              </Button>
              <Button
                size="small"
                aria-disabled={openCount === 0}
                className={openCount === 0 ? 'Mui-disabled' : undefined}
                onClick={() => {
                  if (openCount !== 0) onCollapseAll();
                }}
              >
                Collapse all
              </Button>
            </>
          )}
        </Stack>
      </Stack>
      {overlapping && (
        <Alert severity="info" variant="outlined" sx={{ py: 0 }}>
          {basketNote}
        </Alert>
      )}
    </Stack>
  );
}
