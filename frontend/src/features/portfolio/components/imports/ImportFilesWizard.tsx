import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useEffect, useId, useRef, useState } from 'react';
import type { AccountSummary } from '@/shared/api';
import { useReportImport } from '../../hooks/useReportImport';
import { problemKind } from '../../lib/errors';
import { uploadErrorText } from '../../lib/tradeErrors';
import { willChange, type NameChoices } from '../../lib/reportImport';
import { countOf } from '../../lib/tradeText';
import { FileCard } from './FileCard';
import { FilesDropZone } from './FilesDropZone';
import { ImportFilesActions } from './ImportFilesActions';

interface ImportFilesWizardProps {
  /** The accounts with the Portfolio role. */
  accounts: readonly AccountSummary[];
  /** The account of the page's scope; '' = the owner chooses. */
  initialAccountId: string;
  onHistory: () => void;
  /** A dividend list was among the files: opens the dividend import. */
  onImportDividends: () => void;
}

/**
 * Import files: the account (the page's) and the files, Preview (nothing is stored), one card per file with what it is and
 * would do, the owner's matches for names (read again with Preview again), then one Import for all. A file the server finds
 * stale at import time gets its fresh preview in place and is confirmed again; the others are done.
 */
export function ImportFilesWizard({
  accounts,
  initialAccountId,
  onHistory,
  onImportDividends,
}: ImportFilesWizardProps) {
  const [accountId, setAccountId] = useState(initialAccountId);
  const wizard = useReportImport(accountId);
  const { previews, outcomes, stale, previewing, importing, entries } = wizard;
  const headingId = useId();
  const heading = useRef<HTMLHeadingElement>(null);
  const notice = useRef<HTMLDivElement>(null);
  const account = accounts.find((a) => a.id === accountId);

  // A result the owner asked for is where the keyboard (and the screen reader) goes next.
  useEffect(() => {
    if (previews) (stale.size > 0 ? notice : heading).current?.focus();
  }, [previews, stale, outcomes]);

  const names: NameChoices = {
    drafts: wizard.drafts,
    choicesChanged: wizard.choicesChanged,
    onChoose: wizard.choose,
    onUnchoose: wizard.unchoose,
    onPreviewAgain: () => {
      void wizard.preview();
    },
  };
  const firstStale = [...stale].sort((a, b) => a - b)[0];
  const importError = importing.isError ? importing.error : null;
  const waiting = (previews ?? []).some((preview, index) => !outcomes.has(index) && willChange(preview));
  const done = outcomes.size > 0 && !waiting;

  return (
    <Stack spacing={3}>
      <Stack component="section" aria-label="Choose the files" spacing={2}>
        <Typography variant="body2" color="text.secondary">
          {`Drop the reports you downloaded from Dhan: the global transaction report (trades with side, price and charges), the depository (DP) charges sheets and the demat holding statement. Each file is recognised by its headers; the personal lines at the top of a report are never read. ${account ? `Files go to ${account.name}.` : ''}`}
        </Typography>
        {(accounts.length > 1 || accountId === '') && (
          <TextField
            select
            label="Account"
            value={accountId}
            disabled={wizard.busy}
            onChange={(event) => {
              setAccountId(event.target.value);
              wizard.changeAccount();
            }}
            slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
            sx={{ maxWidth: 360 }}
          >
            <option value="" disabled>
              Choose an account
            </option>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </TextField>
        )}
        {accountId === '' && <Alert severity="info">Choose the account the files belong to first.</Alert>}
        <FilesDropZone
          files={wizard.files}
          problems={wizard.problems}
          disabled={wizard.busy || accountId === ''}
          onFiles={wizard.addFiles}
          onRemove={wizard.removeFile}
        />
        {previewing.isError && (
          <Alert
            severity="error"
            role="alert"
            action={
              <Button
                color="inherit"
                size="small"
                disabled={wizard.busy}
                onClick={() => {
                  void wizard.preview();
                }}
              >
                Retry
              </Button>
            }
          >
            {uploadErrorText(previewing.error, 'The files could not be read.')}
          </Alert>
        )}
        <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
          <Button
            variant="contained"
            disabled={wizard.files.length === 0 || accountId === '' || wizard.busy}
            loading={previewing.isPending}
            onClick={() => {
              void wizard.preview();
            }}
          >
            {wizard.files.length > 1 ? `Preview ${countOf(wizard.files.length, 'file')}` : 'Preview'}
          </Button>
          <Typography variant="body2" color="text.secondary">
            Shows what importing would do. Nothing is stored yet.
          </Typography>
        </Stack>
      </Stack>
      {previews && account && (
        <Stack component="section" aria-labelledby={headingId} spacing={2}>
          <Typography ref={heading} id={headingId} component="h3" variant="h3" tabIndex={-1}>
            {outcomes.size === 0
              ? `Preview of ${countOf(previews.length, 'file')} — nothing is stored yet`
              : 'Import result'}
          </Typography>
          {previews.map((preview, index) => (
            <FileCard
              // The same file name may be chosen twice from two folders: the position tells them apart.
              key={`${String(index)}-${preview.fileName}`}
              preview={preview}
              account={{ id: account.id, name: account.name }}
              outcome={outcomes.get(index)}
              fileError={wizard.fileErrors.get(index)}
              stale={stale.has(index)}
              needsTick={wizard.needsTick(index)}
              ticked={wizard.ticks.has(index)}
              busy={wizard.busy}
              names={names}
              onTick={(on) => {
                wizard.tick(index, on);
              }}
              onImportDividends={onImportDividends}
              noticeRef={index === firstStale ? notice : undefined}
            />
          ))}
          {importError ? (
            <Alert severity={problemKind(importError) === 'sync_running' ? 'info' : 'error'} role="alert">
              {uploadErrorText(importError, 'The import could not be done. Nothing was changed.')}
            </Alert>
          ) : null}
          {done ? (
            <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
              <Button variant="contained" onClick={onHistory}>
                See it in History
              </Button>
              <Button onClick={wizard.clearAll}>Import more files</Button>
            </Stack>
          ) : (
            <ImportFilesActions
              previews={entries.map((entry) => entry.preview)}
              totals={wizard.totals}
              rebuild={wizard.rebuild}
              onRebuild={wizard.setRebuild}
              confirmed={wizard.confirmed}
              onConfirmed={wizard.setConfirmed}
              needsConfirmation={wizard.needsConfirmation}
              choicesChanged={wizard.choicesChanged}
              outdated={wizard.outdated}
              running={importing.isPending}
              busy={wizard.busy}
              onImport={() => {
                void wizard.runImport();
              }}
            />
          )}
        </Stack>
      )}
    </Stack>
  );
}
