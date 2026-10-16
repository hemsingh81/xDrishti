import type { SvgIconComponent } from '@mui/icons-material';
import { Card, CardContent, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { IconChip } from '@/shared/ui';
import type { IconHueName } from '@/theme';

interface AccountCardProps {
  /** Id prefix: the heading is `${id}-title` and names the region. */
  id: string;
  title: string;
  description: ReactNode;
  icon: SvgIconComponent;
  hue: IconHueName;
  children: ReactNode;
}

/** The frame of one section of the My account page: icon chip, title, one line of help, then the content. */
export function AccountCard({ id, title, description, icon, hue, children }: AccountCardProps) {
  return (
    <Card component="section" aria-labelledby={`${id}-title`}>
      <CardContent>
        <Stack direction="row" spacing={1.5} sx={{ mb: 2, alignItems: 'flex-start' }}>
          <IconChip icon={icon} hue={hue} size="md" />
          <Stack spacing={0.25} sx={{ minWidth: 0 }}>
            <Typography id={`${id}-title`} variant="h2" component="h2">
              {title}
            </Typography>
            <Typography color="text.secondary" variant="body2" component="div">
              {description}
            </Typography>
          </Stack>
        </Stack>
        {children}
      </CardContent>
    </Card>
  );
}
