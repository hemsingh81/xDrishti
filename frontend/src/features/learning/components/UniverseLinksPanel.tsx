import { List, ListItem, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { universeLinksQuery } from '../api/learningQueries';
import { MoveLink } from './MoveLink';
import { Panel } from './Panel';

/** Liquidity-tier cells against their all-shares parent. An observation links to the owner act that proposes it as a hypothesis; the screen itself writes nothing. */
export function UniverseLinksPanel() {
  const links = useQuery(universeLinksQuery);
  return (
    <Panel title="Liquidity tiers">
      {links.isPending && <LoadingState label="Loading the tier links…" />}
      {links.isError && <ErrorState error={links.error} />}
      {links.data?.length === 0 && (
        <Typography color="text.secondary">
          No tier cells yet: compute the cells with the universe split (xd learn cells --by-universe).
        </Typography>
      )}
      {links.data && links.data.length > 0 && (
        <List dense aria-label="Tier links">
          {links.data
            .filter((link) => link.window === 'all')
            .map((link) => (
              <ListItem key={link.childKey} sx={{ display: 'block', px: 0 }}>
                <ListItemText
                  primary={link.text}
                  secondary={`Hold-out ${link.holdOut}. An observation only: nothing is applied.`}
                  sx={{ overflowWrap: 'anywhere' }}
                />
                {link.observation && (
                  <MoveLink
                    capability="learning_moves_ui"
                    to={`/owner-acts/universe/${encodeURIComponent(link.childKey)}`}
                    label="Propose as hypothesis"
                  />
                )}
              </ListItem>
            ))}
        </List>
      )}
    </Panel>
  );
}
