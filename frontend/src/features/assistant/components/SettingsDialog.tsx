import { Alert, Button, FormControlLabel, Stack, Switch, TextField, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import { ApiError } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useChangeSettings, type AssistantStatus } from '../api/assistantApi';
import { words } from '../lib/wording';
import { DraftSwitches } from './DraftSwitches';
import { ProposalSwitches } from './ProposalSwitches';

const maxReason = 500;

interface FormProblem {
  field: 'password' | 'other';
  text: string;
}

function problemOf(error: unknown): FormProblem {
  if (!(error instanceof ApiError)) return { field: 'other', text: 'Could not reach the server. Nothing was changed.' };
  const p = error.problem;
  switch (error.status) {
    case 401:
      return { field: 'password', text: 'The password was not accepted. Nothing was changed.' };
    case 423: {
      const wait = typeof p?.retryAfterSeconds === 'number' ? ` Try again in ${p.retryAfterSeconds} seconds.` : '';
      return { field: 'password', text: `Too many wrong passwords; this is locked for now.${wait}` };
    }
    case 409:
      return {
        field: 'other',
        text: 'The settings were changed elsewhere. The form now shows the current values; review and save again.',
      };
    default:
      return { field: 'other', text: `${p?.title ?? 'The request failed.'} Nothing was changed.` };
  }
}

function SettingsForm({ status, onClose }: { status: AssistantStatus; onClose: () => void }) {
  const [enabled, setEnabled] = useState(status.enabled);
  const [portfolio, setPortfolio] = useState(status.portfolioContext);
  const [maxCalls, setMaxCalls] = useState(String(status.maxToolCalls));
  const [draftConfirm, setDraftConfirm] = useState(status.allowDraftConfirm);
  const [autoQuick, setAutoQuick] = useState(status.autoLabQuickTest);
  const [proposalAccept, setProposalAccept] = useState(status.allowProposalAccept);
  const [autoHypothesis, setAutoHypothesis] = useState(status.autoAcceptHypothesis);
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<FormProblem | null>(null);
  const change = useChangeSettings();
  const { notify } = useToast();
  const firstField = useRef<HTMLInputElement>(null);

  // A stale form (409) reloads the status: when the stored row changes, show its values (state derived during render).
  const [seenId, setSeenId] = useState(status.settingsId);
  if (seenId !== status.settingsId) {
    setSeenId(status.settingsId);
    setEnabled(status.enabled);
    setPortfolio(status.portfolioContext);
    setMaxCalls(String(status.maxToolCalls));
    setDraftConfirm(status.allowDraftConfirm);
    setAutoQuick(status.autoLabQuickTest);
    setProposalAccept(status.allowProposalAccept);
    setAutoHypothesis(status.autoAcceptHypothesis);
  }

  const calls = Number(maxCalls);
  const callsOk = Number.isInteger(calls) && calls >= 1 && calls <= 12;
  const raises =
    (enabled && !status.enabled) ||
    (portfolio && !status.portfolioContext) ||
    (callsOk && calls > status.maxToolCalls) ||
    (draftConfirm && !status.allowDraftConfirm) ||
    (autoQuick && !status.autoLabQuickTest) ||
    (proposalAccept && !status.allowProposalAccept) ||
    (autoHypothesis && !status.autoAcceptHypothesis);
  const changed =
    enabled !== status.enabled ||
    portfolio !== status.portfolioContext ||
    draftConfirm !== status.allowDraftConfirm ||
    autoQuick !== status.autoLabQuickTest ||
    proposalAccept !== status.allowProposalAccept ||
    autoHypothesis !== status.autoAcceptHypothesis ||
    (callsOk && calls !== status.maxToolCalls);
  const ready =
    changed &&
    callsOk &&
    reason.trim().length > 0 &&
    reason.length <= maxReason &&
    (!raises || password.length > 0) &&
    !change.isPending;

  // Cleared after every try, right or wrong; never kept (the mutation state would hold the variables, password included).
  const settle = () => {
    setPassword('');
    change.reset();
  };

  const save = () => {
    setProblem(null);
    change.mutate(
      {
        enabled,
        portfolioContext: portfolio,
        maxToolCalls: calls,
        allowDraftConfirm: draftConfirm,
        autoLabQuickTest: autoQuick,
        allowProposalAccept: proposalAccept,
        autoAcceptHypothesis: autoHypothesis,
        reason,
        expectedSettingsId: status.settingsId ?? 0,
        // The password travels only for a change that turns something on or raises it.
        password: raises ? password : null,
      },
      {
        onSuccess: () => {
          settle();
          notify(words.settingsSaved, 'success');
          onClose();
        },
        onError: (error) => {
          settle();
          setProblem(problemOf(error));
        },
      },
    );
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={change.isPending}
      initialFocusRef={firstField}
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) save();
      }}
    >
      <AppDialogHeader>Assistant settings</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <FormControlLabel
            control={
              <Switch
                checked={enabled}
                slotProps={{ input: { ref: firstField } }}
                onChange={(e) => {
                  setEnabled(e.target.checked);
                }}
              />
            }
            label="Assistant"
          />
          <div>
            <FormControlLabel
              control={
                <Switch
                  checked={portfolio}
                  onChange={(e) => {
                    setPortfolio(e.target.checked);
                  }}
                />
              }
              label="Portfolio data"
            />
            {!portfolio && (
              <Typography variant="body2" color="text.secondary">
                {words.portfolioOff}
              </Typography>
            )}
          </div>
          <DraftSwitches
            draftConfirm={draftConfirm}
            onDraftConfirm={setDraftConfirm}
            autoQuick={autoQuick}
            onAutoQuick={setAutoQuick}
          />
          <ProposalSwitches
            accept={proposalAccept}
            onAccept={setProposalAccept}
            auto={autoHypothesis}
            onAuto={setAutoHypothesis}
          />
          <TextField
            label="Maximum tool calls"
            type="number"
            value={maxCalls}
            onChange={(e) => {
              setMaxCalls(e.target.value);
            }}
            error={!callsOk}
            helperText="1 to 12 per question"
            slotProps={{ htmlInput: { min: 1, max: 12 } }}
          />
          <TextField
            label="Reason"
            value={reason}
            onChange={(e) => {
              setReason(e.target.value);
            }}
            required
            multiline
            minRows={2}
            error={reason.length > maxReason}
            helperText={`${reason.length} of ${maxReason} characters`}
          />
          {raises ? (
            <TextField
              label="Your password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => {
                setPassword(e.target.value);
              }}
              required
              error={problem?.field === 'password'}
              helperText={
                problem?.field === 'password'
                  ? problem.text
                  : 'Asked again for every change that turns something on or raises it.'
              }
            />
          ) : (
            <Typography variant="body2" color="text.secondary">
              {words.noPasswordOff}
            </Typography>
          )}
          {problem?.field === 'other' && <Alert severity="error">{problem.text}</Alert>}
          {problem?.field === 'password' && !raises && <Alert severity="error">{problem.text}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={change.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready}>
          Save
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}

/** The owner act: switches and the tool-call budget. A password is asked only when the change turns something on or raises it. */
export function SettingsDialog({
  open,
  status,
  onClose,
}: {
  open: boolean;
  status: AssistantStatus;
  onClose: () => void;
}) {
  return open ? <SettingsForm status={status} onClose={onClose} /> : null;
}
