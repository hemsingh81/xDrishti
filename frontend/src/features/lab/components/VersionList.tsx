import { List, ListItemButton, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { strategyQuery } from '../api/labQueries';

interface VersionListProps {
  familyId: string;
  selected: number | null;
  onOpen: (id: string, version: number) => void;
}

/** The versions of one family, newest first. */
export function VersionList({ familyId, selected, onOpen }: VersionListProps) {
  const { data, error, isPending } = useQuery(strategyQuery(familyId));
  return (
    <>
      <Typography variant="h3" sx={{ mt: 2 }}>
        Versions of {familyId}
      </Typography>
      {isPending && <LoadingState label="Loading versions" />}
      {error && <ErrorState error={error} />}
      {data && (
        <List aria-label="Versions" disablePadding>
          {[...data.versions]
            .sort((a, b) => b.version - a.version)
            .map((v) => (
              <ListItemButton
                key={v.version}
                selected={v.version === selected}
                onClick={() => {
                  onOpen(familyId, v.version);
                }}
              >
                <ListItemText
                  primary={`Version ${v.version}`}
                  secondary={[v.hash.slice(0, 12), v.note].filter(Boolean).join(' · ')}
                />
              </ListItemButton>
            ))}
        </List>
      )}
    </>
  );
}
