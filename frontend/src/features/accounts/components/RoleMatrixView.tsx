import { Alert, Box, Button, Card, CardContent, CardHeader, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { AccountSummary } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import { accountsQuery } from '../api/accountsQueries';
import { useSaveRoleMatrix, type MatrixResult } from '../hooks/useSaveRoleMatrix';
import { draftOf, rolesDiffer, type RoleDraft } from '../lib/roleChanges';
import { RoleMatrixTable } from './RoleMatrixTable';

/** Accounts × roles in one table with a single Save (password confirmation on save). Loads the accounts itself. */
export function RoleMatrixView() {
  const { data: accounts, error, isPending } = useQuery(accountsQuery);
  if (isPending) {
    return <LoadingState label="Loading accounts" />;
  }
  if (error) {
    return <ErrorState error={error} />;
  }
  if (accounts.length === 0) {
    return (
      <Alert role="status" severity="info">
        No accounts yet — add one on the Accounts page, then give it its roles here.
      </Alert>
    );
  }
  return <RoleMatrix accounts={accounts} />;
}

function RoleMatrix({ accounts }: { accounts: AccountSummary[] }) {
  const { notify } = useToast();
  const save = useSaveRoleMatrix();
  // The table restarts from the server's state whenever the roles change (e.g. after a save).
  const seed = JSON.stringify(accounts.map((a) => [a.id, a.roles]));

  return (
    <Card>
      <CardHeader
        title={<Typography variant="h2">Role matrix</Typography>}
        subheader="Tick roles and pick the primary Data account, then save once (password confirmation)."
      />
      <CardContent>
        <MatrixEditor
          key={seed}
          accounts={accounts}
          result={save.data}
          saving={save.isPending}
          onSave={(input) => {
            save.mutate(input, {
              onSuccess: (result) => {
                if (result.cancelled) {
                  return; // the inline warning says so
                } else if (Object.keys(result.errors).length === 0) {
                  notify(
                    result.saved > 0 ? 'Roles saved — services pick them up on their next run.' : 'Nothing to change.',
                  );
                }
              },
            });
          }}
        />
      </CardContent>
    </Card>
  );
}

interface MatrixEditorProps {
  accounts: AccountSummary[];
  result: MatrixResult | undefined;
  saving: boolean;
  onSave: (input: {
    accounts: AccountSummary[];
    drafts: Record<string, RoleDraft>;
    primaryId: string | undefined;
  }) => void;
}

function MatrixEditor({ accounts, result, saving, onSave }: MatrixEditorProps) {
  const [drafts, setDrafts] = useState<Record<string, RoleDraft>>(() =>
    Object.fromEntries(accounts.map((a) => [a.id, draftOf(a.roles)])),
  );
  const currentPrimary = accounts.find((a) => a.roles.data.primary)?.id;
  const [primaryId, setPrimaryId] = useState<string | undefined>(currentPrimary);

  const dirty =
    primaryId !== currentPrimary ||
    accounts.some((a) => {
      const draft = drafts[a.id];
      return draft ? rolesDiffer(draftOf(a.roles), draft) : false;
    });

  function update(id: string, patch: Partial<RoleDraft>) {
    setDrafts((previous) => {
      const draft = previous[id];
      return draft ? { ...previous, [id]: { ...draft, ...patch } } : previous;
    });
  }

  return (
    <Box>
      <RoleMatrixTable
        accounts={accounts}
        drafts={drafts}
        primaryId={primaryId}
        errors={result?.errors ?? {}}
        onToggle={(id, role, checked) => {
          update(id, { [role]: checked });
        }}
        onPrimary={setPrimaryId}
      />
      <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
        The primary Data account cannot be switched off — pick another primary first. Mode and risk profile are set on
        the account&apos;s Trading tab.
      </Typography>
      {result?.cancelled && (
        <Alert severity="warning" sx={{ mt: 2 }}>
          Password not confirmed — the remaining changes were not saved.
        </Alert>
      )}
      <Button
        variant="contained"
        sx={{ mt: 2 }}
        loading={saving}
        disabled={!dirty}
        onClick={() => {
          onSave({ accounts, drafts, primaryId });
        }}
      >
        Save roles
      </Button>
    </Box>
  );
}
