import { Box, Stack, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import type { ReactNode } from 'react';
import type { IconHueName } from '@/theme';
import { IconChip } from './IconChip';

interface PageHeaderProps {
  title: string;
  description?: string;
  actions?: ReactNode;
  /** Optional icon chip before the title; needs `hue` too. */
  icon?: SvgIconComponent;
  hue?: IconHueName;
}

/** Standard page title block: every page starts with one. */
export function PageHeader({ title, description, actions, icon, hue }: PageHeaderProps) {
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={2}
      sx={{ mb: 3, justifyContent: 'space-between', alignItems: { sm: 'flex-end' } }}
    >
      <Box>
        <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
          {icon && hue && <IconChip icon={icon} hue={hue} size="sm" />}
          <Typography variant="h1">{title}</Typography>
        </Stack>
        {description && (
          <Typography color="text.secondary" sx={{ mt: 0.5 }}>
            {description}
          </Typography>
        )}
      </Box>
      {actions && (
        <Stack direction="row" spacing={1}>
          {actions}
        </Stack>
      )}
    </Stack>
  );
}
