import { Alert, Stack } from '@mui/material';
import { useEffect, useRef } from 'react';
import { useDividendImportWizard } from '../../hooks/useDividendImportWizard';
import { dividendErrorText, staleDividendReport } from '../../lib/dividendErrors';
import { problemKind } from '../../lib/errors';
import { DividendImportActions } from './DividendImportActions';
import { DividendImportDone } from './DividendImportDone';
import { DividendImportForm } from './DividendImportForm';
import { DividendPreview } from './DividendPreview';

interface DividendImportWizardProps {
  account: { id: string; name: string };
  onHistory: () => void;
  onImportTrades: () => void;
}

/**
 * Import dividends: the file, Preview (nothing is stored), the report with the owner's choices, then Import. A report that the
 * server finds stale at import time is swapped for the fresh one with a message, and the owner confirms again.
 */
export function DividendImportWizard({ account, onHistory, onImportTrades }: DividendImportWizardProps) {
  const wizard = useDividendImportWizard(account.id);
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

  const importError = importing.isError && staleDividendReport(importing.error) === null ? importing.error : null;

  return (
    <Stack spacing={3}>
      <DividendImportForm
        accountName={account.name}
        file={wizard.file}
        fileProblem={wizard.fileProblem}
        busy={wizard.busy}
        previewing={previewing.isPending}
        error={previewing.isError ? previewing.error : null}
        onFile={wizard.chooseFile}
        onClear={wizard.clearFile}
        onPreview={() => {
          void wizard.preview();
        }}
        onImportTrades={onImportTrades}
      />
      {done && (
        <DividendImportDone result={done} headingRef={doneHeading} onHistory={onHistory} onAnother={wizard.startOver} />
      )}
      {report && (
        <>
          {stale && (
            <Alert severity="warning" ref={noticeRef} tabIndex={-1}>
              Holdings, remembered matches or dividends changed — please review again. Nothing was imported; this is the
              new preview. Your choices that still apply were kept.
            </Alert>
          )}
          <DividendPreview
            report={report}
            account={account}
            headingRef={reportHeading}
            drafts={wizard.drafts}
            lines={wizard.lines}
            onChoose={wizard.choose}
            onUnchoose={wizard.unchoose}
            onInclude={wizard.include}
            onIncludeAll={wizard.includeAll}
          />
          {importError ? (
            <Alert severity={problemKind(importError) === 'sync_running' ? 'info' : 'error'} role="alert">
              {dividendErrorText(importError, 'The import could not be done. Nothing was changed.')}
            </Alert>
          ) : null}
          <DividendImportActions
            report={report}
            drafts={wizard.drafts}
            lines={wizard.lines}
            running={importing.isPending}
            onImport={() => {
              void wizard.runImport();
            }}
          />
        </>
      )}
    </Stack>
  );
}
