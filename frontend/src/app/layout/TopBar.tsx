import DarkModeIcon from '@mui/icons-material/DarkModeOutlined';
import LightModeIcon from '@mui/icons-material/LightModeOutlined';
import MenuIcon from '@mui/icons-material/Menu';
import { AppBar, Box, IconButton, Skeleton, Toolbar, Tooltip, Typography } from '@mui/material';
import { Suspense } from 'react';
import { NotificationBell } from '@/features/notifications';
import { HeaderAccounts } from '@/features/accounts';
import { UserMenu } from '@/features/auth';
import { IstClock } from '@/shared/ui/IstClock';
import { useColorMode } from '@/theme';
import { SearchButton } from './palette/SearchButton';

export function TopBar({ title, onMenu }: { title: string; onMenu: () => void }) {
  const { mode, toggle } = useColorMode();
  return (
    <AppBar position="sticky">
      <Toolbar
        sx={(theme) => ({
          gap: 1,
          [theme.breakpoints.down('sm')]: { gap: 0.5 },
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
          sx={{ fontWeight: 650, minWidth: 0, flexShrink: 1, display: { xs: 'none', sm: 'block' } }}
        >
          {title}
        </Typography>
        <SearchButton />
        <Box sx={{ flex: 1 }} />
        {/* India time, whatever the browser's zone: next to the other status controls. */}
        <IstClock />
        {/* Same size as the control's button, so it loading in does not move the other controls. */}
        <Suspense fallback={<Skeleton variant="rounded" width={64} height={30} sx={{ minWidth: { sm: 250 } }} />}>
          <HeaderAccounts />
        </Suspense>
        {/* Same size as the bell, so it loading in does not move the other controls. */}
        <Suspense fallback={<Skeleton variant="circular" width={40} height={40} />}>
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
