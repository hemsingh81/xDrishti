import { Box, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import { monoFontFamily } from '@/theme';
import { CopyButton } from './CopyButton';

/** The redirect URL to register in the broker's developer console: read-only, monospace, with a copy button. */
export function RedirectUrl({ url }: { url: string }) {
  const labelId = useId();
  return (
    <Stack spacing={0.75}>
      <Typography id={labelId} variant="subtitle2" component="p">
        Redirect URL
      </Typography>
      <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
        <Box
          component="code"
          aria-labelledby={labelId}
          sx={{
            fontFamily: monoFontFamily,
            bgcolor: 'action.hover',
            border: 1,
            borderColor: 'divider',
            borderRadius: 1,
            px: 1.25,
            py: 0.75,
            overflowWrap: 'anywhere',
            minWidth: 0,
            userSelect: 'all',
          }}
        >
          {url}
        </Box>
        <CopyButton text={url} label="Copy redirect URL" />
      </Stack>
      <Typography variant="body2" color="text.secondary">
        Register it exactly as shown — the broker compares it character by character.
      </Typography>
    </Stack>
  );
}
