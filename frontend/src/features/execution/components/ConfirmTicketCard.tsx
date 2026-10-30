import { Alert, Button, Card, CardContent, CardHeader, Stack, TextField, Typography } from '@mui/material';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router';
import { problemText, type ConfirmTicket } from '../api/confirmApi';

const lockMs = 800;
/** The browser clock, read in one place: only ever used as the difference to the server's `serverNow`. */
const browserNow = () => Date.now();

interface Props {
  ticket: ConfirmTicket;
  pending: boolean;
  error: unknown;
  /** The confirm call succeeded and the list has not caught up yet. */
  justConfirmed: boolean;
  onConfirm: (v: { password?: string; reason?: string }) => void;
  onSkip: () => void;
  onRetry: () => void;
}

const stateWord: Record<string, string> = {
  confirmed: 'Confirmed: waiting for the broker.',
  refused: 'Refused.',
  expired: 'Expired.',
  skipped: 'Skipped.',
};

/**
 * One order waiting for the owner. The text is the server's, shown as text and never re-typed. The card never takes
 * focus, is not a modal, and has no form, so Enter does nothing unless the button itself has focus. The confirm button
 * stays off for 800 ms after the text first rendered or changed.
 */
export function ConfirmTicketCard({ ticket, pending, error, justConfirmed, onConfirm, onSkip, onRetry }: Props) {
  const [locked, setLocked] = useState(true);
  const [password, setPassword] = useState('');
  const [reason, setReason] = useState('');
  const [now, setNow] = useState(() => Date.now());
  // The countdown runs on the server's clock: the offset is taken when this read arrived (adjusted during render).
  const [seen, setSeen] = useState({ serverNow: ticket.serverNow, text: ticket.text, hash: ticket.textHash });
  const [offset, setOffset] = useState(() => Date.parse(ticket.serverNow) - browserNow());
  const sending = useRef(false);

  if (seen.serverNow !== ticket.serverNow) {
    setSeen({ ...seen, serverNow: ticket.serverNow });
    setOffset(Date.parse(ticket.serverNow) - browserNow());
  }
  if (seen.text !== ticket.text || seen.hash !== ticket.textHash) {
    setSeen({ ...seen, text: ticket.text, hash: ticket.textHash });
    setLocked(true);
  }

  useEffect(() => {
    const t = setTimeout(() => {
      setLocked(false);
    }, lockMs);
    return () => {
      clearTimeout(t);
    };
  }, [ticket.text, ticket.textHash]);

  useEffect(() => {
    const t = setInterval(() => {
      setNow(Date.now());
    }, 1000);
    return () => {
      clearInterval(t);
    };
  }, []);

  useEffect(() => {
    if (!pending) sending.current = false;
  }, [pending]);

  const secondsLeft = Math.max(0, Math.ceil((Date.parse(ticket.expiresAt) - (now + offset)) / 1000));
  const isOpen = ticket.state === 'open' && !justConfirmed;
  const expired = isOpen && secondsLeft === 0;
  const needsReason = ticket.softCodes.length > 0;
  const missing = (ticket.requiresPassword && password === '') || (needsReason && reason.trim() === '');
  const disabled = locked || pending || !isOpen || expired || missing;

  const send = () => {
    if (disabled || sending.current) return;
    sending.current = true;
    onConfirm({
      password: ticket.requiresPassword ? password : undefined,
      reason: needsReason ? reason.trim() : undefined,
    });
  };

  const status = justConfirmed
    ? stateWord.confirmed
    : expired
      ? stateWord.expired
      : isOpen
        ? pending
          ? 'Confirming…'
          : undefined
        : stateWord[ticket.state];

  return (
    <Card component="article" aria-label={`Order for ${ticket.accountName}`}>
      <CardHeader
        title={ticket.accountName}
        subheader={isOpen && !expired ? `Expires in ${String(secondsLeft)} s` : undefined}
      />
      <CardContent>
        <Stack sx={{ gap: 2 }}>
          <Typography component="p" sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            {ticket.text}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Worst case at the stop: {ticket.riskAtStop.toLocaleString('en-IN')}. Reference price{' '}
            {ticket.refPrice.toLocaleString('en-IN')}. <Link to="/plans">Plan review</Link>
          </Typography>
          {status && (
            <Alert severity={ticket.state === 'refused' || expired ? 'warning' : 'info'} role="status">
              {status}
              {!isOpen && ticket.outcomeReason ? ` ${ticket.outcomeReason}` : ''}
            </Alert>
          )}
          {isOpen && !expired && ticket.requiresPassword && (
            <TextField
              label="Password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => {
                setPassword(e.target.value);
              }}
              helperText={ticket.passwordReasons.join(', ')}
              required
            />
          )}
          {isOpen && !expired && needsReason && (
            <TextField
              label="Reason"
              value={reason}
              onChange={(e) => {
                setReason(e.target.value.slice(0, 200));
              }}
              helperText={`Checks not passed: ${ticket.softCodes.join(', ')}`}
              required
            />
          )}
          {error !== null && error !== undefined && isOpen && (
            <Alert
              severity="error"
              action={
                <Button color="inherit" size="small" onClick={onRetry} disabled={pending}>
                  Try again
                </Button>
              }
            >
              {problemText(error)}
            </Alert>
          )}
          <Stack direction="row" sx={{ gap: 1 }}>
            <Button variant="contained" onClick={send} disabled={disabled}>
              Place this order at Dhan
            </Button>
            <Button variant="outlined" onClick={onSkip} disabled={pending || !isOpen || expired}>
              Skip
            </Button>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}
