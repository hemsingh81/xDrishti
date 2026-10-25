import CloseIcon from '@mui/icons-material/Close';
import { Box, Divider, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';

interface RecordDrawerProps {
  title: string;
  subtitle?: string;
  closeLabel: string;
  onClose: () => void;
  children: ReactNode;
}

/** The right-hand drawer of a stored record: full width on a phone, a close button of 44 px. Read only. */
export function RecordDrawer({ title, subtitle, closeLabel, onClose, children }: RecordDrawerProps) {
  const titleId = useId();
  return (
    <Drawer
      anchor="right"
      open
      onClose={onClose}
      slotProps={{ paper: { role: 'dialog', 'aria-labelledby': titleId, sx: { width: 640, maxWidth: '100vw' } } }}
    >
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 1, px: 2.5, py: 1.5 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography id={titleId} component="h2" variant="h2" sx={{ overflowWrap: 'anywhere' }}>
            {title}
          </Typography>
          {subtitle && (
            <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
              {subtitle}
            </Typography>
          )}
        </Box>
        <IconButton aria-label={closeLabel} edge="end" onClick={onClose} sx={{ minWidth: 44, minHeight: 44 }}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Divider />
      <Box sx={{ p: 2.5, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 3 }}>{children}</Box>
    </Drawer>
  );
}
