import { Box, Chip, Stack, Typography } from '@mui/material';
import type { ReviewAccount } from '../api/planQueries';

const standingText: Record<string, string> = {
  passed: 'Passed',
  warned: 'Warned',
  needsconfirmation: 'Needs confirmation',
  confirmeddespite: 'Confirmed despite',
  refused: 'Refused (cannot be overridden)',
};

/** The checklist standings per paper account: every check with its standing as text. */
export function TicketChecklist({ accounts, names }: { accounts: ReviewAccount[]; names: Record<string, string> }) {
  return (
    <Stack spacing={1}>
      {accounts.map((a) => {
        const name = names[a.accountId] ?? a.accountId;
        const notPassed = a.checks.filter(
          (c) => c.standing.toLowerCase() !== 'passed' || c.outcome.toLowerCase() !== 'pass',
        );
        return (
          <Box key={a.accountId}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              {name} <Chip size="small" variant="outlined" label={`Mode: ${a.mode}`} /> quantity {a.effectiveQuantity}
              {a.effectiveQuantity !== a.quantity ? ` (allocated ${a.quantity})` : ''}
            </Typography>
            {a.checks.length === 0 ? (
              <Typography variant="caption" color="text.secondary">
                No checklist was stored for this account.
              </Typography>
            ) : notPassed.length === 0 ? (
              <Typography variant="caption" color="text.secondary">
                All {a.checks.length} checks passed.
              </Typography>
            ) : (
              <Box component="ul" aria-label={`Checks of ${name}`} sx={{ m: 0, pl: 2 }}>
                {notPassed.map((c) => (
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
      })}
    </Stack>
  );
}
