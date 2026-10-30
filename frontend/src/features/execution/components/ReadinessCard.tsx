import {
  Alert,
  Button,
  Card,
  CardContent,
  CardHeader,
  List,
  ListItem,
  ListItemText,
  Stack,
  Typography,
} from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { formatIstDate, formatIstDateTime } from '@/shared/lib/format';
import { useCheckNow, type ReadinessAccount } from '../api/readinessApi';
import { toOutcome, type Outcome } from '../outcome';
import { CheckStatus } from './CheckStatus';

const stateOutcome = (state: string): Outcome =>
  state === 'ready' ? 'pass' : state === 'not_ready' ? 'fail' : 'unavailable';
const stateWord = (state: string) =>
  state === 'ready' ? 'Ready' : state === 'not_ready' ? 'Not ready' : 'Unavailable';

/** One account: can execution act today, and what is missing. Nothing is ready until a check says so. */
export function ReadinessCard({ account }: { account: ReadinessAccount }) {
  const checkNow = useCheckNow();
  return (
    <Card component="section" aria-label={`Readiness of ${account.name}`}>
      <CardHeader
        title={account.name}
        subheader={account.checkedAt ? `Last check ${formatIstDateTime(account.checkedAt)}` : 'No check recorded yet'}
        action={<CheckStatus outcome={stateOutcome(account.state)} />}
      />
      <CardContent>
        <Typography variant="h2" sx={{ fontSize: '1.25rem', mb: 1 }}>
          Execution: {stateWord(account.state)}
        </Typography>
        {account.checks.length === 0 ? (
          <Typography color="text.secondary">No checks recorded: nothing is ready until a check says so.</Typography>
        ) : (
          <List dense aria-label="Checks">
            {account.checks.map((c) => (
              <ListItem key={c.code} secondaryAction={<CheckStatus outcome={toOutcome(c.outcome)} />}>
                <ListItemText primary={c.text} secondary={c.code} />
              </ListItem>
            ))}
          </List>
        )}
        <Stack sx={{ gap: 0.5, my: 1 }}>
          <Typography variant="body2">
            Token ends: {account.tokenExpiresAt ? formatIstDateTime(account.tokenExpiresAt) : 'not known'}
          </Typography>
          {account.ipModifyAfter && (
            <Typography variant="body2">IP can be changed from {formatIstDate(account.ipModifyAfter)}</Typography>
          )}
        </Stack>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
          <Button
            variant="outlined"
            onClick={() => {
              checkNow.mutate();
            }}
            disabled={checkNow.isPending}
          >
            Check now
          </Button>
          <Button
            variant="contained"
            component={RouterLink}
            to={`/accounts?account=${account.accountId}&tab=connection`}
          >
            Connect Dhan
          </Button>
        </Stack>
        {checkNow.isSuccess && (
          <Typography role="status" variant="body2" sx={{ mt: 1 }}>
            Check requested: the result shows within a few seconds.
          </Typography>
        )}
        {checkNow.isError && (
          <Alert severity="error" sx={{ mt: 1 }}>
            {checkNow.error.message}
          </Alert>
        )}
      </CardContent>
    </Card>
  );
}
