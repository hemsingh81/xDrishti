import { Alert, Box, Button, Link, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { AccountDetail } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useSaveRoles } from '../hooks/useSaveRoles';
import { allMessages, fieldErrorsOf, problemMessage } from '../lib/fieldErrors';
import { draftOf, isPlanEmpty, planRoleChange, type RoleDraft } from '../lib/roleChanges';
import type { AccountTab } from '../lib/tabs';
import { RoleToggle } from './RoleToggle';

/** Roles of one account: switch Data / Trading / Portfolio, choose primary or standby. */
export function RolesTab({ account, onNavigate }: { account: AccountDetail; onNavigate: (tab: AccountTab) => void }) {
  return <RolesForm key={account.updatedAt} account={account} onNavigate={onNavigate} />;
}

function RolesForm({ account, onNavigate }: { account: AccountDetail; onNavigate: (tab: AccountTab) => void }) {
  const { notify } = useToast();
  const save = useSaveRoles();
  const current = draftOf(account.roles);
  const [draft, setDraft] = useState<RoleDraft>(current);
  const [blocked, setBlocked] = useState<string | undefined>();
  const isPrimary = account.data.primary;
  const tradingIncomplete = !account.trading.enabled && account.trading.riskProfileId === null;
  const portfolioIncomplete = !account.portfolio.enabled && account.portfolio.defaultBucket === null;
  const messages = [...allMessages(fieldErrorsOf(save.error))];
  const general = problemMessage(save.error);

  function change(patch: Partial<RoleDraft>) {
    setDraft((previous) => ({ ...previous, ...patch }));
    setBlocked(undefined);
    save.reset();
  }

  function submit() {
    const plan = planRoleChange(current, draft);
    setBlocked(plan.blocked);
    if (plan.blocked) {
      return;
    }
    if (isPlanEmpty(plan)) {
      notify('Nothing to change.', 'info');
      return;
    }
    save.mutate(
      { id: account.id, plan },
      {
        onSuccess: () => {
          notify('Roles saved — services pick them up on their next run.');
        },
      },
    );
  }

  const setupNote = (tab: AccountTab, label: string) => (
    <>
      Complete the {label} settings first:{' '}
      <Link
        component="button"
        type="button"
        onClick={() => {
          onNavigate(tab);
        }}
      >
        open {label} settings
      </Link>
    </>
  );

  return (
    <Stack spacing={2}>
      <Stack spacing={1.5}>
        <RoleToggle
          roleKey="data"
          checked={draft.data}
          disabled={isPrimary}
          note={
            isPrimary ? 'This is the primary Data account. Make another account primary to change this.' : undefined
          }
          onChange={(data) => {
            change({ data });
          }}
          dataKind={{
            value: draft.dataKind,
            disabled: isPrimary,
            onChange: (dataKind) => {
              change({ dataKind });
            },
          }}
        />
        <RoleToggle
          roleKey="trading"
          checked={draft.trading}
          disabled={tradingIncomplete}
          note={tradingIncomplete ? setupNote('trading', 'Trading') : undefined}
          onChange={(trading) => {
            change({ trading });
          }}
        />
        <RoleToggle
          roleKey="portfolio"
          checked={draft.portfolio}
          disabled={portfolioIncomplete}
          note={portfolioIncomplete ? setupNote('portfolio', 'Portfolio') : undefined}
          onChange={(portfolio) => {
            change({ portfolio });
          }}
        />
      </Stack>
      <Typography variant="body2" color="text.secondary">
        Services pick up role changes on their next run. Role changes ask for your password.
      </Typography>
      {(blocked ?? general) && <Alert severity="error">{blocked ?? general}</Alert>}
      {messages.length > 0 && (
        <Alert severity="error">
          <Box component="ul" sx={{ m: 0, pl: 2 }}>
            {messages.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </Box>
        </Alert>
      )}
      <Box>
        <Button variant="contained" onClick={submit} loading={save.isPending}>
          Save roles
        </Button>
      </Box>
    </Stack>
  );
}
