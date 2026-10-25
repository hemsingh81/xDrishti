import { Box, Button, Chip, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { ReviewAccount } from '../api/planQueries';
import { notPassed, standingLine, standingText } from '../lib/standings';

function AccountStanding({ account: a, name }: { account: ReviewAccount; name: string }) {
  const [all, setAll] = useState(false);
  const listed = all ? a.checks : notPassed(a);
  return (
    <Box>
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        {name}: {standingLine(a)} <Chip size="small" variant="outlined" label={`Mode: ${a.mode}`} /> quantity{' '}
        {a.effectiveQuantity}
        {a.effectiveQuantity !== a.quantity ? ` (allocated ${String(a.quantity)})` : ''}
      </Typography>
      {a.checks.length > 0 && (
        <Button
          size="small"
          aria-expanded={all}
          aria-label={`${all ? 'Show fewer checks' : 'Show all checks'} of ${name}`}
          onClick={() => {
            setAll((v) => !v);
          }}
        >
          {all ? 'Show fewer checks' : 'Show all checks'}
        </Button>
      )}
      {listed.length > 0 && (
        <Box component="ul" aria-label={`Checks of ${name}`} sx={{ m: 0, pl: 2 }}>
          {listed.map((c) => (
            <li key={c.code}>
              <Typography variant="caption">
                <strong>{c.code}</strong>: {c.outcome}, {standingText[c.standing.toLowerCase()] ?? c.standing}.{' '}
                {c.detail}
              </Typography>
            </li>
          ))}
        </Box>
      )}
    </Box>
  );
}

/** One standing line per paper account, the checks that were not passed listed under it, every check on request. No empty heading for a ticket with no accounts. */
export function TicketChecklist({ accounts, names }: { accounts: ReviewAccount[]; names: Record<string, string> }) {
  if (accounts.length === 0) return null;
  return (
    <Stack spacing={1}>
      {accounts.map((a) => (
        <AccountStanding key={a.accountId} account={a} name={names[a.accountId] ?? a.accountId} />
      ))}
    </Stack>
  );
}
