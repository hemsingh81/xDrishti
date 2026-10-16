import { Alert, Button, Card, CardContent, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import { useId, type Ref } from 'react';
import type { ImportFileError, ImportFilePreview, ImportFileResult } from '@/shared/api';
import { statementText, withoutSidesText } from '../../lib/wrongFile';
import { willChange, type NameChoices } from '../../lib/reportImport';
import { FileResult } from './FileResult';
import { FileWarnings } from './FileWarnings';
import { KindBlock } from './KindBlock';
import { KindChip } from './KindChip';
import { SummaryTiles } from './SummaryTiles';

interface FileCardProps {
  preview: ImportFilePreview;
  account: { id: string; name: string };
  outcome: ImportFileResult | undefined;
  /** A refusal that nothing was stored for and that can be tried again (a sync is running, the preview is out of date ...). */
  fileError: ImportFileError | undefined;
  /** The preview was swapped for a fresh one at import time: it has to be confirmed again. */
  stale: boolean;
  /** The file looks like another account's: it is imported only when ticked. */
  needsTick: boolean;
  ticked: boolean;
  busy: boolean;
  names: NameChoices;
  onTick: (on: boolean) => void;
  onImportDividends: () => void;
  /** Where the keyboard goes when this card holds the first thing to look at after a preview or an import. */
  noticeRef?: Ref<HTMLDivElement>;
}

const unsupportedText: Record<string, string> = {
  pnl_report:
    'This is a profit and loss statement: a statement of results, not a list of trades. It is recognised and not imported; nothing is changed.',
  tradebook_without_sides: withoutSidesText,
  statement: statementText,
};

function fileErrorText(error: ImportFileError): string {
  if (error.code === 'portfolio.sync_running') {
    return 'A sync or import of this account is running. Nothing was stored; try again in a moment.';
  }
  if (error.code === 'portfolio.invalid')
    return 'The preview is out of date. Nothing was stored; preview the files again.';
  return error.message;
}

/** One file of the import: its name and kind, what is in it, the notices, and the block of its kind - or what the import did. */
export function FileCard(props: FileCardProps) {
  const { preview, account, outcome, fileError, stale, needsTick, ticked, busy, onTick, onImportDividends, noticeRef } =
    props;
  const headingId = useId();
  const unsupported = !preview.supported || preview.error !== null;
  const message = preview.message ?? unsupportedText[preview.kind] ?? null;
  return (
    <Card component="section" aria-labelledby={headingId} variant="outlined">
      <CardContent>
        <Stack spacing={1.5}>
          <Stack direction="row" sx={{ alignItems: 'center', columnGap: 1, rowGap: 0.5, flexWrap: 'wrap' }}>
            <Typography id={headingId} component="h4" variant="h3" sx={{ overflowWrap: 'anywhere' }}>
              {preview.fileName}
            </Typography>
            <KindChip kind={preview.kind} supported={preview.supported && preview.error === null} />
          </Stack>
          {outcome ? (
            <FileResult result={outcome} />
          ) : (
            <>
              {stale && (
                <Alert severity="warning" ref={noticeRef} tabIndex={-1}>
                  Holdings, trades or remembered matches changed — please review again. Nothing was imported for this
                  file; this is the new preview. Your choices that still apply were kept.
                </Alert>
              )}
              {fileError && (
                <Alert severity={fileError.code === 'portfolio.sync_running' ? 'info' : 'warning'} role="alert">
                  {fileErrorText(fileError)}
                </Alert>
              )}
              {preview.error && (
                <Alert severity="error" role="alert">
                  {preview.error.message}
                </Alert>
              )}
              {!preview.error && !preview.supported && message && <Alert severity="info">{message}</Alert>}
              {preview.routeTo === 'dividends' && (
                <Stack direction="row">
                  <Button variant="outlined" size="small" onClick={onImportDividends}>
                    Open the dividend import
                  </Button>
                </Stack>
              )}
              {!unsupported && (
                <>
                  {preview.summary && <SummaryTiles summary={preview.summary} />}
                  <FileWarnings warnings={preview.warnings} accountName={account.name} />
                  {needsTick && (
                    <FormControlLabel
                      control={
                        <Checkbox
                          checked={ticked}
                          disabled={busy}
                          onChange={(event) => {
                            onTick(event.target.checked);
                          }}
                        />
                      }
                      label={`Import ${preview.fileName} into ${account.name} anyway`}
                    />
                  )}
                  {!willChange(preview) && (
                    <Typography color="text.secondary">
                      Nothing new: everything in this file is stored or known already.
                    </Typography>
                  )}
                  <KindBlock preview={preview} account={account} names={props.names} busy={busy} />
                </>
              )}
            </>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}
