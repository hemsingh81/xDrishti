import { Alert, Button, Stack, Typography } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { CorporateAction } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useAddAction, useUpdateAction } from '../api/actionMutations';
import { actionName } from '../lib/actions';
import {
  emptyActionForm,
  formOfAction,
  toActionInput,
  toActionUpdate,
  validateActionForm,
  type ActionForm,
} from '../lib/actionForm';
import { actionErrorText, fieldErrors, isCsvAlreadyAdjusted } from '../lib/errors';
import { ActionFormFields } from './ActionFormFields';
import { CsvAdjustedConfirm } from './CsvAdjustedConfirm';
import { InstrumentPicker } from './InstrumentPicker';

const shownFields = ['instrumentId', 'exDate', 'kind', 'numerator', 'denominator', 'note'];

interface ActionDialogProps {
  /** The action to edit; left out, the dialog adds a new one. */
  action?: CorporateAction;
  onClose: () => void;
}

/**
 * Add an action by hand (it is applied at once) or edit one: a suggestion or an ignored action becomes applied when
 * saved. The server's messages show on their fields; a repeat (409) is said in words. An ex-date inside CSV history that
 * is adjusted already is refused with a 409 first; "Adjust anyway" sends the same request again with the owner's consent.
 */
export function ActionDialog({ action, onClose }: ActionDialogProps) {
  const { notify } = useToast();
  const add = useAddAction();
  const update = useUpdateAction();
  const mutation = action ? update : add;
  const dateRef = useRef<HTMLInputElement>(null);
  const [form, setForm] = useState<ActionForm>(action ? formOfAction(action) : emptyActionForm);
  const [symbol, setSymbol] = useState<string | null>(action?.symbol ?? null);
  const [clientErrors, setClientErrors] = useState<Record<string, string>>({});
  // The 409 about CSV history is answered by its own dialog, not by the red alert.
  const adjustedTwice = mutation.isError && isCsvAlreadyAdjusted(mutation.error);
  const serverErrors = fieldErrors(mutation.error);
  const errors = { ...serverErrors, ...clientErrors };
  const others = Object.entries(serverErrors).filter(([field]) => !shownFields.includes(field));
  const problem =
    mutation.isError && !adjustedTwice && Object.keys(serverErrors).length === 0
      ? actionErrorText(mutation.error, 'The action could not be saved.')
      : null;
  const applies = action?.status !== 'applied';

  const change = (patch: Partial<ActionForm>) => {
    setForm((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    add.reset();
    update.reset();
  };

  /** Sends the form; `acknowledge` is the owner's consent to adjust history that is adjusted already. */
  const save = async (acknowledge: boolean) => {
    const consent = acknowledge ? { acknowledgeCsvAdjusted: true } : {};
    try {
      const saved = action
        ? await update.mutateAsync({ id: action.id, body: { ...toActionUpdate(form), ...consent } })
        : await add.mutateAsync({ ...toActionInput(form), ...consent });
      notify(`${action ? 'Saved' : 'Added'} ${actionName(saved)}; it is applied to adjusted prices.`);
      onClose();
    } catch {
      // The fields, the alert or the confirmation show what the server said.
    }
  };

  const submit = async (event: SyntheticEvent) => {
    event.preventDefault();
    const found = validateActionForm(form, { needsInstrument: !action });
    setClientErrors(found);
    if (Object.keys(found).length > 0) return;
    await save(false);
  };

  return (
    <>
      <AppDialog
        open
        onClose={onClose}
        busy={mutation.isPending}
        initialFocusRef={dateRef}
        onSubmit={(event) => {
          void submit(event);
        }}
      >
        <AppDialogHeader>{action ? 'Edit corporate action' : 'Add corporate action'}</AppDialogHeader>
        <AppDialogBody>
          <Stack spacing={2.5}>
            {problem && <Alert severity="error">{problem}</Alert>}
            {others.length > 0 && (
              <Alert severity="error">{others.map(([field, message]) => `${field}: ${message}`).join(' ')}</Alert>
            )}
            {action ? (
              <Typography>
                <strong>{action.symbol}</strong> {action.name}
              </Typography>
            ) : (
              <InstrumentPicker
                value={form.instrumentId}
                error={errors.instrumentId}
                onChange={(id, name) => {
                  setSymbol(name);
                  change({ instrumentId: id });
                }}
              />
            )}
            <ActionFormFields
              form={form}
              errors={errors}
              csvAdjustedThrough={action?.csvAdjustedThrough}
              onChange={change}
              dateRef={dateRef}
            />
            {action && applies && (
              <Typography variant="body2" color="text.secondary">
                Saving applies this action: adjusted prices use it from then on.
              </Typography>
            )}
          </Stack>
        </AppDialogBody>
        <AppDialogFooter>
          <Button onClick={onClose} disabled={mutation.isPending}>
            Cancel
          </Button>
          <Button type="submit" variant="contained" loading={mutation.isPending}>
            {action ? (applies ? 'Save and apply' : 'Save') : 'Add'}
          </Button>
        </AppDialogFooter>
      </AppDialog>
      <CsvAdjustedConfirm
        open={adjustedTwice}
        symbol={symbol}
        through={action?.csvAdjustedThrough ?? null}
        exDate={form.exDate}
        error={mutation.error}
        onConfirm={() => {
          void save(true);
        }}
        onCancel={mutation.reset}
      />
    </>
  );
}
