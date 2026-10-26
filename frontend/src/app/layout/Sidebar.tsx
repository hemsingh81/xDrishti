import { Box, List, ListSubheader, Typography } from '@mui/material';
import { BrandMark } from '@/shared/ui';
import { rem, sidebar } from '@/theme';
import { navigation } from '../navigation';
import { NavBadgeSource } from './navBadges';
import { NavRow } from './NavRow';

export function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  return (
    <Box
      component="nav"
      aria-label="Main"
      sx={{
        height: '100%',
        overflowY: 'auto',
        px: `${sidebar.paddingX}px`,
        py: 2,
        display: 'flex',
        flexDirection: 'column',
        gap: '1px',
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: '10px', pt: 0.5, px: 1, pb: 2 }}>
        <BrandMark />
        <Typography sx={{ fontWeight: 800, fontSize: rem(17), letterSpacing: '-0.01em', whiteSpace: 'nowrap' }}>
          <Box component="span" sx={{ color: 'primary.main' }}>
            x
          </Box>
          Drishti
        </Typography>
      </Box>
      {navigation.map((section) => (
        <List
          key={section.title}
          component="div"
          disablePadding
          subheader={
            <ListSubheader
              component="div"
              disableSticky
              sx={{
                bgcolor: 'transparent',
                // The prototype's `.side .sec`: 10.5 px caps, 14 px above and 5 px below.
                padding: '14px 10px 5px',
                lineHeight: 1.5,
                fontSize: rem(10.5),
                fontWeight: 700,
                letterSpacing: '0.09em',
                textTransform: 'uppercase',
                whiteSpace: 'nowrap',
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
