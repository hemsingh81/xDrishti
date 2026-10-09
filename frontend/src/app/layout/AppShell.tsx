import { Box, Drawer } from '@mui/material';
import { Suspense, useState } from 'react';
import { Outlet, useLocation } from 'react-router';
import { LoadingState } from '@/shared/ui';
import { layout } from '@/theme';
import { allNavItems } from '../navigation';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

/** Persistent layout: sidebar (permanent on desktop, drawer on mobile), top bar and the routed page. */
export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const { pathname } = useLocation();
  const title = allNavItems.find((i) => i.path === pathname)?.label ?? 'xDrishti';
  const paper = { width: layout.sidebarWidth, boxSizing: 'border-box' as const };

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      <Box sx={{ width: { md: layout.sidebarWidth }, flexShrink: { md: 0 } }}>
        <Drawer
          variant="temporary"
          open={mobileOpen}
          onClose={() => {
            setMobileOpen(false);
          }}
          sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': paper }}
        >
          <Sidebar
            onNavigate={() => {
              setMobileOpen(false);
            }}
          />
        </Drawer>
        <Drawer variant="permanent" open sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': paper }}>
          <Sidebar />
        </Drawer>
      </Box>
      <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
        <TopBar
          title={title}
          onMenu={() => {
            setMobileOpen(true);
          }}
        />
        <Box component="main" sx={{ p: { xs: 2, md: 3 }, maxWidth: 1560, width: '100%' }}>
          <Suspense fallback={<LoadingState />}>
            <Outlet />
          </Suspense>
        </Box>
      </Box>
    </Box>
  );
}
