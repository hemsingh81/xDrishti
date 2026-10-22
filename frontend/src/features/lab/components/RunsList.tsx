import { Chip, List, ListItemButton, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatIstDateTime } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { runsQuery } from '../api/runQueries';

interface RunsListProps {
  strategyId: string | null;
  poll: number | false;
  selectedId: string | null;
  onSelect: (id: string) => void;
}

/** The runs, newest first, with their state (and the verdict outcome of a finished full test). */
export function RunsList({ strategyId, poll, selectedId, onSelect }: RunsListProps) {
  const { data, error, isPending } = useQuery(runsQuery(strategyId, poll));
  if (isPending) return <LoadingState label="Loading runs" />;
  if (error) return <ErrorState error={error} />;
  if (data.length === 0) return <Typography color="text.secondary">No runs yet.</Typography>;
  return (
    <List aria-label="Runs" disablePadding>
      {data.map((r) => (
        <ListItemButton
          key={r.id}
          selected={r.id === selectedId}
          onClick={() => {
            onSelect(r.id);
          }}
        >
          <ListItemText
            primary={`${r.strategy.id} v${r.strategy.version} · ${r.method}`}
            secondary={`${r.state} · ${r.universe.label} · ${formatIstDateTime(r.createdAt)}`}
          />
          {r.verdict && <Chip size="small" variant="outlined" label={r.verdict.outcome} />}
        </ListItemButton>
      ))}
    </List>
  );
}
