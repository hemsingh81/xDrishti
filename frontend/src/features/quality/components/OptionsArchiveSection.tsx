import { Box, Button } from '@mui/material';
import { lazy, Suspense, useState } from 'react';

const OptionsArchiveCard = lazy(async () => ({
  default: (await import('./OptionsArchiveCard')).OptionsArchiveCard,
}));

/** The options archive checks, closed by default so nothing is read or loaded until the owner opens it. */
export function OptionsArchiveSection() {
  const [open, setOpen] = useState(false);
  return (
    <Box sx={{ flexShrink: 0, mt: 1 }}>
      <Button
        size="small"
        aria-expanded={open}
        aria-controls="options-archive-panel"
        onClick={() => {
          setOpen((v) => !v);
        }}
      >
        Options archive
      </Button>
      {open && (
        <Box id="options-archive-panel" sx={{ maxHeight: { md: 320 }, overflow: 'auto' }}>
          <Suspense fallback={null}>
            <OptionsArchiveCard />
          </Suspense>
        </Box>
      )}
    </Box>
  );
}
