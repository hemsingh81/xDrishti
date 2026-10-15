import { Box } from '@mui/material';
import { useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogHeader } from '@/shared/ui/dialog';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { DividendImportHistory } from './DividendImportHistory';
import { DividendImportWizard } from './DividendImportWizard';

type ImportTab = 'import' | 'history';

const tabs: readonly ButtonTab<ImportTab>[] = [
  { value: 'import', label: 'Import file', id: 'dividend-import-tab-import', panelId: 'dividend-import-panel-import' },
  { value: 'history', label: 'History', id: 'dividend-import-tab-history', panelId: 'dividend-import-panel-history' },
];

interface DividendImportDialogProps {
  account: { id: string; name: string };
  onClose: () => void;
  /** A tradebook was dropped here: leave this dialog for the trade import. */
  onImportTrades: () => void;
  /** A report of Dhan (transaction report, DP charges, demat holding) was dropped here: open Import files. */
  onImportFiles: () => void;
}

/**
 * Import dividends for the account of the Cash & dividends panel: the wizard (file, preview, choices, import, result) and the
 * history of imported files with Undo. The wizard stays mounted while History is shown, so a half-done import is not lost.
 */
export function DividendImportDialog({ account, onClose, onImportTrades, onImportFiles }: DividendImportDialogProps) {
  const [tab, setTab] = useState<ImportTab>('import');
  return (
    <AppDialog open onClose={onClose} maxWidth="lg" fullScreenOnMobile>
      <AppDialogHeader subtitle={account.name}>Import dividends</AppDialogHeader>
      <AppDialogBody>
        <ButtonTabs tabs={tabs} value={tab} onChange={setTab} label="Import dividends" />
        <Box
          role="tabpanel"
          id="dividend-import-panel-import"
          aria-labelledby="dividend-import-tab-import"
          hidden={tab !== 'import'}
        >
          <DividendImportWizard
            account={account}
            onHistory={() => {
              setTab('history');
            }}
            onImportTrades={onImportTrades}
            onImportFiles={onImportFiles}
          />
        </Box>
        {tab === 'history' && (
          <Box
            role="tabpanel"
            id="dividend-import-panel-history"
            aria-labelledby="dividend-import-tab-history"
            tabIndex={0}
          >
            <DividendImportHistory accountId={account.id} />
          </Box>
        )}
      </AppDialogBody>
    </AppDialog>
  );
}
