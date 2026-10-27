import { Box, Button, List, ListItemButton, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatAsOf } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { conversationsQuery } from '../api/assistantApi';
import { words } from '../lib/wording';

interface Props {
  selected: string | null;
  onSelect: (id: string) => void;
  onNew: () => void;
}

/** Past conversations, newest first. The list is text; the title is shown as text only. */
export function ConversationList({ selected, onSelect, onNew }: Props) {
  const query = useQuery(conversationsQuery);
  return (
    <Box component="nav" aria-label="Conversations" sx={{ minWidth: 0 }}>
      <Button variant="outlined" fullWidth onClick={onNew} sx={{ mb: 1 }}>
        New conversation
      </Button>
      {query.isPending && <LoadingState label="Loading conversations…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data?.length === 0 && <Typography color="text.secondary">{words.noConversations}</Typography>}
      {query.data && query.data.length > 0 && (
        <List dense disablePadding>
          {query.data.map((c) => (
            <ListItemButton
              key={c.id}
              selected={c.id === selected}
              aria-current={c.id === selected ? 'true' : undefined}
              onClick={() => {
                onSelect(c.id);
              }}
            >
              <ListItemText
                primary={c.title}
                secondary={formatAsOf(c.lastActivityAt)}
                slotProps={{ primary: { noWrap: true } }}
              />
            </ListItemButton>
          ))}
        </List>
      )}
    </Box>
  );
}
