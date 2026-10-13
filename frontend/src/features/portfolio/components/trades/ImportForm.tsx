import DownloadIcon from '@mui/icons-material/FileDownloadOutlined';
import { Alert, Button, Link, Stack, TextField, Typography } from '@mui/material';
import type { AccountSummary } from '@/shared/api';
import { problemKind } from '../../lib/errors';
import { uploadErrorText, uploadFieldText } from '../../lib/tradeErrors';
import { FileDropZone } from './FileDropZone';

interface ImportFormProps {
  accounts: readonly AccountSummary[];
  accountId: string;
  file: File | null;
  fileProblem: string | null;
  /** A preview or an import is running: nothing can be changed meanwhile. */
  busy: boolean;
  /** The preview is the one running (the Preview button shows progress). */
  previewing: boolean;
  /** Why the preview failed, if it did. */
  error: unknown;
  onAccount: (id: string) => void;
  onFile: (file: File) => void;
  onClear: () => void;
  onPreview: () => void;
}

/** Step one: which account, which file (with the template to start from), then Preview. Nothing is stored by it. */
export function ImportForm({
  accounts,
  accountId,
  file,
  fileProblem,
  busy,
  previewing,
  error,
  onAccount,
  onFile,
  onClear,
  onPreview,
}: ImportFormProps) {
  const fields = error ? uploadFieldText(error) : {};
  const invalid = problemKind(error) === 'invalid';
  const general = error && !(invalid && (fields.file || fields.accountId || fields.other)) ? error : null;
  return (
    <Stack component="section" aria-label="Choose the file" spacing={2}>
      <Typography variant="body2" color="text.secondary">
        Load a tradebook exported from your broker. Required columns: date, symbol or ISIN, side (buy or sell), quantity
        and price; optional: exchange, trade id, time and charges. Start from the{' '}
        <Link href="/api/portfolio/trades/template" download="xdrishti-trades-template.csv">
          <DownloadIcon fontSize="inherit" sx={{ verticalAlign: 'text-bottom', mr: 0.25 }} aria-hidden />
          CSV template
        </Link>
        . Dates are read as day first; whole shares only.
      </Typography>
      <TextField
        select
        label="Account"
        value={accountId}
        disabled={busy}
        error={fields.accountId !== undefined}
        helperText={fields.accountId}
        onChange={(event) => {
          onAccount(event.target.value);
        }}
        slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
        sx={{ maxWidth: 360 }}
      >
        <option value="" disabled>
          Choose an account
        </option>
        {accounts.map((account) => (
          <option key={account.id} value={account.id}>
            {account.name}
          </option>
        ))}
      </TextField>
      <FileDropZone
        file={file}
        onFile={onFile}
        onClear={onClear}
        disabled={busy}
        error={fileProblem ?? fields.file ?? (invalid ? fields.other : undefined)}
      />
      {general ? (
        <Alert
          severity="error"
          role="alert"
          action={
            <Button color="inherit" size="small" disabled={busy || !file || accountId === ''} onClick={onPreview}>
              Retry
            </Button>
          }
        >
          {uploadErrorText(general, 'The file could not be read.')}
        </Alert>
      ) : null}
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center' }}>
        <Button
          variant="contained"
          disabled={!file || accountId === '' || busy}
          loading={previewing}
          onClick={onPreview}
        >
          Preview
        </Button>
        <Typography variant="body2" color="text.secondary">
          Shows what importing would do. Nothing is stored yet.
        </Typography>
      </Stack>
    </Stack>
  );
}
