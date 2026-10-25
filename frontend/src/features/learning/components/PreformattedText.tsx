import { Box, Typography } from '@mui/material';
import { useId } from 'react';

/** Server text kept as written (a report, parameters): a labelled, scrollable block of text nodes, copyable. */
export function PreformattedText({ label, text }: { label: string; text: string }) {
  const id = useId();
  return (
    <Box>
      <Typography id={id} variant="subtitle2" component="h4" sx={{ mb: 0.5 }}>
        {label}
      </Typography>
      <Box
        component="pre"
        role="region"
        aria-labelledby={id}
        tabIndex={0}
        sx={{
          m: 0,
          p: 1.5,
          maxHeight: 400,
          overflow: 'auto',
          border: 1,
          borderColor: 'divider',
          borderRadius: 1,
          fontFamily: 'monospace',
          fontSize: '0.8rem',
          whiteSpace: 'pre',
          userSelect: 'text',
        }}
      >
        {text.length > 0 ? text : 'Nothing was stored.'}
      </Box>
    </Box>
  );
}
