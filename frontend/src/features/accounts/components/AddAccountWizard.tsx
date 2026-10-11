import { Box, Button, Stack } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type SyntheticEvent } from 'react';
import type { AccountDetail, AccountSummary } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useCreateAccount } from '../api/accountMutations';
import { bucketsQuery, riskProfilesQuery } from '../api/accountsQueries';
import { useStartConnect } from '../api/connectionMutations';
import { connectionProblem } from '../lib/connectErrors';
import { fieldErrorsOf, problemMessage, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import { defaultProfileId } from '../lib/tradingForm';
import {
  emptyBrokerApp,
  initialWizardState,
  lastStep,
  stepOfField,
  suggestName,
  toCreateInput,
  validateStep,
  wizardSteps,
  type WizardState,
} from '../lib/wizardState';
import { BrokerAppStep } from './wizard/BrokerAppStep';
import { IdentityStep } from './wizard/IdentityStep';
import { ReviewStep } from './wizard/ReviewStep';
import { RolesStep } from './wizard/RolesStep';
import { SettingsStep } from './wizard/SettingsStep';
import { WizardProgress } from './wizard/WizardProgress';

interface AddAccountWizardProps {
  accounts: AccountSummary[];
  onClose: () => void;
  onCreated: (accountId: string) => void;
}

/**
 * Add-account wizard: Dhan app → identity → roles → role settings → review. Nothing is saved until the last
 * step (all or nothing, password confirmed on submit); server field errors jump back to the step that owns the field.
 * The API key and secret exist only in this component's state and are dropped when it closes. "Create and connect"
 * then starts the broker sign-in for the new account (the same password confirmation covers both).
 */
export function AddAccountWizard({ accounts, onClose, onCreated }: AddAccountWizardProps) {
  const { notify } = useToast();
  const create = useCreateAccount();
  const connect = useStartConnect();
  // After a successful start the browser is on its way to the broker: stay busy until it leaves.
  const busy = create.isPending || connect.isPending || connect.isSuccess;
  const [connectAfter, setConnectAfter] = useState(false);
  const profiles = useQuery(riskProfilesQuery);
  const buckets = useQuery(bucketsQuery);
  const [state, setState] = useState<WizardState>(() => initialWizardState(accounts));
  const [step, setStep] = useState(0);
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const [bodyRef, setBodyRef] = useState<HTMLElement | null>(null);
  const errors = { ...fieldErrorsOf(create.error), ...clientErrors };

  function change(patch: Partial<WizardState>) {
    setState((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    create.reset();
  }

  function close() {
    setState((previous) => ({ ...previous, app: emptyBrokerApp() }));
    create.reset();
    onClose();
  }

  function goTo(next: number) {
    setStep(next);
    setClientErrors({});
  }

  /** Values that depend on earlier answers are filled in when their step is reached. */
  function prepare(next: number) {
    if (next === 1 && !state.nameEdited) {
      setState((s) => ({ ...s, identity: { ...s.identity, name: suggestName(accounts) } }));
    }
    if (next === 3) {
      setState((s) => ({
        ...s,
        trading: { ...s.trading, riskProfileId: s.trading.riskProfileId || defaultProfileId(profiles.data ?? []) },
        portfolio: {
          ...s.portfolio,
          defaultBucket:
            s.portfolio.defaultBucket || (buckets.data?.some((b) => b.name === 'Trading') ? 'Trading' : ''),
        },
      }));
    }
  }

  function finish(account: AccountDetail) {
    setState((previous) => ({ ...previous, app: emptyBrokerApp() }));
    create.reset();
    onCreated(account.id);
  }

  /** The account exists either way; if the sign-in cannot start, it stays and the owner connects later. */
  function connectNew(account: AccountDetail) {
    // The secrets are stored: drop the typed values now, not only when the dialog closes.
    setState((previous) => ({ ...previous, app: emptyBrokerApp() }));
    create.reset();
    connect.mutate(account.id, {
      onError: (error) => {
        const reason = connectionProblem(error);
        notify(
          reason
            ? `${account.name} was added, but connecting failed: ${reason}`
            : `${account.name} was added. Connect it from the Connection tab.`,
          reason ? 'error' : 'success',
        );
        finish(account);
      },
    });
  }

  function submit(event: SyntheticEvent, andConnect = false) {
    event.preventDefault();
    const found = validateStep(step, state);
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstInvalid(bodyRef);
      return;
    }
    if (step < lastStep) {
      prepare(step + 1);
      goTo(step + 1);
      return;
    }
    setConnectAfter(andConnect);
    create.mutate(toCreateInput(state), {
      onSuccess: (account) => {
        if (andConnect) {
          connectNew(account);
          return;
        }
        notify(`${account.name} was added. Connect it from the Connection tab.`);
        finish(account);
      },
      onError: (error) => {
        const fields = Object.keys(fieldErrorsOf(error));
        if (fields.length > 0) {
          setStep(Math.min(...fields.map(stepOfField)));
          focusFirstInvalid(bodyRef);
        }
      },
    });
  }

  const stepProps = { state, errors, onChange: change };
  return (
    <AppDialog open onClose={close} busy={busy} maxWidth="md" fullScreenOnMobile onSubmit={submit}>
      <AppDialogHeader>Add Dhan account</AppDialogHeader>
      <AppDialogBody ref={setBodyRef}>
        <Stack spacing={2.5}>
          <WizardProgress step={step} />
          <Box role="group" aria-label={wizardSteps[step]}>
            {step === 0 && (
              <BrokerAppStep
                app={state.app}
                errors={errors}
                onChange={(patch) => {
                  change({ app: { ...state.app, ...patch } });
                }}
              />
            )}
            {step === 1 && (
              <IdentityStep
                identity={state.identity}
                errors={errors}
                suggested={!state.nameEdited && state.identity.name !== ''}
                onChange={(patch) => {
                  change({
                    identity: { ...state.identity, ...patch },
                    nameEdited: state.nameEdited || 'name' in patch,
                  });
                }}
              />
            )}
            {step === 2 && <RolesStep {...stepProps} />}
            {step === 3 && <SettingsStep {...stepProps} />}
            {step === 4 && <ReviewStep state={state} message={problemMessage(create.error)} />}
          </Box>
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={close} disabled={busy}>
          Cancel
        </Button>
        <Box sx={{ flex: 1 }} />
        <Button
          onClick={() => {
            goTo(step - 1);
          }}
          disabled={step === 0 || busy}
        >
          Back
        </Button>
        {step === lastStep && (
          <Button
            variant="outlined"
            loading={busy && connectAfter}
            disabled={busy}
            onClick={(event) => {
              submit(event, true);
            }}
          >
            Create and connect
          </Button>
        )}
        <Button type="submit" variant="contained" loading={busy && !connectAfter} disabled={busy}>
          {step === lastStep ? 'Create account' : 'Next'}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
