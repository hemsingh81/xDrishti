import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import { Box, List, ListItem, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatDateTime } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { signInActivityQuery } from '../api/accountApi';
import { auditEventLabel } from '../lib/auditEvents';
import { AccountCard } from './AccountCard';
import { QueryAlert } from './QueryAlert';

/** The last sign-in events: when, what happened and from which address. Newest first. */
export function SignInActivityCard() {
  const query = useQuery(signInActivityQuery);
  return (
    <AccountCard
      id="activity"
      title="Recent sign-in activity"
      icon={HistoryIcon}
      hue="audit"
      description="Sign-ins, sign-outs, lockouts and account changes. If something here is not you, change your password."
    >
      {query.isPending && <LoadingState label="Loading sign-in activity" />}
      {query.isError && (
        <QueryAlert
          error={query.error}
          fallback="The sign-in activity could not be loaded."
          onRetry={() => {
            void query.refetch();
          }}
        />
      )}
      {query.data?.length === 0 && <Typography color="text.secondary">No sign-in activity recorded yet.</Typography>}
      {query.data && query.data.length > 0 && (
        <List aria-label="Recent sign-in activity" disablePadding>
          {query.data.map((entry, index) => (
            <ListItem key={`${entry.at}|${entry.event}|${String(index)}`} divider disableGutters>
              <ListItemText
                primary={auditEventLabel(entry.event)}
                secondary={
                  <>
                    <Box component="time" dateTime={entry.at} className="numeric">
                      {formatDateTime(entry.at)}
                    </Box>
                    {' · '}
                    <span className="numeric">{entry.ip ?? 'address unknown'}</span>
                  </>
                }
                slotProps={{ primary: { sx: { fontWeight: 600 } } }}
              />
            </ListItem>
          ))}
        </List>
      )}
    </AccountCard>
  );
}
