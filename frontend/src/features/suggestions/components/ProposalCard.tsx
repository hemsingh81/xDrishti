import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import CrossIcon from '@mui/icons-material/CancelOutlined';
import { Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { formatDateTime } from '@/shared/lib/format';
import { useRejectProposal, type Proposal } from '../api/proposalApi';
import { figureText, kindLabel } from '../lib/wording';
import { AcceptDialog } from './AcceptDialog';

/** One suggestion: what, why (the model's words, shown as text), the engine's evidence and checks (a word and an icon each), and the owner's two buttons while it is pending. */
export function ProposalCard({ proposal }: { proposal: Proposal }) {
  const [accepting, setAccepting] = useState(false);
  const reject = useRejectProposal();
  const pending = proposal.state === 'pending';
  const e = proposal.evidence;

  return (
    <Box
      component="article"
      aria-label={`${kindLabel[proposal.kind] ?? proposal.kind}: ${proposal.subject}`}
      sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 1 }}
    >
      <Stack direction="row" spacing={1} sx={{ mb: 1, alignItems: 'center' }}>
        <Chip size="small" label={kindLabel[proposal.kind] ?? proposal.kind} />
        <Chip size="small" variant="outlined" label={proposal.state} />
        {proposal.decision?.auto === true && <Chip size="small" color="warning" label="accepted automatically" />}
      </Stack>
      <Typography variant="h3" sx={{ fontSize: '1rem' }}>
        {proposal.subject}
      </Typography>
      <Typography sx={{ my: 1 }}>{proposal.reason}</Typography>
      <Typography variant="subtitle2">Evidence</Typography>
      <Typography variant="body2" color="text.secondary">
        Basis: {e.basis}. Trades: {e.n}. {e.dateRange ?? ''} Runs: {e.population.length}.
      </Typography>
      <ul>
        {e.figures.map((f) => (
          <li key={f.label}>
            <Typography variant="body2">{figureText(f)}</Typography>
          </li>
        ))}
      </ul>
      {e.limits.map((limit) => (
        <Typography key={limit} variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {limit}
        </Typography>
      ))}
      <Typography variant="subtitle2" sx={{ mt: 1 }}>
        Engine checks
      </Typography>
      <ul aria-label="Engine checks">
        {proposal.checks.map((c) => (
          <li key={c.code}>
            <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
              {c.ok ? <CheckIcon fontSize="small" aria-hidden /> : <CrossIcon fontSize="small" aria-hidden />}
              <Typography variant="body2">
                {c.ok ? 'Passed' : 'Failed'}: {c.detail}
              </Typography>
            </Stack>
          </li>
        ))}
      </ul>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        Expires {formatDateTime(proposal.expiresAt)}. {proposal.notes.join(' ')}
      </Typography>
      {proposal.decision !== null && (
        <Typography variant="body2" sx={{ mt: 1 }}>
          {proposal.decision.state === 'accepted' ? 'Accepted' : 'Rejected'}
          {proposal.decision.reason ? `: ${proposal.decision.reason}` : ''}
        </Typography>
      )}
      {pending && (
        <Stack direction="row" spacing={1} sx={{ mt: 1.5 }}>
          <Button
            variant="contained"
            onClick={() => {
              setAccepting(true);
            }}
          >
            Accept
          </Button>
          <Button
            disabled={reject.isPending}
            onClick={() => {
              reject.mutate({ id: proposal.id, reason: null });
            }}
          >
            Reject
          </Button>
        </Stack>
      )}
      {accepting && (
        <AcceptDialog
          proposal={proposal}
          onClose={() => {
            setAccepting(false);
          }}
        />
      )}
    </Box>
  );
}
