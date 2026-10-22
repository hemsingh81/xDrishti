import { Card, CardContent, CardHeader, Chip, List, ListItemButton, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { strategiesQuery } from '../api/labQueries';
import { VersionList } from './VersionList';

interface LibraryCardProps {
  familyId: string | null;
  version: number | null;
  onSelectFamily: (id: string) => void;
  onOpenVersion: (id: string, version: number) => void;
}

/** The strategy families (built-ins and own); choosing one lists its versions. */
export function LibraryCard({ familyId, version, onSelectFamily, onOpenVersion }: LibraryCardProps) {
  const { data, error, isPending } = useQuery(strategiesQuery);
  return (
    <Card>
      <CardHeader title={<Typography variant="h2">Library</Typography>} />
      <CardContent>
        {isPending && <LoadingState label="Loading strategies" />}
        {error && <ErrorState error={error} />}
        {data?.length === 0 && <Typography color="text.secondary">No strategies yet.</Typography>}
        {data && data.length > 0 && (
          <List aria-label="Strategies" disablePadding>
            {data.map((s) => (
              <ListItemButton
                key={s.id}
                selected={s.id === familyId}
                onClick={() => {
                  onSelectFamily(s.id);
                }}
              >
                <ListItemText
                  primary={s.id}
                  secondary={`${s.style} · latest v${s.latestVersion} · ${s.versionCount} ${s.versionCount === 1 ? 'version' : 'versions'}`}
                />
                {s.builtIn && <Chip size="small" label="Built-in" />}
              </ListItemButton>
            ))}
          </List>
        )}
        {familyId && <VersionList familyId={familyId} selected={version} onOpen={onOpenVersion} />}
      </CardContent>
    </Card>
  );
}
