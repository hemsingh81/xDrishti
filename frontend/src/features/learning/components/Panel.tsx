import { Card, CardContent, Stack, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';

interface PanelProps {
  title: string;
  /** Controls beside the title (a selector). */
  actions?: ReactNode;
  /** Height (px) to hold while the panel's query is pending: the settled height, so what follows does not jump (layout shift). */
  reserve?: { xs: number; md: number };
  children: ReactNode;
}

/** A titled card section: the page's unit of layout. The heading names the region. */
export function Panel({ title, actions, reserve, children }: PanelProps) {
  const titleId = useId();
  return (
    <Card component="section" aria-labelledby={titleId} sx={{ minWidth: 0, minHeight: reserve }}>
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
