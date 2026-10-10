import LogoutIcon from '@mui/icons-material/LogoutOutlined';
import { Avatar, Box, Button, Divider, ListItemIcon, Menu, MenuItem, Typography } from '@mui/material';
import { useState } from 'react';
import { useLogout } from '../api/authMutations';
import { useSession } from '../api/sessionQuery';

function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return parts
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('');
}

/** Top-bar account control: the owner's name and a menu with the signed-in email and Sign out. */
export function UserMenu() {
  const { data } = useSession();
  const logout = useLogout();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);

  if (data?.status !== 'authenticated') {
    return null;
  }
  const { displayName, email } = data.session.user;

  return (
    <>
      <Button
        color="inherit"
        aria-label="Account menu"
        aria-haspopup="menu"
        aria-expanded={anchor ? true : undefined}
        onClick={(event) => {
          setAnchor(event.currentTarget);
        }}
        startIcon={
          <Avatar sx={{ width: 28, height: 28, fontSize: 13, bgcolor: 'primary.main' }}>
            {initialsOf(displayName)}
          </Avatar>
        }
        sx={{ textTransform: 'none', minWidth: 0, '& .MuiButton-startIcon': { mr: { xs: 0, sm: 1 } } }}
      >
        <Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}>
          {displayName}
        </Box>
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
