import { Box, Stack, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';
import { QualityStatusChip } from '../QualityStatusChip';

interface CheckSectionProps {
  title: string;
  status: string;
  children: ReactNode;
}

/** One check in the drawer: its title with the result as a chip, then the explanation and the evidence. */
export function CheckSection({ title, status, children }: CheckSectionProps) {
  const titleId = useId();
  return (
    <Box
      component="section"
      aria-labelledby={titleId}
      sx={{ py: 2, borderTop: 1, borderColor: 'divider', '&:first-of-type': { borderTop: 0 } }}
    >
      <Stack direction="row" sx={{ gap: 1, mb: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography id={titleId} variant="h3">
          {title}
        </Typography>
        <QualityStatusChip status={status} />
      </Stack>
      <Stack spacing={1}>{children}</Stack>
    </Box>
  );
}
