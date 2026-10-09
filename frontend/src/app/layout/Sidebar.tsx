import { Box, List, ListItemButton, ListItemIcon, ListItemText, ListSubheader, Typography } from '@mui/material';
import { NavLink } from 'react-router';
import { BrandMark } from '@/shared/ui';
import { navigation } from '../navigation';

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  return (
    <Box component="nav" aria-label="Main" sx={{ height: '100%', overflowY: 'auto', px: 1.5, py: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, px: 1, pb: 2 }}>
        <BrandMark />
        <Typography sx={{ fontWeight: 800, fontSize: 17 }}>
          <Box component="span" sx={{ color: 'primary.main' }}>
            x
          </Box>
          Drishti
        </Typography>
      </Box>
      {navigation.map((section) => (
        <List
          key={section.title}
          dense
          disablePadding
          subheader={
            <ListSubheader
              disableSticky
              sx={{
                bgcolor: 'transparent',
                lineHeight: 2.4,
                fontSize: 11,
                fontWeight: 700,
                letterSpacing: '0.08em',
                textTransform: 'uppercase',
              }}
            >
              {section.title}
            </ListSubheader>
          }
        >
          {section.items.map(({ path, label, icon: Icon, phase }) => (
            <ListItemButton
              key={path}
              component={NavLink}
              to={path}
              end={path === '/'}
              onClick={onNavigate}
              sx={{
                borderRadius: 2,
                mb: 0.25,
                '&.active': {
                  bgcolor: 'action.selected',
                  color: 'primary.main',
                  '& .MuiListItemIcon-root': { color: 'primary.main' },
                },
              }}
            >
              <ListItemIcon sx={{ minWidth: 36 }}>
                <Icon fontSize="small" />
              </ListItemIcon>
              <ListItemText
                primary={label}
                slotProps={{ primary: { sx: { fontWeight: 550, opacity: phase ? 0.7 : 1 } } }}
              />
            </ListItemButton>
          ))}
        </List>
      ))}
    </Box>
  );
}
