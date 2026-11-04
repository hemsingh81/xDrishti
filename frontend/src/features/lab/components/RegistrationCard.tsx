import { Card, CardContent, CardHeader, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { registrationQuery } from '../api/confidenceQueries';
import { RegistrationForm } from './RegistrationForm';
import { RegistrationView } from './RegistrationView';

/**
 * The pre-registration of one stored version (CF-06, D1): why the rule should work and what would kill it, written before the data is searched.
 * Written once; after that it is shown read-only. The text is plain text everywhere.
 */
export function RegistrationCard({ strategyId, version }: { strategyId: string; version: number }) {
  const { data, error, isPending } = useQuery(registrationQuery(strategyId, version));
  return (
    <Card component="section" aria-label="Registration">
      <CardHeader
        title={<Typography variant="h2">Registration - {`${strategyId} v${version}`}</Typography>}
        subheader="Written down before the data is searched. A full or robust test of an unregistered version carries a warning (and is refused once the owner turns enforcement on)."
      />
      <CardContent>
        {isPending && <LoadingState label="Loading the registration" />}
        {error && <ErrorState error={error} />}
        {data === null && <RegistrationForm strategyId={strategyId} version={version} />}
        {data && <RegistrationView registration={data} />}
      </CardContent>
    </Card>
  );
}
