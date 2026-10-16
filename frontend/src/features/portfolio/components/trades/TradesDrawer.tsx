import AddIcon from '@mui/icons-material/Add';
import UploadIcon from '@mui/icons-material/UploadFileOutlined';
import CloseIcon from '@mui/icons-material/Close';
import { Box, Button, Divider, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import type { AccountSummary } from '@/shared/api';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { initialTradesAccount } from '../../lib/accounts';
import { tradesPanelId, tradesTabId, tradesTabLabels, tradesTabs, type TradesTab } from '../../lib/tradesTab';
import { ImportHistory } from './ImportHistory';
import { ImportWizard } from './ImportWizard';

interface TradesDrawerProps {
  tab: TradesTab;
  onTabChange: (tab: TradesTab) => void;
  onClose: () => void;
  /** Add one trade by hand. */
  onAddTrade: () => void;
  /** A dividend file was dropped on the trade import: open the dividend import instead. */
  onImportDividends: () => void;
  /** Opens the Import files dialog: the header button, or a report was dropped on this import. */
  onImportFiles: () => void;
  /** The accounts with the Portfolio role. */
  accounts: readonly AccountSummary[];
  /** The account of the page's scope (null = all accounts). */
  scopeAccountId: string | null;
}

const tabs: readonly ButtonTab<TradesTab>[] = tradesTabs.map((tab) => ({
  value: tab,
  label: tradesTabLabels[tab],
  id: tradesTabId(tab),
  panelId: tradesPanelId(tab),
}));

/**
 * The trades in a side panel: Import CSV (the wizard: file, preview, import) and History (the imported files with Undo),
 * and Add trade for one trade typed in. The wizard stays mounted while History is shown, so a half-done import is not lost.
 */
export function TradesDrawer({
  tab,
  onTabChange,
  onClose,
  onAddTrade,
  onImportDividends,
  onImportFiles,
  accounts,
  scopeAccountId,
}: TradesDrawerProps) {
  const titleId = useId();
  return (
    <Drawer
      anchor="right"
      open
      onClose={onClose}
      slotProps={{
        paper: {
          role: 'dialog',
          'aria-labelledby': titleId,
          sx: { width: { xs: '100%', md: 920 }, maxWidth: '100vw' },
        },
      }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1, px: 2.5, py: 1.5 }}>
        <Typography id={titleId} component="h2" variant="h2" sx={{ flex: 1 }}>
          Trades
        </Typography>
        <Button size="small" variant="outlined" startIcon={<UploadIcon />} onClick={onImportFiles}>
          Import files
        </Button>
        <Button size="small" variant="outlined" startIcon={<AddIcon />} onClick={onAddTrade}>
          Add trade
        </Button>
        <IconButton aria-label="Close trades" size="small" edge="end" onClick={onClose}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </Stack>
      <Divider />
      <Box sx={{ px: 2.5, pt: 1.5, overflowY: 'auto', flex: 1, pb: 3 }}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          Your trades explain the dates of your lots. Importing or adding trades never changes a lot by itself: you
          rebuild the lots after seeing what would change.
        </Typography>
        <ButtonTabs tabs={tabs} value={tab} onChange={onTabChange} label="Trades" />
        <Box
          role="tabpanel"
          id={tradesPanelId('import')}
          aria-labelledby={tradesTabId('import')}
          hidden={tab !== 'import'}
        >
          <ImportWizard
            accounts={accounts}
            initialAccountId={initialTradesAccount(accounts, scopeAccountId)}
            onHistory={() => {
              onTabChange('history');
            }}
            onImportDividends={onImportDividends}
            onImportFiles={onImportFiles}
          />
        </Box>
        {tab === 'history' && (
          <Box role="tabpanel" id={tradesPanelId('history')} aria-labelledby={tradesTabId('history')} tabIndex={0}>
            <ImportHistory />
          </Box>
        )}
      </Box>
    </Drawer>
  );
}
