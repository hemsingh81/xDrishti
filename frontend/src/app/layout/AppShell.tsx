import { Box, Drawer } from '@mui/material';
import { Suspense, useState } from 'react';
import { Outlet, useLocation } from 'react-router';
import { LoadingState } from '@/shared/ui';
import { layout } from '@/theme';
import { allNavItems } from '../navigation';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

/** Persistent layout: sidebar (permanent on desktop, drawer on mobile), top bar and the routed page (full width). */
export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const { pathname } = useLocation();
  const title = allNavItems.find((i) => i.path === pathname)?.label ?? 'xDrishti';
  const paper = { width: layout.sidebarWidth, boxSizing: 'border-box' as const };

  return (
    <Box sx={{ display: 'flex', minHeight: '100dvh', height: { md: '100dvh' } }}>
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
      <Box sx={{ flex: 1, minWidth: 0, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TopBar
          title={title}
          onMenu={() => {
            setMobileOpen(true);
          }}
        />
        {/* Full width: padding only. From `md` the page is the viewport height and `main` is the one scroll container, so
            a screen can fill it (`height: 100%`) and scroll a region of its own; below `md` the document scrolls. */}
        <Box
          component="main"
          sx={{ p: { xs: 2, md: 3 }, width: '100%', flex: 1, minHeight: 0, overflowY: { md: 'auto' } }}
        >
          <Suspense fallback={<LoadingState />}>
            <Outlet />
          </Suspense>
        </Box>
      </Box>
    </Box>
  );
}
