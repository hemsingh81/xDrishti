import { Alert, Stack } from '@mui/material';
import { useEffect, useRef } from 'react';
import type { AccountSummary } from '@/shared/api';
import { useImportWizard } from '../../hooks/useImportWizard';
import { problemKind } from '../../lib/errors';
import { staleReport, uploadErrorText } from '../../lib/tradeErrors';
import { ImportActions } from './ImportActions';
import { ImportDone } from './ImportDone';
import { ImportForm } from './ImportForm';
import { PreviewReport } from './PreviewReport';

interface ImportWizardProps {
  /** The accounts with the Portfolio role. */
  accounts: readonly AccountSummary[];
  /** The account of the page's scope, chosen at the start; the only account when there is just one. */
  initialAccountId: string;
  onHistory: () => void;
}

/**
 * Import CSV: choose the account and the file, Preview (nothing is stored), read the report, then Import and rebuild lots or
 * Import only. A report that the server finds stale at import time is swapped for the fresh one with a message.
 */
export function ImportWizard({ accounts, initialAccountId, onHistory }: ImportWizardProps) {
  const wizard = useImportWizard(initialAccountId);
  const { report, done, stale, importing, previewing } = wizard;
  const reportHeading = useRef<HTMLHeadingElement>(null);
  const doneHeading = useRef<HTMLHeadingElement>(null);
  const noticeRef = useRef<HTMLDivElement>(null);

  // A result the owner asked for is where the keyboard (and the screen reader) goes next.
  useEffect(() => {
    if (report) (stale ? noticeRef : reportHeading).current?.focus();
  }, [report, stale]);
  useEffect(() => {
    if (done) doneHeading.current?.focus();
  }, [done]);

  const running = importing.isPending ? (importing.variables.rebuild === false ? 'only' : 'rebuild') : null;
  const importError = importing.isError && staleReport(importing.error) === null ? importing.error : null;

  return (
    <Stack spacing={3}>
      <ImportForm
        accounts={accounts}
        accountId={wizard.accountId}
        file={wizard.file}
        fileProblem={wizard.fileProblem}
        busy={wizard.busy}
        previewing={previewing.isPending}
        error={previewing.isError ? previewing.error : null}
        onAccount={wizard.chooseAccount}
        onFile={wizard.chooseFile}
        onClear={wizard.clearFile}
        onPreview={() => {
          void wizard.preview();
        }}
      />
      {done && <ImportDone result={done} headingRef={doneHeading} onHistory={onHistory} onAnother={wizard.startOver} />}
      {report && (
        <>
          {stale && (
            <Alert severity="warning" ref={noticeRef} tabIndex={-1}>
              Holdings or trades changed — please review again. Nothing was imported; this is the new preview.
            </Alert>
          )}
          <PreviewReport report={report} headingRef={reportHeading} />
          {importError ? (
            <Alert severity={problemKind(importError) === 'sync_running' ? 'info' : 'error'} role="alert">
              {uploadErrorText(importError, 'The import could not be done. Nothing was changed.')}
            </Alert>
          ) : null}
          <ImportActions
            report={report}
            confirmed={wizard.confirmed}
            onConfirmed={wizard.setConfirmed}
            running={running}
            onImport={(rebuild) => {
              void wizard.runImport(rebuild);
            }}
          />
        </>
      )}
    </Stack>
  );
}
