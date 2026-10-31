import { Alert, Box, Checkbox, FormControlLabel, FormGroup, Stack, TextField, Typography, Button } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type SyntheticEvent } from 'react';
import { ApiError, type Schemas } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { problemText } from '../api/confirmApi';
import { limitsQuery } from '../api/viewsApi';
import { useChangeLimits } from '../api/writesApi';
import { aboveCeiling, bodyOf, draftOf, numericFields, raisedFields, type LimitsDraft } from '../lib/limitsDraft';
import { RetryError } from './RetryError';

type Row = Schemas['LimitsRowView'];

function SheetInForce({ row, missing }: { row: Row | null; missing: readonly string[] }) {
  if (!row) return <Typography>Empty: nothing may be sent automatically</Typography>;
  return (
    <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: 'auto 1fr', gap: 0.5, columnGap: 2 }}>
      {numericFields.map(([key, label]) => (
        <Box key={key} sx={{ display: 'contents' }}>
          <dt>{label}</dt>
          <dd style={{ margin: 0 }}>{row[key] ?? 'unset (allows nothing)'}</dd>
        </Box>
      ))}
      <dt>Allow-list</dt>
      <dd style={{ margin: 0 }}>{row.allowList.length > 0 ? row.allowList.join(', ') : 'empty (allows nothing)'}</dd>
      <dt>Window (IST)</dt>
      <dd style={{ margin: 0 }}>
        {row.windowStart && row.windowEnd ? `${row.windowStart} to ${row.windowEnd}` : 'unset (allows nothing)'}
      </dd>
      <dt>Products</dt>
      <dd style={{ margin: 0 }}>{row.products.join(', ') || 'none'}</dd>
      {missing.length > 0 && (
        <>
          <dt>Missing</dt>
          <dd style={{ margin: 0 }}>{missing.join(', ')}: an incomplete sheet allows nothing</dd>
        </>
      )}
    </Box>
  );
}

