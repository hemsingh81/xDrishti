import { Card, CardContent, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';

interface SectionProps {
  title: string;
  /** One line under the title: what the card shows and on what basis. */
  note?: string;
  children: ReactNode;
}

/** A titled card: the page's unit of layout. The heading names the region. */
export function Section({ title, note, children }: SectionProps) {
  const titleId = useId();
  return (
    <Card component="section" aria-labelledby={titleId} sx={{ minWidth: 0 }}>
      <CardContent sx={{ display: 'grid', gap: 1.5 }}>
        <div>
          <Typography id={titleId} variant="h2" component="h2">
            {title}
          </Typography>
          {note && (
            <Typography variant="caption" color="text.secondary" component="p" sx={{ m: 0 }}>
              {note}
            </Typography>
          )}
        </div>
        {children}
      </CardContent>
    </Card>
  );
}
