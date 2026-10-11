import CheckIcon from '@mui/icons-material/Check';
import ContentCopyIcon from '@mui/icons-material/ContentCopyOutlined';
import { Button } from '@mui/material';
import { useEffect, useState } from 'react';

type CopyState = 'idle' | 'copied' | 'failed';

/** Copies `text` to the clipboard and says so (the label changes, announced politely). */
export function CopyButton({ text, label = 'Copy' }: { text: string; label?: string }) {
  const [state, setState] = useState<CopyState>('idle');

  useEffect(() => {
    if (state === 'idle') {
      return undefined;
    }
    const timer = setTimeout(() => {
      setState('idle');
    }, 2500);
    return () => {
      clearTimeout(timer);
    };
  }, [state]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setState('copied');
    } catch {
      setState('failed'); // no clipboard access (e.g. insecure context): the text can still be selected by hand
    }
  }

  return (
    <Button
      size="small"
      variant="outlined"
      startIcon={state === 'copied' ? <CheckIcon /> : <ContentCopyIcon />}
      onClick={() => void copy()}
      aria-live="polite"
    >
      {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed — select the text' : label}
    </Button>
  );
}
