import { Box, Drawer, IconButton, Stack, Typography } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { useQuery } from '@tanstack/react-query';
import { formatIstDateTime } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { detailQuery } from '../api/auditQueries';
import { actorText, words } from '../lib/auditParams';
import { OutcomeChip, SourceChip } from './AuditChips';

/** One entry with its detail fields (a sign-in adds IP address and user agent). All text, nothing built into markup. */
export function AuditDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const detail = useQuery({ ...detailQuery(id ?? ''), enabled: id !== null });
  const entry = detail.data?.entry;
  return (
    <Drawer
      anchor="right"
      open={id !== null}
      onClose={onClose}
      slotProps={{ paper: { role: 'dialog', 'aria-label': 'Audit entry' } }}
    >
      <Box sx={{ width: { xs: '100vw', sm: 420 }, p: 2.5 }}>
        <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
          <Typography variant="h2" sx={{ fontSize: '1.1rem' }}>
            Audit entry
          </Typography>
          <IconButton aria-label="Close" onClick={onClose}>
            <CloseIcon />
          </IconButton>
        </Stack>
        {detail.isPending && id !== null && <LoadingState label="Loading the entry" />}
        {detail.isError && <ErrorState error={detail.error} />}
        {entry && (
          <Stack sx={{ gap: 1.5 }}>
            <Typography sx={{ overflowWrap: 'anywhere' }}>{entry.summary}</Typography>
            <Stack direction="row" sx={{ gap: 0.75, flexWrap: 'wrap' }}>
              <SourceChip source={entry.source} />
              <OutcomeChip outcome={entry.outcome} />
            </Stack>
            <Box
              component="dl"
              sx={{ m: 0, display: 'grid', gridTemplateColumns: 'max-content 1fr', columnGap: 2, rowGap: 0.75 }}
            >
              <Row label="When" value={formatIstDateTime(entry.at)} />
              <Row label="Category" value={words(entry.category)} />
              <Row label="Kind" value={entry.kind} />
              <Row label="By" value={actorText(entry)} />
              {entry.accountName && <Row label="Account" value={entry.accountName} />}
              {entry.planId !== null && <Row label="Plan" value={String(entry.planId)} />}
              {entry.itemId !== null && <Row label="Ticket" value={String(entry.itemId)} />}
              {(detail.data?.fields ?? []).map((f) => (
                <Row key={f.label} label={f.label} value={f.value} />
              ))}
            </Box>
            {entry.redacted && (
              <Typography variant="caption" color="text.secondary">
                Some text was masked because it looked like a secret.
              </Typography>
            )}
          </Stack>
        )}
      </Box>
    </Drawer>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <>
      <Typography component="dt" color="text.secondary">
        {label}
      </Typography>
      <Typography component="dd" sx={{ m: 0, overflowWrap: 'anywhere' }}>
        {value}
      </Typography>
    </>
  );
}
