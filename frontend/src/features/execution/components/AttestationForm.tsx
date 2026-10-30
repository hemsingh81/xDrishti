import {
  Alert,
  Button,
  Card,
  CardContent,
  CardHeader,
  Checkbox,
  FormControlLabel,
  Stack,
  TextField,
} from '@mui/material';
import { useState, type SyntheticEvent } from 'react';
import { useRecordAttestation, type ReadinessAccount } from '../api/readinessApi';

const ipv4 = /^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/;
const ipv6 = /^[0-9a-fA-F:.]+$/;
const isIp = (v: string) => ipv4.test(v) || (v.includes(':') && ipv6.test(v));

/** The owner's record that Dhan shows this address with orders allowed. It never contacts the broker. */
export function AttestationForm({ account }: { account: ReadinessAccount }) {
  const save = useRecordAttestation();
  const [ip, setIp] = useState('');
  const [allowed, setAllowed] = useState(false);
  const [note, setNote] = useState('');
  const [touched, setTouched] = useState(false);

  const ipError = !isIp(ip.trim()) ? 'Enter a valid IPv4 or IPv6 address.' : '';
  const noteError = note.length > 200 ? 'The note is at most 200 characters.' : '';

  const submit = (e: SyntheticEvent) => {
    e.preventDefault();
    setTouched(true);
    if (ipError || noteError) return;
    save.mutate({ accountId: account.accountId, ip: ip.trim(), ordersAllowed: allowed, note });
  };

  return (
    <Card component="section" aria-label="IP attestation">
      <CardHeader title="Record IP attestation" />
      <CardContent>
        <form onSubmit={submit} noValidate>
          <Stack sx={{ gap: 2 }}>
            <TextField
              label="Static IP address"
              value={ip}
              onChange={(e) => {
                setIp(e.target.value);
              }}
              error={touched && !!ipError}
              helperText={touched ? ipError : ' '}
              required
            />
            <FormControlLabel
              control={
                <Checkbox
                  checked={allowed}
                  onChange={(e) => {
                    setAllowed(e.target.checked);
                  }}
                />
              }
              label="Orders Allowed is true at Dhan"
            />
            <TextField
              label="Note"
              value={note}
              onChange={(e) => {
                setNote(e.target.value);
              }}
              error={!!noteError}
              helperText={noteError || 'Optional, plain text.'}
              multiline
            />
            <Button type="submit" variant="contained" disabled={save.isPending}>
              Record attestation
            </Button>
            {save.isSuccess && <Alert severity="success">Attestation recorded.</Alert>}
            {save.isError && <Alert severity="error">{save.error.message}</Alert>}
          </Stack>
        </form>
      </CardContent>
    </Card>
  );
}
