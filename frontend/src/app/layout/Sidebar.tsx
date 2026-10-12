import { Box, List, ListSubheader, Typography } from '@mui/material';
import { BrandMark } from '@/shared/ui';
import { navigation } from '../navigation';
import { NavBadgeSource } from './navBadges';
import { NavRow } from './NavRow';

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
          {section.items.map((item) =>
            item.badge ? (
              <NavBadgeSource key={item.path} id={item.badge}>
                {(badge) => <NavRow item={item} badge={badge} onNavigate={onNavigate} />}
              </NavBadgeSource>
            ) : (
              <NavRow key={item.path} item={item} onNavigate={onNavigate} />
            ),
          )}
        </List>
      ))}
    </Box>
  );
}
