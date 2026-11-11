import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Button, Chip, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { Basket, BasketHistory, BasketHistoryEntry } from '@/shared/api';
import type { BasketVersionPage } from '@/shared/api/systemBasketTypes';
import { formatDate, formatNumber, istDayIso } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { historyQuery } from '../api/basketsQueries';
import { versionsQuery } from '../api/systemQueries';
import { basketProblemText } from '../lib/errors';
import { shortHash } from '../lib/system';

interface VersionDrawerProps {
  basket: Basket;
  open: boolean;
  onClose: () => void;
}

function QueryBody({ query, fallback, children }: { query: UseQueryResult; fallback: string; children: ReactNode }) {
  if (query.isPending) return <LoadingState label="Loading history" />;
  if (query.isError) {
    return (
      <Alert
        severity="error"
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
        {basketProblemText(query.error, fallback)}
      </Alert>
    );
  }
  return children;
}

function Versions({ page }: { page: BasketVersionPage }) {
  if (page.items.length === 0) return <Typography color="text.secondary">No version has been formed yet.</Typography>;
  return (
    <Box component="ul" aria-label="Versions" sx={{ m: 0, p: 0 }}>
      {page.items.map((v) => (
        <Box
          component="li"
          key={v.versionId}
          sx={{ listStyle: 'none', py: 1.25, borderBottom: 1, borderColor: 'divider' }}
        >
          <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
            <Typography sx={{ fontWeight: 600 }}>{`As of ${formatDate(v.asOf)}`}</Typography>
            <Chip size="small" variant="outlined" label={v.provenance} />
          </Stack>
          <Typography variant="body2" color="text.secondary">
            {`Effective from ${formatDate(v.effectiveFrom)} · ${formatNumber(v.memberCount)} members of ${formatNumber(v.universeSize)} · parameters `}
            <Box component="span" sx={{ fontFamily: monoFontFamily }}>
              {shortHash(v.paramsHash)}
            </Box>
          </Typography>
        </Box>
      ))}
    </Box>
  );
}

interface Day {
  day: string;
  added: string[];
  removed: string[];
}

/** The history entries grouped by India day, newest first: the symbols added and removed that day. */
function byDay(entries: readonly BasketHistoryEntry[]): Day[] {
  const days = new Map<string, Day>();
  for (const entry of entries) {
    const day = istDayIso(new Date(entry.at));
    const found = days.get(day) ?? { day, added: [], removed: [] };
    (entry.kind === 'added' ? found.added : found.removed).push(entry.symbol);
    days.set(day, found);
  }
  return [...days.values()].sort((a, b) => b.day.localeCompare(a.day));
}

/** Membership changes of an index / list basket: one entry per day with the count and symbols added and removed. */
function Changes({ history, since }: { history: BasketHistory; since: string | null | undefined }) {
  const days = byDay(history.items);
  return (
    <>
      {since && (
        <Typography variant="body2" color="text.secondary" sx={{ pb: 1 }}>
          {`Members are known from ${formatDate(since)}; earlier dates use these members (ISS-22).`}
        </Typography>
      )}
      {days.length === 0 && <Typography color="text.secondary">No membership change has been recorded yet.</Typography>}
      <Box component="ul" aria-label="Changes" sx={{ m: 0, p: 0 }}>
        {days.map((d) => (
          <Box component="li" key={d.day} sx={{ listStyle: 'none', py: 1.25, borderBottom: 1, borderColor: 'divider' }}>
            <Typography sx={{ fontWeight: 600 }}>{formatDate(d.day)}</Typography>
            <Typography variant="body2" color="text.secondary">
              {`${formatNumber(d.added.length)} added, ${formatNumber(d.removed.length)} removed`}
            </Typography>
            {d.added.length > 0 && (
              <Typography variant="body2" sx={{ fontFamily: monoFontFamily, overflowWrap: 'anywhere' }}>
                {`Added: ${d.added.join(', ')}`}
              </Typography>
            )}
            {d.removed.length > 0 && (
              <Typography variant="body2" sx={{ fontFamily: monoFontFamily, overflowWrap: 'anywhere' }}>
                {`Removed: ${d.removed.join(', ')}`}
              </Typography>
            )}
          </Box>
        ))}
      </Box>
    </>
  );
}

/**
 * The history of a system basket (unlike portfolio baskets it has dated membership): the versions of a derived basket
 * (as-of, effective date, size, parameter hash) or the applied changes of an index / list basket.
 */
export function VersionDrawer({ basket, open, onClose }: VersionDrawerProps) {
  const derived = basket.kind === 'derived';
  const versions = useQuery({ ...versionsQuery(basket.id), enabled: open && derived });
  const history = useQuery({ ...historyQuery(basket.id), enabled: open && !derived });
  return (
    <Drawer
      anchor="right"
      open={open}
      onClose={onClose}
      slotProps={{ paper: { 'aria-label': `History of ${basket.name}`, sx: { width: { xs: '100%', sm: 460 } } } }}
    >
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h2">{derived ? 'Versions' : 'History'}</Typography>
          <Typography color="text.secondary">{basket.name}</Typography>
        </Box>
        <IconButton aria-label="Close history" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Box sx={{ px: 2, pb: 3, overflowY: 'auto' }}>
        {derived ? (
          <QueryBody query={versions} fallback="The versions could not be loaded.">
            {versions.data && <Versions page={versions.data} />}
          </QueryBody>
        ) : (
          <QueryBody query={history} fallback="The history could not be loaded.">
            {history.data && <Changes history={history.data} since={basket.membersKnownSince} />}
          </QueryBody>
        )}
      </Box>
    </Drawer>
  );
}
