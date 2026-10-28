import { Alert, Box, MenuItem, Stack, TextField, Typography, Button } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useMemo, useRef, useState } from 'react';
import { api, unwrap } from '@/shared/api';
import { istDayIso } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { complianceKey } from '../api/complianceQueries';
import { StaticIpFields } from './StaticIpFields';
import { addDays } from '../lib/dates';
import { maxSummary, reviewDays, saveProblem, validate, type Fields } from '../lib/complianceForm';
import { kindLabels, kinds, noteSentence, outcomesOf, type Kind } from '../lib/labels';

/**
 * The only write of the live feature (spec P11-05): saves one compliance record. The password is typed again for THIS save; it lives in the variables of the call
 * only (the mutation is dropped at once, `gcTime` 0) and the field is cleared after every try. Nothing here calls a broker.
 */
export function ComplianceDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const today = useMemo(() => istDayIso(), []);
  const [f, setF] = useState<Fields>({
    kind: 'sebi_recheck',
    outcome: 'rechecked_no_change',
    decidedOn: today,
    summary: '',
    sources: '',
    staticIpState: 'none',
    isp: '',
    monthlyCost: '',
    ipVersion: '',
    whitelistedAt: '',
    nextReviewOn: addDays(today, reviewDays),
  });
  const [password, setPassword] = useState('');
  const [touched, setTouched] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const passwordRef = useRef<HTMLInputElement>(null);
  const save = useMutation({
    gcTime: 0,
    mutationFn: async (input: { fields: Fields; password: string }) => {
      const { fields: x } = input;
      const staticIp = x.kind === 'static_ip';
      return unwrap(
        await api.POST('/api/live-trading/compliance', {
          body: {
            kind: x.kind,
            outcome: x.outcome,
            decidedOn: x.decidedOn,
            summary: x.summary,
            sources: x.sources
              .split('\n')
              .map((s) => s.trim())
              .filter(Boolean),
            staticIpState: staticIp ? x.staticIpState : null,
            isp: staticIp && x.isp.trim() ? x.isp.trim() : null,
            monthlyCost: staticIp && x.monthlyCost.trim() ? Number(x.monthlyCost) : null,
            ipVersion: staticIp && x.ipVersion ? x.ipVersion : null,
            whitelistedAt: staticIp && x.staticIpState === 'whitelisted' ? x.whitelistedAt : null,
            nextReviewOn: x.nextReviewOn,
            password: input.password,
          },
        }),
      );
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: complianceKey });
      void queryClient.invalidateQueries({ queryKey: ['plans', 'notices'] });
    },
  });
  const errors = validate(f, today);
  const invalid = Object.keys(errors).length > 0;
  const set = <K extends keyof Fields>(name: K, value: Fields[K]) => {
    setF((prev) => ({ ...prev, [name]: value }));
  };
  const shown = (name: keyof Fields) => (touched ? errors[name] : undefined);

  const submit = async () => {
    setTouched(true);
    if (invalid || password.length === 0 || save.isPending) return;
    setProblem(null);
    try {
      await save.mutateAsync({ fields: f, password });
      setSaved(true);
    } catch (error) {
      setProblem(saveProblem(error));
      passwordRef.current?.focus();
    } finally {
      setPassword('');
      save.reset();
    }
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={save.isPending}
      maxWidth="md"
      fullScreenOnMobile
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      <AppDialogHeader>New compliance record</AppDialogHeader>
      <AppDialogBody>
        {saved ? (
          <Alert severity="success">
            Saved. The record is in the list; it cannot be edited, only followed by a newer one.
          </Alert>
        ) : (
          <Stack sx={{ gap: 2, pt: 1 }}>
            <Typography variant="body2">{noteSentence}</Typography>
            <Typography variant="body2" color="text.secondary">
              Never write an IP address, a client id or a token: they are refused.
            </Typography>
            <TextField
              select
              label="Kind"
              value={f.kind}
              onChange={(e) => {
                const kind = e.target.value as Kind;
                setF((prev) => ({ ...prev, kind, outcome: outcomesOf[kind][0]?.value ?? '' }));
              }}
            >
              {kinds.map((k) => (
                <MenuItem key={k} value={k}>
                  {kindLabels[k]}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label="Outcome"
              value={f.outcome}
              onChange={(e) => {
                set('outcome', e.target.value);
              }}
            >
              {outcomesOf[f.kind].map((o) => (
                <MenuItem key={o.value} value={o.value}>
                  {o.label}
                </MenuItem>
              ))}
            </TextField>
            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' } }}>
              <TextField
                label="Decided on"
                type="date"
                value={f.decidedOn}
                onChange={(e) => {
                  set('decidedOn', e.target.value);
                }}
                slotProps={{ inputLabel: { shrink: true }, htmlInput: { max: today } }}
                error={Boolean(shown('decidedOn'))}
                helperText={shown('decidedOn')}
              />
              <TextField
                label="Review again on"
                type="date"
                value={f.nextReviewOn}
                onChange={(e) => {
                  set('nextReviewOn', e.target.value);
                }}
                slotProps={{ inputLabel: { shrink: true } }}
                error={Boolean(shown('nextReviewOn'))}
                helperText={shown('nextReviewOn') ?? 'About 90 days ahead.'}
              />
            </Box>
            <TextField
              label="What you were told or read"
              value={f.summary}
              onChange={(e) => {
                set('summary', e.target.value);
              }}
              multiline
              minRows={3}
              required
              error={Boolean(shown('summary')) || f.summary.length > maxSummary}
              helperText={shown('summary') ?? `${String(f.summary.length)} of ${String(maxSummary)} characters`}
            />
            <TextField
              label="Sources (one per line)"
              value={f.sources}
              onChange={(e) => {
                set('sources', e.target.value);
              }}
              multiline
              minRows={2}
              error={Boolean(shown('sources'))}
              helperText={
                shown('sources') ?? 'https:// links or circular ids such as NSE/INVG/73992. The app opens none of them.'
              }
            />
            {f.kind === 'static_ip' && <StaticIpFields fields={f} errors={touched ? errors : {}} set={set} />}
            <TextField
              label="Your password for this save"
              type="password"
              autoComplete="current-password"
              value={password}
              inputRef={passwordRef}
              onChange={(e) => {
                setPassword(e.target.value);
              }}
              required
              error={touched && password.length === 0}
              helperText={
                touched && password.length === 0
                  ? 'Type your password.'
                  : 'Asked again for every save. A wrong one counts toward the sign-in lock.'
              }
            />
            {problem && <Alert severity="error">{problem}</Alert>}
          </Stack>
        )}
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={save.isPending}>
          {saved ? 'Close' : 'Cancel'}
        </Button>
        {!saved && (
          <Button type="submit" variant="contained" disabled={save.isPending}>
            Save record
          </Button>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
