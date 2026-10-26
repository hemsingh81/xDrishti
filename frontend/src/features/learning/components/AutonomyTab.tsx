import PauseIcon from '@mui/icons-material/PauseCircleOutlined';
import PlayIcon from '@mui/icons-material/PlayCircleOutlined';
import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatRate } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { autonomyQuery } from '../api/learningQueries';
import { AutonomyHistory } from './AutonomyHistory';
import { MoveLink } from './MoveLink';
import { Panel } from './Panel';

function Switch({ name, on }: { name: string; on: boolean }) {
  const Icon = on ? PlayIcon : PauseIcon;
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
      <Icon aria-hidden />
      <Typography>
        {name}: <strong>{on ? 'ON' : 'OFF'}</strong>
        {on ? '' : ' (Manual approval)'}
      </Typography>
    </Box>
  );
}

/** Autonomy, read only: the switches in force and how they changed. Nothing here changes a setting. */
export function AutonomyTab() {
  const query = useQuery(autonomyQuery);
  if (query.isPending) return <LoadingState label="Loading the autonomy settings…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const s = query.data;
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Panel title="Switches in force">
        <Box sx={{ display: 'grid', gap: 1 }}>
          <Switch name="Automatic approval" on={s.autoApproveTickets} />
          <Switch name="Reserve promotion" on={s.promoteFromReserve} />
          <Typography>Minimum win-rate bound: {formatRate(s.minPWin)}</Typography>
          <Typography>Daily cap: {s.maxAutoPerDay} automatic approval(s)</Typography>
          <Typography variant="body2" color="text.secondary">
            Snapshot hash: {s.hash ?? 'none stored (the defaults apply)'}
          </Typography>
          <Typography variant="body2">{s.notice}</Typography>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            No setting lets anything place an order; there is no order in this system yet.
          </Typography>
          <Typography variant="body2" color="text.secondary">
            A change needs a fresh password and leaves a new row; this tab never changes a setting.
          </Typography>
          <MoveLink capability="autonomy_form" to="/owner-acts" label="Change these switches" />
        </Box>
      </Panel>
      <Panel title="Change history">
        <AutonomyHistory history={s.history} />
      </Panel>
    </Box>
  );
}
