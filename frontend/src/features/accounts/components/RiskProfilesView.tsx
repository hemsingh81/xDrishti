import AddIcon from '@mui/icons-material/Add';
import { Alert, Button, Grid, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { RiskProfile } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { riskProfilesQuery } from '../api/accountsQueries';
import { useDeleteRiskProfile } from '../api/riskProfileMutations';
import { problemMessage } from '../lib/fieldErrors';
import { RiskProfileCard } from './RiskProfileCard';
import { RiskProfileDialog, type RiskProfileDialogMode } from './RiskProfileDialog';

/** Shared risk profiles: every limit, who uses it, create (optionally from an existing one), edit, delete. */
export function RiskProfilesView() {
  const { notify } = useToast();
  const { data, error, isPending } = useQuery(riskProfilesQuery);
  const remove = useDeleteRiskProfile();
  const [dialog, setDialog] = useState<RiskProfileDialogMode | null>(null);
  const [deleting, setDeleting] = useState<RiskProfile | null>(null);

  function closeDelete() {
    setDeleting(null);
    remove.reset();
  }

  if (isPending) {
    return <LoadingState label="Loading risk profiles" />;
  }
  if (error) {
    return <ErrorState error={error} />;
  }
  const message = problemMessage(remove.error);

  return (
    <Stack spacing={2}>
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={1}
        sx={{ justifyContent: 'space-between', alignItems: { sm: 'center' } }}
      >
        <Typography color="text.secondary">
          Shared by Trading accounts; each account can override a few values on its Trading tab.
        </Typography>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={() => {
            setDialog({ kind: 'create' });
          }}
        >
          New profile
        </Button>
      </Stack>
      <Grid container spacing={2}>
        {data.map((profile) => (
          <Grid key={profile.id} size={{ xs: 12, md: 6, xl: 4 }}>
            <RiskProfileCard
              profile={profile}
              onEdit={() => {
                setDialog({ kind: 'edit', profile });
              }}
              onDuplicate={() => {
                setDialog({ kind: 'create', startFrom: profile });
              }}
              onDelete={() => {
                setDeleting(profile);
              }}
            />
          </Grid>
        ))}
      </Grid>
      {dialog && (
        <RiskProfileDialog
          mode={dialog}
          profiles={data}
          onClose={() => {
            setDialog(null);
          }}
        />
      )}
      <ConfirmDialog
        open={deleting !== null}
        title={`Delete ${deleting?.name ?? 'profile'}?`}
        confirmLabel="Delete profile"
        destructive
        busy={remove.isPending}
        onCancel={closeDelete}
        onConfirm={() => {
          if (!deleting) {
            return;
          }
          remove.mutate(deleting.id, {
            onSuccess: () => {
              notify(`Risk profile “${deleting.name}” deleted.`);
              closeDelete();
            },
          });
        }}
      >
        This cannot be undone. You will be asked for your password.
        {message && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {message}
          </Alert>
        )}
      </ConfirmDialog>
    </Stack>
  );
}
