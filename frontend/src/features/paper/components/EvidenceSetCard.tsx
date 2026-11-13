import { Alert, Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { evidenceSetQuery, type EvidenceSet } from '../api/shadowQueries';
import { EvidenceSetDialog } from './EvidenceSetDialog';

const stateText: Record<string, string> = {
  pending: 'Pending: no set stored yet',
  open: 'Open: the owner may still replace it',
  frozen: 'Frozen: never replaceable',
};

/** Who or what fixed the set, in words: the default rule (system:default) is never shown as an owner decision. */
function setOrigin(set: EvidenceSet): string {
  if (set.mode === 'auto') return 'System default (the versions of the eligible cells, registered automatically)';
  if (set.mode === 'backfill')
    return 'Migration backfill (the versions of rows already stored; NOT pre-registered, not chosen before their results)';
  if (set.mode === 'owner') return `Owner (${set.actor ?? 'unknown'})`;
  return 'Not fixed yet: the first shadow write of the epoch registers the default';
}

/** The frozen evidence set of the current edge epoch (AM-01b, GAP-09): which strategy versions count for the edge figures. The only control is the owner's replace, offered until the first shadow write. */
export function EvidenceSetCard() {
  const query = useQuery(evidenceSetQuery);
  const [replacing, setReplacing] = useState(false);
  if (query.isPending) return <Typography color="text.secondary">Reading the evidence set…</Typography>;
  if (query.isError) return <Alert severity="warning">The evidence set could not be read.</Alert>;
  const set = query.data;
  const canReplace = set.state !== 'frozen';
  return (
    <Box component="section" aria-labelledby="shadow-evidence-set-heading">
      <Typography variant="h2" id="shadow-evidence-set-heading" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Evidence set
      </Typography>
      <Stack spacing={1}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <Chip size="small" label={stateText[set.state] ?? 'Unavailable'} />
          <Typography>{setOrigin(set)}</Typography>
        </Stack>
        <Typography color="text.secondary">{set.note}</Typography>
        {set.reason && <Typography>Reason: {set.reason}</Typography>}
        <Typography>
          {set.state === 'pending' ? 'Versions the default would register' : 'Versions in the set'}:{' '}
          {set.versions.length}
        </Typography>
        {set.versions.length > 0 && (
          <Box component="ul" aria-label="Evidence set versions" sx={{ m: 0, pl: 2.5 }}>
            {set.versions.map((v) => (
              <li key={v}>
                <code>{v.slice(0, 12)}</code>
              </li>
            ))}
          </Box>
        )}
        {canReplace && (
          <Box>
            <Button
              variant="outlined"
              onClick={() => {
                setReplacing(true);
              }}
            >
              Replace the set…
            </Button>
          </Box>
        )}
      </Stack>
      {replacing && (
        <EvidenceSetDialog
          initial={set.versions}
          onClose={() => {
            setReplacing(false);
          }}
        />
      )}
    </Box>
  );
}
