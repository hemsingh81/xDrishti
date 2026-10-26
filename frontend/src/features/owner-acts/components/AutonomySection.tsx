import { Typography } from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { autonomySettingsQuery } from '../api/movesApi';
import { useCapability } from '../api/capabilitiesRead';
import { AutonomyForm } from './AutonomyForm';

/** The autonomy form of the switches page. Drawn only while `autonomy_form` is ON; off, it says so and offers nothing to write. */
export function AutonomySection() {
  const { enabled, loaded } = useCapability('autonomy_form');
  if (!loaded) return null;
  return (
    <section aria-label="Autonomy settings">
      <Typography variant="h2" component="h2" sx={{ fontSize: '1.1rem', mb: 1 }}>
        Autonomy settings
      </Typography>
      {enabled ? (
        <Loaded />
      ) : (
        <Typography color="text.secondary">
          The autonomy form is switched off. Switch it on above to change the autonomy settings from the screen.
        </Typography>
      )}
    </section>
  );
}

function Loaded() {
  const queryClient = useQueryClient();
  const query = useQuery(autonomySettingsQuery);
  const [version, setVersion] = useState(0);
  if (query.isPending) return <LoadingState />;
  if (query.isError) return <ErrorState error={query.error} />;
  return (
    <AutonomyForm
      key={`${query.data.id ?? 0}:${version}`}
      settings={query.data}
      onReload={() => {
        void queryClient.refetchQueries({ queryKey: autonomySettingsQuery.queryKey }).then(() => {
          setVersion((v) => v + 1);
        });
      }}
    />
  );
}
