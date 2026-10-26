import { Stack, Typography } from '@mui/material';
import { ConfigCard } from './ConfigCard';
import { EpochsCard } from './EpochsCard';
import { QueueCard } from './QueueCard';

/**
 * The Governance tab of the paper screen (spec P10-05): the configuration the 60 paper trades are measured against, the differences
 * not yet captured, the epochs and the queue of accepted hypotheses. Read only: the restart and the notes are CLI verbs
 * (`xd paper epoch restart|explain`); there is no apply or revert control.
 */
export function GovernanceTab() {
  return (
    <Stack sx={{ gap: 2 }}>
      <ConfigCard />
      <EpochsCard />
      <QueueCard />
      <Typography variant="body2" color="text.secondary">
        Detection, labelling and the restart of the count only. The fingerprint covers the engine and schema versions,
        not the code revision. Trades around a change are kept out of the count. Restart on purpose with{' '}
        <code>xd paper epoch restart --reason &quot;…&quot;</code>.
      </Typography>
    </Stack>
  );
}
