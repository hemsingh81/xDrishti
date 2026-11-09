import { Box, Button, Link, ListItem, Stack, Typography } from '@mui/material';
import { Suspense, useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { PasswordReasonDialog } from '@/features/execution';
import type { EssentialSettingView } from '@/shared/api/types';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useChangeNotices, useResetEssential } from '../api/essentialsApi';
import type { essentialSettings } from '../lib/essentials';
import { formatIstDateTime } from '@/shared/lib/format';
import { EssentialMark } from './EssentialMark';

type Row = (typeof essentialSettings)[number];
type Act = 'notices-off' | 'notices-on' | 'reset';

/**
 * One Essential setting: its mark, who changed it and when, and the acts it offers. Every act is a button and is
 * confirmed (a password for turning notices off or for a reset); nothing runs on load.
 */
export function EssentialRow({ row, item }: { row: Row; item: EssentialSettingView }) {
  const [act, setAct] = useState<Act | null>(null);
  const notices = useChangeNotices();
  const reset = useResetEssential();
  const hasLink = 'to' in row;
  const isNotices = row.key === 'notices.failure';
  const noticesOn = (item.value ?? item.default) !== 'false';
  const pending = notices.isPending || reset.isPending;

  const open = (next: Act) => {
    notices.reset();
    reset.reset();
    setAct(next);
  };
  const close = () => {
    if (!pending) setAct(null);
  };
  const done = {
    onSuccess: () => {
      setAct(null);
    },
  };

  return (
    <ListItem divider sx={{ display: 'block', py: 1.5 }}>
      <Box sx={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'space-between', gap: 1 }}>
        <Typography component="h2" variant="subtitle1">
          {item.label}
        </Typography>
        {hasLink ? (
          <Link component={RouterLink} to={row.to}>
            {row.linkLabel}
          </Link>
        ) : (
          <EssentialMark item={item} />
        )}
      </Box>
      <Typography variant="body2" color="text.secondary">
        {row.help}
      </Typography>
      {item.changedBy !== null && item.changedAt !== null && (
        <Typography variant="body2" color="text.secondary">
          Changed by {item.changedBy} · {formatIstDateTime(item.changedAt)}
        </Typography>
      )}
      {!item.canReset && item.resetNote !== null && (
        <Typography variant="body2" color="text.secondary">
          {item.resetNote}
        </Typography>
      )}
      <Stack direction="row" spacing={1} sx={{ mt: 1, flexWrap: 'wrap' }}>
        {isNotices && (
          <Button
            size="small"
            variant="outlined"
            onClick={() => {
              open(noticesOn ? 'notices-off' : 'notices-on');
            }}
          >
            {noticesOn ? 'Turn off failure notices' : 'Turn on failure notices'}
          </Button>
        )}
        {item.canReset && !hasLink && (
          <Button
            size="small"
            variant="text"
            onClick={() => {
              open('reset');
            }}
          >
            Reset {item.label} to default
          </Button>
        )}
      </Stack>
      <ConfirmDialog
        open={act === 'notices-on'}
        title="Turn on failure notices?"
        confirmLabel="Turn on"
        busy={notices.isPending}
        onCancel={close}
        onConfirm={() => {
          notices.mutate({ on: true, password: null }, done);
        }}
      >
        You will be told when a service run fails.
        {notices.isError && ' The change was refused; nothing was changed.'}
      </ConfirmDialog>
      <Suspense fallback={null}>
        {act !== null && act !== 'notices-on' && (
          <PasswordReasonDialog
            open
            askReason={false}
            title={act === 'reset' ? `Reset ${item.label} to default?` : 'Turn off failure notices?'}
            confirmLabel={act === 'reset' ? 'Reset to default' : 'Turn off'}
            intro={
              <Typography variant="body2">
                {act === 'reset'
                  ? 'Asks for your password again, for this act only.'
                  : 'Loosening a safety asks for your password again, for this act only.'}
              </Typography>
            }
            pending={pending}
            error={act === 'reset' ? reset.error : notices.error}
            onClose={close}
            onSubmit={({ password }) => {
              if (act === 'reset') reset.mutate({ key: item.key, password }, done);
              else notices.mutate({ on: false, password }, done);
            }}
          />
        )}
      </Suspense>
    </ListItem>
  );
}
