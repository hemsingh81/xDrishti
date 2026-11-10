import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Button, Chip, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery, type UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import type { Basket, BasketMembers } from '@/shared/api';
import type { BasketSource, BasketVersionPage } from '@/shared/api/systemBasketTypes';
import { formatDate, formatIstDate, formatNumber, istDayIso } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { membersQuery } from '../api/basketsQueries';
import { sourceQuery, versionsQuery } from '../api/systemQueries';
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

/** Membership changes of an index / list basket: one entry per applied fetch, with the symbols added that day. */
function Changes({
  source,
  members,
  since,
}: {
  source: BasketSource;
  members: BasketMembers | undefined;
  since: string | null | undefined;
}) {
  const applied = source.fetches.filter((f) => f.outcome === 'applied');
  const addedOn = (at: string) =>
    (members?.items ?? [])
      .filter((m) => istDayIso(new Date(m.validFrom)) === istDayIso(new Date(at)))
      .map((m) => m.instrument.symbol);
  return (
    <>
      {since && (
        <Typography variant="body2" color="text.secondary" sx={{ pb: 1 }}>
          {`Members are known from ${formatDate(since)}; earlier dates use these members (ISS-22).`}
        </Typography>
      )}
      {applied.length === 0 && (
        <Typography color="text.secondary">No membership change has been applied yet.</Typography>
      )}
      <Box component="ul" aria-label="Changes" sx={{ m: 0, p: 0 }}>
        {applied.map((f) => (
          <Box component="li" key={f.at} sx={{ listStyle: 'none', py: 1.25, borderBottom: 1, borderColor: 'divider' }}>
            <Typography sx={{ fontWeight: 600 }}>{formatIstDate(f.at)}</Typography>
            <Typography variant="body2" color="text.secondary">
              {`${formatNumber(f.added)} added, ${formatNumber(f.removed)} removed`}
            </Typography>
            {addedOn(f.at).length > 0 && (
              <Typography variant="body2" sx={{ fontFamily: monoFontFamily, overflowWrap: 'anywhere' }}>
                {`Added: ${addedOn(f.at).join(', ')}`}
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
  const source = useQuery({ ...sourceQuery(basket.id), enabled: open && !derived });
  const members = useQuery({ ...membersQuery(basket.id, null), enabled: open && !derived });
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
          <QueryBody query={source} fallback="The history could not be loaded.">
            {source.data && <Changes source={source.data} members={members.data} since={basket.membersKnownSince} />}
          </QueryBody>
        )}
      </Box>
    </Drawer>
  );
}
