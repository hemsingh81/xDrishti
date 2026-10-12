import { Button } from '@mui/material';
import { useRef, useState } from 'react';
import type { ResolveResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useResolveSymbols } from '../api/resolveMutation';
import { useTrackingActions } from '../hooks/trackingActionsContext';
import { trackingProblemText } from '../lib/errors';
import { maxSymbols, parseSymbols } from '../lib/parseSymbols';
import { newlyTrackable } from '../lib/trackingText';
import { PasteInput } from './PasteInput';
import { PastePreview } from './PastePreview';

interface PasteSymbolsDialogProps {
  open: boolean;
  onClose: () => void;
  /** The matched instruments were tracked. */
  onTracked: () => void;
}

/**
 * Bulk add by list: paste or upload symbols, look them up, see what matched and what did not, then confirm to track the
 * matched ones. The list is read in the browser; only the entries go to the server, and only to look them up.
 */
export function PasteSymbolsDialog({ open, onClose, onTracked }: PasteSymbolsDialogProps) {
  const [text, setText] = useState('');
  const [result, setResult] = useState<ResolveResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [wasOpen, setWasOpen] = useState(open);
  const firstField = useRef<HTMLTextAreaElement>(null);
  const resolve = useResolveSymbols();
  const { track, busy: tracking } = useTrackingActions();

  // Every opening starts with an empty list.
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setText('');
      setResult(null);
      setError(null);
    }
  }

  const entries = parseSymbols(text);
  const busy = resolve.isPending || tracking;
  const fresh = result ? newlyTrackable(result.matched) : [];

  const lookUp = () => {
    setError(null);
    resolve.mutate(entries, {
      onSuccess: setResult,
      onError: (failure) => {
        setError(trackingProblemText(failure, 'The list could not be looked up.'));
      },
    });
  };
  const confirm = async () => {
    const label = fresh.length === 1 ? (fresh[0]?.symbol ?? '') : `${formatNumber(fresh.length)} instruments`;
    if (
      await track(
        fresh.map((m) => m.id),
        label,
      )
    )
      onTracked();
  };

  return (
    <AppDialog
      open={open}
      onClose={onClose}
      busy={busy}
      maxWidth="md"
      fullScreenOnMobile
      initialFocusRef={firstField}
      onSubmit={(event) => {
        event.preventDefault();
        if (result) void confirm();
        else if (entries.length > 0 && entries.length <= maxSymbols) lookUp();
      }}
    >
      <AppDialogHeader subtitle={result ? 'Check the result, then confirm' : 'Track many instruments at once'}>
        Paste symbols
      </AppDialogHeader>
      <AppDialogBody>
        {result ? (
          <PastePreview result={result} />
        ) : (
          <PasteInput inputRef={firstField} text={text} onTextChange={setText} error={error} disabled={busy} />
        )}
      </AppDialogBody>
      <AppDialogFooter>
        {result ? (
          <>
            <Button
              disabled={busy}
              onClick={() => {
                setResult(null);
              }}
            >
              Back
            </Button>
            <Button onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button type="submit" variant="contained" loading={tracking} disabled={fresh.length === 0}>
              {fresh.length === 0
                ? 'Nothing to track'
                : `Track ${formatNumber(fresh.length)} ${fresh.length === 1 ? 'instrument' : 'instruments'}`}
            </Button>
          </>
        ) : (
          <>
            <Button onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button
              type="submit"
              variant="contained"
              loading={resolve.isPending}
              disabled={entries.length === 0 || entries.length > maxSymbols}
            >
              Look up
            </Button>
          </>
        )}
      </AppDialogFooter>
    </AppDialog>
  );
}
