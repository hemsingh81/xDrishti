import { Alert, Box, Button, Card, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import type { UseQueryResult } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import type { HoldingGroups } from '@/shared/api';
import { LoadingState } from '@/shared/ui';
import { stateAlpha } from '@/theme';
import { problemKind, problemText } from '../lib/errors';
import type { GroupBy, PortfolioFilters } from '../lib/filters';
import { groupSortOf, nextGroupSort, type GroupSortField } from '../lib/groups';
import { isUnallocatedGroup } from '../lib/income';
import type { GroupScope } from './GroupChildren';
import { GroupCards } from './GroupCards';
import { GroupTable } from './GroupTable';
import { GroupToolbar } from './GroupToolbar';

interface GroupGridProps {
  query: UseQueryResult<HoldingGroups>;
  by: GroupBy;
  scope: GroupScope;
  /** Shown when the scope has no holding. */
  emptyState: ReactNode;
  onChange: (patch: Partial<PortfolioFilters>) => void;
}

/** The groups could not be loaded: Retry, or, when the server refused the request itself, a way back to the flat list. */
function GroupsError({ query, onFlat }: { query: UseQueryResult<HoldingGroups>; onFlat: () => void }) {
  const refused = problemKind(query.error) === 'invalid';
  return (
    <Alert
      severity="error"
      sx={{ m: 2 }}
      action={
        refused ? (
          <Button color="inherit" size="small" onClick={onFlat}>
            Group by None
          </Button>
        ) : (
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              void query.refetch();
            }}
          >
            Retry
          </Button>
        )
      }
    >
      {problemText(query.error, 'The groups could not be loaded.')}
    </Alert>
  );
}

/**
 * The Group by view: a card that fills the space under the tiles (from `md`), with the total row stuck under the column
 * titles and one row per group; on a phone, one card per group. A group opens to its holdings. Opened groups are local
 * state (the URL keeps the grouping and the order, not what is open).
 */
export function GroupGrid({ query, by, scope, emptyState, onChange }: GroupGridProps) {
  const phone = useMediaQuery(useTheme().breakpoints.down('sm'));
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set());
  const data = query.data;
  const sort = groupSortOf(scope.filters.sort);
  // Keys of groups that are gone (another filter or order) do not count as open.
  // The extra Unallocated row is not a group of holdings: it is not counted, opened or listed by Expand all.
  const groupKeys = data ? data.groups.filter((group) => !isUnallocatedGroup(group)).map((group) => group.key) : [];
  const openCount = groupKeys.filter((key) => open.has(key)).length;

  const toggle = (key: string) => {
    setOpen((previous) => {
      const next = new Set(previous);
      if (!next.delete(key)) next.add(key);
      return next;
    });
  };
  const onSort = (field: GroupSortField) => {
    onChange({ sort: nextGroupSort(sort, field) });
  };

  let body: ReactNode;
  if (data === undefined) {
    body = query.isError ? (
      <GroupsError
        query={query}
        onFlat={() => {
          onChange({ groupBy: 'none' });
        }}
      />
    ) : (
      <LoadingState label="Loading groups" />
    );
  } else if (groupKeys.length === 0) {
    body = emptyState;
  } else if (phone) {
    body = <GroupCards data={data} by={by} expanded={open} scope={scope} onToggle={toggle} />;
  } else {
    body = (
      <GroupTable data={data} by={by} sort={sort} expanded={open} scope={scope} onSort={onSort} onToggle={toggle} />
    );
  }

  return (
    // From md the card fills the space under the tiles, but never less than 60 % of the window: an opened group needs room.
    <Card
      sx={{
        display: 'flex',
        flexDirection: 'column',
        flex: '1 1 auto',
        minHeight: { xs: 320, md: 'max(320px, 60vh)' },
      }}
    >
      <GroupToolbar
        by={by}
        groupCount={groupKeys.length}
        openCount={openCount}
        onExpandAll={() => {
          setOpen(new Set(groupKeys));
        }}
        onCollapseAll={() => {
          setOpen(new Set());
        }}
        overlapping={data ? !data.additive : by === 'basket'}
        sortControl={
          phone
            ? {
                sort,
                onChange: (next) => {
                  onChange({ sort: next });
                },
              }
            : null
        }
      />
      {query.isError && data !== undefined && (
        <Alert
          severity="warning"
          sx={{ mx: 2, mb: 1 }}
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void query.refetch();
              }}
            >
              Retry
            </Button>
          }
        >
          {`${problemText(query.error, 'The groups could not be refreshed.')} What is shown may be out of date.`}
        </Alert>
      )}
      <Box
        aria-busy={query.isFetching || undefined}
        sx={{
          flex: '1 1 auto',
          minHeight: 0,
          overflow: 'auto',
          opacity: query.isPlaceholderData ? stateAlpha.busy : 1,
          transition: 'opacity 120ms',
        }}
      >
        {body}
      </Box>
    </Card>
  );
}
