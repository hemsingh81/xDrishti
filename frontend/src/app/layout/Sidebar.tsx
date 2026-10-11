import { Box, List, ListItemButton, ListItemIcon, ListItemText, ListSubheader, Typography } from '@mui/material';
import { NavLink } from 'react-router';
import { BrandMark, IconChip } from '@/shared/ui';
import { navHue } from '@/theme';
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
              sx={{ mb: 0.25 }}
            >
              <ListItemIcon sx={{ minWidth: 40 }}>
                <IconChip icon={Icon} hue={navHue[path] ?? 'slate'} size="sm" />
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
