import { Alert, Button, Stack } from '@mui/material';
import { useState } from 'react';
import type { CorporateAction } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { ConfirmDialog } from '@/shared/ui/ConfirmDialog';
import { useToast } from '@/shared/ui/toastContext';
import { useIgnoreAction, useUpdateAction } from '../api/actionMutations';
import { actionName } from '../lib/actions';
import { confirmAsIs } from '../lib/actionForm';
import { actionErrorText, isCsvAlreadyAdjusted } from '../lib/errors';
import { ActionDialog } from './ActionDialog';
import { CsvAdjustedConfirm } from './CsvAdjustedConfirm';

type Pending = 'adjust' | 'edit' | 'ignore' | null;

/**
 * The buttons of one row. A suggestion: Confirm (applies it as it is), Edit (change, then apply) and Ignore (declines
 * it, it is not suggested again). An applied action: Edit and Ignore (take back). An ignored one: Apply and Edit.
 * Taking an applied action back asks first. Confirming or applying is sent as it is; when the API refuses it because
 * the ex-date is inside CSV history that is adjusted already (409), "Adjust anyway" sends it again with the owner's
 * consent.
 */
export function ActionRowActions({ action }: { action: CorporateAction }) {
  const { notify } = useToast();
  const confirm = useUpdateAction();
  const ignore = useIgnoreAction();
  const [pending, setPending] = useState<Pending>(null);
  const busy = confirm.isPending || ignore.isPending;
  const name = actionName(action);
  const applied = action.status === 'applied';
  const suggested = action.status === 'suggested';
  const verb = suggested ? 'Confirm' : 'Apply';

  const close = () => {
    setPending(null);
    confirm.reset();
    ignore.reset();
  };

  // The row may leave the filtered list when the reload ends, so the answers are handled here, not by the row.
  const run = async (task: () => Promise<unknown>, done: string, failed: string) => {
    try {
      await task();
      setPending(null);
      notify(done);
    } catch (error) {
      notify(actionErrorText(error, failed), 'error');
    }
  };
  const apply = async (acknowledge = false) => {
    try {
      await confirm.mutateAsync({
        id: action.id,
        body: acknowledge ? { ...confirmAsIs, acknowledgeCsvAdjusted: true } : confirmAsIs,
      });
      setPending(null);
      notify(`${suggested ? 'Confirmed' : 'Applied'} ${name}; adjusted prices use it now.`);
    } catch (error) {
      if (isCsvAlreadyAdjusted(error)) setPending('adjust');
      // After "Adjust anyway" the dialog stays open and says what went wrong; otherwise a toast does.
      else if (!acknowledge)
        notify(actionErrorText(error, `The action could not be ${suggested ? 'confirmed' : 'applied'}.`), 'error');
    }
  };
  const decline = () =>
    run(
      () => ignore.mutateAsync(action.id),
      applied ? `Took back ${name}; prices are no longer adjusted for it.` : `Ignored ${name}.`,
      'The action could not be ignored.',
    );

  return (
    <Stack direction="row" spacing={0.5} sx={{ justifyContent: 'flex-end' }}>
      {!applied && (
        <Button
          size="small"
          variant="outlined"
          aria-label={`${verb} ${name}`}
          loading={confirm.isPending}
          disabled={busy}
          onClick={() => {
            void apply();
          }}
        >
          {verb}
        </Button>
      )}
      <Button
        size="small"
        color="inherit"
        aria-label={`Edit ${name}`}
        disabled={busy}
        onClick={() => {
          setPending('edit');
        }}
      >
        Edit
      </Button>
      {action.status !== 'ignored' && (
        <Button
          size="small"
          color="inherit"
          aria-label={`${applied ? 'Ignore (take back)' : 'Ignore'} ${name}`}
          loading={ignore.isPending}
          disabled={busy}
          onClick={() => {
            if (applied) setPending('ignore');
            else void decline();
          }}
        >
          {applied ? 'Ignore (take back)' : 'Ignore'}
        </Button>
      )}
      {pending === 'edit' && <ActionDialog action={action} onClose={close} />}
      <CsvAdjustedConfirm
        open={pending === 'adjust'}
        symbol={action.symbol}
        through={action.csvAdjustedThrough ?? null}
        exDate={action.exDate}
        error={confirm.error}
        busy={confirm.isPending}
        failure={
          confirm.isError && !isCsvAlreadyAdjusted(confirm.error)
            ? actionErrorText(confirm.error, `The action could not be ${suggested ? 'confirmed' : 'applied'}.`)
            : null
        }
        onConfirm={() => {
          void apply(true);
        }}
        onCancel={close}
      />
      <ConfirmDialog
        open={pending === 'ignore'}
        title={`Take back ${action.symbol} ${action.kind} ${action.ratio}?`}
        confirmLabel="Ignore"
        destructive
        busy={ignore.isPending}
        onConfirm={() => {
          void decline();
        }}
        onCancel={close}
      >
        Prices before {formatDate(action.exDate)} are no longer adjusted for it. The action stays in the list as ignored
        and is not suggested again.
        {ignore.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {actionErrorText(ignore.error, 'The action could not be ignored.')}
          </Alert>
        )}
      </ConfirmDialog>
    </Stack>
  );
}
