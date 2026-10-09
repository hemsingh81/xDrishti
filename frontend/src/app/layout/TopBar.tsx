import DarkModeIcon from '@mui/icons-material/DarkModeOutlined';
import LightModeIcon from '@mui/icons-material/LightModeOutlined';
import MenuIcon from '@mui/icons-material/Menu';
import { AppBar, IconButton, Toolbar, Tooltip, Typography } from '@mui/material';
import { useColorMode } from '@/theme';

export function TopBar({ title, onMenu }: { title: string; onMenu: () => void }) {
  const { mode, toggle } = useColorMode();
  return (
    <AppBar position="sticky" sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
      <Toolbar sx={{ gap: 1 }}>
        <IconButton edge="start" aria-label="Open navigation" onClick={onMenu} sx={{ display: { md: 'none' } }}>
          <MenuIcon />
        </IconButton>
        <Typography component="h2" sx={{ fontWeight: 650, flex: 1 }}>
          {title}
        </Typography>
        <Tooltip title={mode === 'dark' ? 'Light mode' : 'Dark mode'}>
          <IconButton aria-label="Toggle colour mode" onClick={toggle}>
            {mode === 'dark' ? <LightModeIcon /> : <DarkModeIcon />}
          </IconButton>
        </Tooltip>
      </Toolbar>
    </AppBar>
  );
}
