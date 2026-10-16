import { Box } from '@mui/material';
import { useState } from 'react';
import type { AccountSummary } from '@/shared/api';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { AppDialog, AppDialogBody, AppDialogHeader } from '@/shared/ui/dialog';
import { initialTradesAccount } from '../../lib/accounts';
import { ImportHistory } from '../trades/ImportHistory';
import { ImportFilesWizard } from './ImportFilesWizard';

type ImportTab = 'import' | 'history';

const tabs: readonly ButtonTab<ImportTab>[] = [
  { value: 'import', label: 'Import files', id: 'import-files-tab-import', panelId: 'import-files-panel-import' },
  { value: 'history', label: 'History', id: 'import-files-tab-history', panelId: 'import-files-panel-history' },
];

interface ImportFilesDialogProps {
  /** The accounts with the Portfolio role. */
  accounts: readonly AccountSummary[];
  /** The account of the page's scope (null = all accounts: the owner picks one). */
  scopeAccountId: string | null;
  onClose: () => void;
  /** A dividend list was among the files: leave this dialog for the dividend import. */
  onImportDividends: () => void;
}

/**
 * Import files for the account of the page: drop Dhan's downloaded reports (transaction report, DP charges, demat holding,
 * trade and dividend lists) and import them with one button, and the history of imported files with Undo. The wizard stays
 * mounted while History is shown, so a half-done import is not lost.
 */
export function ImportFilesDialog({ accounts, scopeAccountId, onClose, onImportDividends }: ImportFilesDialogProps) {
  const [tab, setTab] = useState<ImportTab>('import');
  return (
    <AppDialog open onClose={onClose} maxWidth="lg" fullScreenOnMobile>
      <AppDialogHeader>Import files</AppDialogHeader>
      <AppDialogBody>
        <ButtonTabs tabs={tabs} value={tab} onChange={setTab} label="Import files" />
        <Box
          role="tabpanel"
          id="import-files-panel-import"
          aria-labelledby="import-files-tab-import"
          hidden={tab !== 'import'}
        >
          <ImportFilesWizard
            accounts={accounts}
            initialAccountId={initialTradesAccount(accounts, scopeAccountId)}
            onHistory={() => {
              setTab('history');
            }}
            onImportDividends={onImportDividends}
          />
        </Box>
        {tab === 'history' && (
          <Box role="tabpanel" id="import-files-panel-history" aria-labelledby="import-files-tab-history" tabIndex={0}>
            <ImportHistory />
          </Box>
        )}
      </AppDialogBody>
    </AppDialog>
  );
}
