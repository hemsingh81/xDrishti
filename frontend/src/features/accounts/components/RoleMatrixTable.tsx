import {
  Box,
  Checkbox,
  Radio,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import type { AccountSummary } from '@/shared/api';
import { modeLabel } from '../lib/labels';
import type { RoleDraft } from '../lib/roleChanges';
import { TokenStatus } from './TokenStatus';

type ToggleRole = 'data' | 'trading' | 'portfolio';

interface RoleMatrixTableProps {
  accounts: AccountSummary[];
  drafts: Record<string, RoleDraft>;
  primaryId: string | undefined;
  /** Failed rows: account id → message. */
  errors: Record<string, string>;
  onToggle: (id: string, role: ToggleRole, checked: boolean) => void;
  onPrimary: (id: string) => void;
}

/** The matrix itself. Each control is labelled "<role> — <account>" for screen readers. */
export function RoleMatrixTable({ accounts, drafts, primaryId, errors, onToggle, onPrimary }: RoleMatrixTableProps) {
  return (
    <TableContainer>
      <Table size="small" aria-label="Role matrix">
        <TableHead>
          <TableRow>
            <TableCell>Account</TableCell>
            <TableCell align="center">Data</TableCell>
            <TableCell align="center">Primary data</TableCell>
            <TableCell align="center">Trading</TableCell>
            <TableCell>Mode</TableCell>
            <TableCell>Risk profile</TableCell>
            <TableCell align="center">Portfolio</TableCell>
            <TableCell>Token</TableCell>
            <TableCell>Result</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {accounts.map((account) => {
            const draft = drafts[account.id];
            if (!draft) {
              return null;
            }
            const isPrimary = primaryId === account.id;
            return (
              <TableRow key={account.id}>
                <TableCell component="th" scope="row">
                  <Box
                    component="span"
                    aria-hidden
                    sx={{
                      display: 'inline-block',
                      width: 10,
                      height: 10,
                      borderRadius: '50%',
                      mr: 1,
                      bgcolor: account.color,
                    }}
                  />
                  <strong>{account.name}</strong>
                </TableCell>
                <TableCell align="center">
                  <Checkbox
                    checked={draft.data}
                    // Unticking the primary would leave none: choose another primary first.
                    disabled={isPrimary}
                    onChange={(e) => {
                      onToggle(account.id, 'data', e.target.checked);
                    }}
                    slotProps={{ input: { 'aria-label': `Data — ${account.name}` } }}
                  />
                </TableCell>
                <TableCell align="center">
                  <Radio
                    name="primary-data"
                    checked={isPrimary}
                    disabled={!draft.data}
                    onChange={() => {
                      onPrimary(account.id);
                    }}
                    slotProps={{ input: { 'aria-label': `Primary data — ${account.name}` } }}
                  />
                </TableCell>
                <TableCell align="center">
                  <Checkbox
                    checked={draft.trading}
                    onChange={(e) => {
                      onToggle(account.id, 'trading', e.target.checked);
                    }}
                    slotProps={{ input: { 'aria-label': `Trading — ${account.name}` } }}
                  />
                </TableCell>
                <TableCell>{account.roles.trading.enabled ? modeLabel(account.roles.trading.mode) : '—'}</TableCell>
                <TableCell>{account.roles.trading.enabled ? (account.roles.trading.profile ?? '—') : '—'}</TableCell>
                <TableCell align="center">
                  <Checkbox
                    checked={draft.portfolio}
                    onChange={(e) => {
                      onToggle(account.id, 'portfolio', e.target.checked);
                    }}
                    slotProps={{ input: { 'aria-label': `Portfolio — ${account.name}` } }}
                  />
                </TableCell>
                <TableCell>
                  <TokenStatus status={account.tokenStatus} />
                </TableCell>
                <TableCell>
                  {errors[account.id] && (
                    <Typography role="alert" variant="body2" color="error">
                      {errors[account.id]}
                    </Typography>
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
