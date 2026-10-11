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
  /** Less space below the header, for screens that fill the viewport (the content under it needs the height). */
  dense?: boolean;
}

/** Standard page title block: every page starts with one. */
export function PageHeader({ title, description, actions, icon, hue, dense = false }: PageHeaderProps) {
  return (
    <Stack
      direction={{ xs: 'column', sm: 'row' }}
      spacing={2}
      sx={{ mb: dense ? 2 : 3, justifyContent: 'space-between', alignItems: { sm: 'flex-end' } }}
    >
      <Box>
        <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
          {icon && hue && <IconChip icon={icon} hue={hue} size="sm" />}
          <Typography variant="h1">{title}</Typography>
        </Stack>
        {description && (
          // The shell is full width; a description keeps a readable line length of its own.
          <Typography color="text.secondary" sx={{ mt: 0.5, maxWidth: '90ch' }}>
            {description}
          </Typography>
        )}
      </Box>
      {actions && (
        <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
          {actions}
        </Stack>
      )}
    </Stack>
  );
}
