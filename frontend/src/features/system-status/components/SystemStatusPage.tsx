import RefreshIcon from '@mui/icons-material/Refresh';
import { Button, Card, CardContent, CardHeader, Grid, Typography } from '@mui/material';
import { formatDateTime, formatDuration, formatNumber } from '@/shared/lib/format';
import { ErrorState, LoadingState, PageHeader, StatTile, StatusChip } from '@/shared/ui';
import { useSystemStatus } from '../api/systemStatusQuery';
import { ServiceTable } from './ServiceTable';

/** End-to-end check of the stack: browser → proxy → API → database ← worker. */
export function SystemStatusPage() {
  const { data, error, isPending, isFetching, refetch } = useSystemStatus();

  return (
    <>
      <PageHeader
        title="System status"
        description="Live health of the API, database and background services (refreshes every 10 seconds)."
        actions={
          <Button variant="outlined" startIcon={<RefreshIcon />} onClick={() => void refetch()} disabled={isFetching}>
            Refresh
          </Button>
        }
      />
      {isPending && <LoadingState label="Loading system status" />}
      {error && <ErrorState error={error} />}
      {data && (
        <>
          <Grid container spacing={2} sx={{ mb: 2 }}>
            <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
              <StatTile
                label="Overall"
                value={<StatusChip ok={data.isHealthy} />}
                hint={`checked ${formatDateTime(data.checkedAt)}`}
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
              <StatTile
                label="API"
                value={`v${data.application.version}`}
                hint={`${data.application.environment} · up ${formatDuration(data.application.uptimeSeconds)}`}
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
              <StatTile
                label="Database"
                value={data.database.isConnected ? `PostgreSQL ${data.database.serverVersion ?? ''}` : 'Unreachable'}
                tone={data.database.isConnected ? 'default' : 'bad'}
                hint={
                  data.database.isConnected
                    ? `TimescaleDB ${data.database.timescaleVersion ?? 'not installed'} · ${formatNumber(data.database.latencyMs)} ms`
                    : data.database.error
                }
              />
            </Grid>
            <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
              <StatTile
                label="Schema"
                value={
                  data.database.pendingMigrations === 0 ? 'Up to date' : `${data.database.pendingMigrations} pending`
                }
                tone={data.database.pendingMigrations === 0 ? 'good' : 'bad'}
                hint={data.database.latestMigration ?? '—'}
              />
            </Grid>
          </Grid>
          <Card>
            <CardHeader title={<Typography variant="h2">Background services</Typography>} />
            <CardContent sx={{ p: 0 }}>
              <ServiceTable services={data.services} />
            </CardContent>
          </Card>
        </>
      )}
    </>
  );
}
