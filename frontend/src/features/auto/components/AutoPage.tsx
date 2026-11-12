import AutoModeIcon from '@mui/icons-material/AutoModeOutlined';
import { Card, CardContent, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState, PageHeader } from '@/shared/ui';
import { autoQuery } from '../api/autoApi';
import { AutoScopePicker } from './AutoScopePicker';
import { AutoSuggestions } from './AutoSuggestions';
import { AutoSwitchCard } from './AutoSwitchCard';

/** One switch, what to cover, and tomorrow's suggestions. Nothing here approves, arms or sends anything. */
export function AutoPage() {
  const auto = useQuery(autoQuery);
  return (
    <>
      <PageHeader
        title="Auto"
        icon={AutoModeIcon}
        hue="suggestions"
        description="Turn on the nightly chain, pick what to cover and read tomorrow's suggestions. It only suggests; no order is placed."
      />
      <Stack spacing={2}>
        {/* Minimum heights are the settled heights (measured); the page below does not jump as the three cards load. */}
        <Card sx={{ minHeight: auto.isPending ? { xs: 430, md: 192 } : undefined }}>
          <CardContent>
            {auto.isPending && <LoadingState label="Loading Auto…" />}
            {auto.isError && <ErrorState error={auto.error} />}
            {auto.data && <AutoSwitchCard auto={auto.data} />}
          </CardContent>
        </Card>
        <Card sx={{ minHeight: { xs: 389, md: 326 } }}>
          <CardContent>
            <AutoScopePicker />
          </CardContent>
        </Card>
        <Card sx={{ minHeight: { xs: 290, md: 230 } }}>
          <CardContent>
            <AutoSuggestions />
          </CardContent>
        </Card>
      </Stack>
    </>
  );
}