function Form({ accountId, view }: { accountId: string; view: Schemas['LimitsView'] }) {
  const change = useChangeLimits();
  const [draft, setDraft] = useState<LimitsDraft>(() => draftOf(view.inForce));
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const raised = raisedFields(draft, view.inForce);
  const over = aboveCeiling(draft, view.ceilings);
  const set = <K extends keyof LimitsDraft>(key: K, value: LimitsDraft[K]) => {
    setDraft((d) => ({ ...d, [key]: value }));
  };
  const fieldErrors = change.error instanceof ApiError ? (change.error.problem?.errors ?? {}) : {};
  const errorOf = (key: string) =>
    Object.entries(fieldErrors)
      .find(([k]) => k.toLowerCase() === key.toLowerCase())?.[1]
      ?.join(' ');
  const props = (key: string) => {
    const note =
      errorOf(key) ?? (over.includes(key) ? 'Above the ceiling' : raised.includes(key) ? 'Raises risk' : undefined);
    return {
      color: raised.includes(key) ? ('warning' as const) : undefined,
      error: Boolean(errorOf(key)) || over.includes(key),
      helperText: note,
    };
  };
  const answer = change.data;
  const submit = (e: SyntheticEvent) => {
    e.preventDefault();
    if (over.length > 0 || reason.trim() === '' || (raised.length > 0 && password === '')) return;
    change.mutate(
      { accountId, body: bodyOf(draft, reason.trim(), password) },
      {
        onSuccess: () => {
          setPassword('');
        },
      },
    );
  };
  return (
    <Box component="form" onSubmit={submit} aria-label="Limits form" sx={{ display: 'grid', gap: 1.5 }}>
      <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)' } }}>
        {numericFields.map(([key, label]) => (
          <TextField
            key={key}
            size="small"
            type="number"
            label={label}
            value={draft[key]}
            onChange={(e) => {
              set(key, e.target.value);
            }}
            slotProps={{
              htmlInput: {
                min: 0,
                ...(key === 'maxOrdersPerDay' ? { max: view.ceilings.maxOrdersPerDay } : {}),
                ...(key === 'maxOpenPositions' ? { max: view.ceilings.maxOpenPositions } : {}),
              },
            }}
            {...props(key)}
          />
        ))}
        <TextField
          size="small"
          label="Allow-list (symbols)"
          value={draft.allowList}
          onChange={(e) => {
            set('allowList', e.target.value);
          }}
          {...props('allowList')}
        />
        <TextField
          size="small"
          type="time"
          label="Window start (IST)"
          value={draft.windowStart}
          onChange={(e) => {
            set('windowStart', e.target.value);
          }}
          slotProps={{
            inputLabel: { shrink: true },
            htmlInput: { min: view.ceilings.windowEarliest, max: view.ceilings.windowLatest },
          }}
          {...props('windowStart')}
        />
        <TextField
          size="small"
          type="time"
          label="Window end (IST)"
          value={draft.windowEnd}
          onChange={(e) => {
            set('windowEnd', e.target.value);
          }}
          slotProps={{
            inputLabel: { shrink: true },
            htmlInput: { min: view.ceilings.windowEarliest, max: view.ceilings.windowLatest },
          }}
          {...props('windowEnd')}
        />
      </Box>
      <FormGroup row aria-label="Products">
        {view.ceilings.products.map((p) => (
          <FormControlLabel
            key={p}
            label={p}
            control={
              <Checkbox
                checked={draft.products.includes(p)}
                onChange={(e) => {
                  set('products', e.target.checked ? [...draft.products, p] : draft.products.filter((x) => x !== p));
                }}
              />
            }
          />
        ))}
      </FormGroup>
      <FormControlLabel
        label="Exit all positions when the loss cap halts"
        control={
          <Checkbox
            checked={draft.exitAllOnLossHalt}
            onChange={(e) => {
              set('exitAllOnLossHalt', e.target.checked);
            }}
          />
        }
      />
      <TextField
        size="small"
        label="Reason"
        required
        value={reason}
        onChange={(e) => {
          setReason(e.target.value);
        }}
      />
      <Alert severity={raised.length > 0 ? 'warning' : 'info'}>
        {raised.length > 0
          ? 'This change raises risk: it needs your password and takes effect at 09:00 IST of the next trading day after a full closed session.'
          : 'This change only lowers or keeps risk: it needs no password and takes effect at once.'}
      </Alert>
      {raised.length > 0 && (
        <TextField
          size="small"
          type="password"
          label="Password"
          required
          autoComplete="current-password"
          value={password}
          onChange={(e) => {
            setPassword(e.target.value);
          }}
        />
      )}
      {change.isError && !Object.keys(fieldErrors).length && (
        <Alert severity="error">{problemText(change.error)}</Alert>
      )}
      {answer && (
        <Alert severity="success">
          Saved. Effective from{' '}
          {formatIstDateTime((answer.pending ?? answer.inForce)?.effectiveFrom ?? answer.serverNow)} IST.
        </Alert>
      )}
      <Stack direction="row">
        <Button
          type="submit"
          variant="contained"
          disabled={
            change.isPending || over.length > 0 || reason.trim() === '' || (raised.length > 0 && password === '')
          }
        >
          Save limits
        </Button>
      </Stack>
    </Box>
  );
}

/** The limits sheet of one account: the sheet in force beside the form, risk-raising fields marked, ceilings enforced. */
export function LimitsForm({ accountId }: { accountId: string }) {
  const { data, error, isPending, refetch } = useQuery(limitsQuery(accountId));
  if (isPending) return <LoadingState label="Loading limits" />;
  if (error) return <RetryError error={error} onRetry={() => void refetch()} />;
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '1fr 2fr' } }}>
      <Box component="section" aria-label="Sheet in force">
        <Typography variant="subtitle2">In force</Typography>
        <SheetInForce row={data.inForce} missing={data.missing} />
        {data.pending && (
          <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', mt: 1 }}>
            A raise waits for {formatIstDateTime(data.pending.effectiveFrom)} IST.
          </Typography>
        )}
      </Box>
      <Form key={data.inForce?.id ?? 'none'} accountId={accountId} view={data} />
    </Box>
  );
}
