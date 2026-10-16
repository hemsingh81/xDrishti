import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import LogoutIcon from '@mui/icons-material/LogoutOutlined';
import ManageAccountsIcon from '@mui/icons-material/ManageAccountsOutlined';
import { Avatar, Box, Button, Divider, ListItemIcon, Menu, MenuItem, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { palettes } from '@/theme';
import { useLogout } from '../api/authMutations';
import { useSession } from '../api/sessionQuery';

function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return parts
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('');
}

/** Top-bar account control: the owner's name and a menu with the signed-in email, My account and Sign out. */
export function UserMenu() {
  const { data } = useSession();
  const logout = useLogout();
  const navigate = useNavigate();
  const { mode } = useTheme().palette;
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);

  if (data?.status !== 'authenticated') {
    return null;
  }
  const { displayName, email } = data.session.user;

  return (
    <>
      <Button
        color="inherit"
        aria-label={`Account menu, ${displayName}`}
        aria-haspopup="menu"
        aria-expanded={anchor ? true : undefined}
        onClick={(event) => {
          setAnchor(event.currentTarget);
        }}
        sx={{ textTransform: 'none', minWidth: 0, gap: 1, px: { xs: 0.5, lg: 1 } }}
      >
        {/* A normal child, not startIcon: that slot forces a 20 px font on the initials, which then overflow the circle. */}
        <Avatar
          aria-hidden
          sx={{
            width: 28,
            height: 28,
            fontSize: 13,
            fontWeight: 700,
            bgcolor: palettes[mode].accentSoft,
            color: palettes[mode].accent,
          }}
        >
          {initialsOf(displayName)}
        </Avatar>
        <Box component="span" sx={{ display: { xs: 'none', lg: 'inline' } }}>
          {displayName}
        </Box>
        <ExpandMoreIcon fontSize="small" aria-hidden sx={{ display: { xs: 'none', sm: 'block' }, ml: -0.5 }} />
      </Button>
      <Menu
        anchorEl={anchor}
        open={Boolean(anchor)}
        onClose={() => {
          setAnchor(null);
        }}
        slotProps={{ list: { 'aria-label': 'Account' } }}
      >
        <Box sx={{ px: 2, py: 1 }}>
          <Typography sx={{ fontWeight: 600 }}>{displayName}</Typography>
          <Typography variant="caption" color="text.secondary">
            {email}
          </Typography>
        </Box>
        <Divider />
        <MenuItem
          onClick={() => {
            setAnchor(null);
            void navigate('/account');
          }}
        >
          <ListItemIcon>
            <ManageAccountsIcon fontSize="small" />
          </ListItemIcon>
          My account
        </MenuItem>
        <MenuItem
          disabled={logout.isPending}
          onClick={() => {
            logout.mutate();
          }}
        >
          <ListItemIcon>
            <LogoutIcon fontSize="small" />
          </ListItemIcon>
          {logout.isPending ? 'Signing out…' : 'Sign out'}
        </MenuItem>
        {logout.isError && (
          <Typography role="alert" variant="caption" color="error" sx={{ px: 2, pb: 1, display: 'block' }}>
            Could not sign out. Please try again.
          </Typography>
        )}
      </Menu>
    </>
  );
}
