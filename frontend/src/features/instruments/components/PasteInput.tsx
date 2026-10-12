import UploadFileIcon from '@mui/icons-material/UploadFileOutlined';
import { Alert, Box, Button, Stack, TextField, Typography } from '@mui/material';
import { useState, type ChangeEvent, type Ref } from 'react';
import { formatNumber } from '@/shared/lib/format';
import { maxFileBytes, maxSymbols, parseSymbols, symbolsFromCsv } from '../lib/parseSymbols';

interface PasteInputProps {
  /** The text area, for the dialog to focus. */
  inputRef: Ref<HTMLTextAreaElement>;
  text: string;
  onTextChange: (text: string) => void;
  /** Why the look-up failed. */
  error: string | null;
  disabled: boolean;
}

/** The first step: a text area for pasted symbols and a button that reads a CSV file into it. Parsed in the browser. */
export function PasteInput({ inputRef, text, onTextChange, error, disabled }: PasteInputProps) {
  const [fileError, setFileError] = useState<string | null>(null);
  const count = parseSymbols(text).length;
  const tooMany = count > maxSymbols;

  const readFile = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = ''; // the same file can be chosen again
    if (!file) return;
    if (file.size > maxFileBytes) {
      setFileError('That file is too large. Choose a list of up to 5,000 symbols.');
      return;
    }
    try {
      const content = await file.text();
      const entries = /\.(txt|list)$/i.test(file.name) ? parseSymbols(content) : symbolsFromCsv(content);
      setFileError(entries.length === 0 ? 'No symbols were found in that file.' : null);
      if (entries.length > 0) onTextChange([text.trim(), ...entries].filter((part) => part !== '').join('\n'));
    } catch {
      setFileError('That file could not be read.');
    }
  };

  return (
    <Stack spacing={2}>
      <Typography color="text.secondary">
        Paste tickers (<code>RELIANCE</code>), symbols (<code>NSE:RELIANCE</code>) or ISINs, separated by new lines,
        commas or blanks - or upload a CSV file. Nothing is tracked until you have seen the result.
      </Typography>
      <TextField
        label="Symbols"
        multiline
        minRows={8}
        maxRows={14}
        fullWidth
        inputRef={inputRef}
        disabled={disabled}
        value={text}
        placeholder={'RELIANCE\nTCS\nINE040A01034'}
        error={tooMany}
        helperText={
          tooMany
            ? `Too many: ${formatNumber(count)} entries, at most ${formatNumber(maxSymbols)} at a time.`
            : `${formatNumber(count)} ${count === 1 ? 'entry' : 'entries'} (at most ${formatNumber(maxSymbols)})`
        }
        onChange={(event) => {
          onTextChange(event.target.value);
        }}
        slotProps={{ htmlInput: { spellCheck: false, autoCapitalize: 'off' } }}
      />
      <Box>
        <Button component="label" size="small" variant="outlined" startIcon={<UploadFileIcon />} disabled={disabled}>
          Upload CSV file
          <input
            hidden
            type="file"
            accept=".csv,.txt,text/csv,text/plain"
            onChange={(event) => {
              void readFile(event);
            }}
          />
        </Button>
      </Box>
      {fileError && <Alert severity="warning">{fileError}</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
    </Stack>
  );
}
