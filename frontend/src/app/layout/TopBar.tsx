import DarkModeIcon from '@mui/icons-material/DarkModeOutlined';
import LightModeIcon from '@mui/icons-material/LightModeOutlined';
import MenuIcon from '@mui/icons-material/Menu';
import { AppBar, Box, IconButton, Skeleton, Toolbar, Tooltip, Typography } from '@mui/material';
import { Suspense } from 'react';
import { NotificationBell } from '@/features/notifications';
import { HeaderAccounts } from '@/features/accounts';
import { UserMenu } from '@/features/auth';
import { IstClock } from '@/shared/ui/IstClock';
import { layout, rem, useColorMode } from '@/theme';
import { SearchButton } from './palette/SearchButton';

export function TopBar({ title, onMenu }: { title: string; onMenu: () => void }) {
  const { mode, toggle } = useColorMode();
  return (
    <AppBar position="sticky">
      <Toolbar
        sx={(theme) => ({
          // The prototype's `.top`: 60 px high, 10 px gaps, 24 px side padding (12 px on phones).
          // MUI's toolbar sets `min-height` in media queries, which sort after plain rules: the doubled class wins.
          // The 1 px border of the bar is part of the 60 px.
          '&.MuiToolbar-root': { minHeight: layout.topBarHeight - 1, height: layout.topBarHeight - 1 },
          gap: '10px',
          paddingInline: '24px',
          [theme.breakpoints.down('md')]: { paddingInline: '12px', gap: '8px' },
          // On a phone the bar wraps to a second row when the text is enlarged, so nothing is pushed off the screen (WCAG 1.4.10).
          [theme.breakpoints.down('sm')]: { gap: 0.5, flexWrap: 'wrap', '&.MuiToolbar-root': { height: 'auto' } },
          // The narrowest phones (320 px): tighter padding and gaps, so the avatar is not squeezed out of the bar.
          [theme.breakpoints.down(360)]: { px: 1, gap: 0.25 },
        })}
      >
        <IconButton edge="start" aria-label="Open navigation" onClick={onMenu} sx={{ display: { md: 'none' } }}>
          <MenuIcon />
        </IconButton>
        {/* Hidden on phones: the page has its own header and the controls need the width. */}
        <Typography
          component="h2"
          noWrap
          sx={{
            fontSize: rem(16),
            fontWeight: 650,
            letterSpacing: '-0.01em',
            minWidth: 0,
            flexShrink: 1,
            display: { xs: 'none', sm: 'block' },
          }}
        >
          {title}
        </Typography>
        <SearchButton />
        <Box sx={{ flex: 1 }} />
        {/* India time, whatever the browser's zone: next to the other status controls. */}
        <IstClock />
        {/* Same size as the control's button, so it loading in does not move the other controls. */}
        <Suspense
          fallback={
            <Skeleton
              variant="rounded"
              sx={{ width: { xs: 58, sm: 64 }, height: { xs: 44, sm: 30 }, minWidth: { sm: 250 } }}
            />
          }
        >
          <HeaderAccounts />
        </Suspense>
        {/* Same size as the bell, so it loading in does not move the other controls. */}
        <Suspense
          fallback={<Skeleton variant="rounded" sx={{ width: { xs: 44, sm: 36 }, height: { xs: 44, sm: 36 } }} />}
        >
          <NotificationBell />
        </Suspense>
        <Tooltip title={mode === 'dark' ? 'Light mode' : 'Dark mode'}>
          <IconButton aria-label="Toggle colour mode" onClick={toggle}>
            {mode === 'dark' ? <LightModeIcon /> : <DarkModeIcon />}
          </IconButton>
        </Tooltip>
        <UserMenu />
      </Toolbar>
    </AppBar>
  );
}
