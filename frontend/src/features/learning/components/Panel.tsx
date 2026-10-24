import { Card, CardContent, Stack, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';

interface PanelProps {
  title: string;
  /** Controls beside the title (a selector). */
  actions?: ReactNode;
  children: ReactNode;
}

/** A titled card section: the page's unit of layout. The heading names the region. */
export function Panel({ title, actions, children }: PanelProps) {
  const titleId = useId();
  return (
    <Card component="section" aria-labelledby={titleId} sx={{ minWidth: 0 }}>
      <CardContent>
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          sx={{ justifyContent: 'space-between', alignItems: { sm: 'center' }, gap: 1, mb: 1.5 }}
        >
          <Typography id={titleId} variant="h2" component="h2">
            {title}
          </Typography>
          {actions}
        </Stack>
        {children}
      </CardContent>
    </Card>
  );
}
