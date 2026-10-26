import { Box, Drawer, Link } from '@mui/material';
import { useIsFetching } from '@tanstack/react-query';
import { Suspense, useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router';
import { AttentionPopups } from '@/features/notifications';
import { LoadingState } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { layout, pagePadding } from '@/theme';
import { allNavItems, routeTitles } from '../navigation';
import { Sidebar } from './Sidebar';
import { TopBar } from './TopBar';

const mainId = 'main-content';

/**
 * True once the route's queries have been settled for a moment (the `data-ready` marker of `main`): the browser runner and
 * the performance figures wait for it instead of guessing from timers. It resets on every navigation.
 */
function useSettled(routeKey: string): boolean {
  const fetching = useIsFetching();
  const [settled, setSettled] = useState<string | null>(null);
  useEffect(() => {
    if (fetching > 0) return;
    let timer = 0;
    const settle = () => {
      // A route's own loading state (the spinner of LoadingState, a status region) means its chunk or data is still coming.
      if (document.querySelector(`#${mainId} [role="status"] > [role="progressbar"]`)) {
        timer = window.setTimeout(settle, 150);
      } else {
        setSettled(routeKey);
      }
    };
    timer = window.setTimeout(settle, 150);
    return () => {
      window.clearTimeout(timer);
    };
  }, [fetching, routeKey]);
  return settled === routeKey && fetching === 0;
}

/**
 * Persistent layout: sidebar (permanent on desktop, drawer on mobile), top bar and the routed page (full width). It owns the
 * one ToastProvider of the app, so a toast raised anywhere (a page, a dialog, the bell) shows, and the attention pop-ups.
 */
export function AppShell() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const { pathname, search } = useLocation();
  const ready = useSettled(pathname + search);
  const title = allNavItems.find((i) => i.path === pathname)?.label ?? routeTitles[pathname] ?? 'xDrishti';
  const paper = { width: layout.sidebarWidth, boxSizing: 'border-box' as const };

  return (
    <ToastProvider>
      {/* First tab stop: jumps over the navigation to the page. Hidden until it has focus. */}
      <Link
        href={`#${mainId}`}
        onClick={(event) => {
          event.preventDefault();
          document.getElementById(mainId)?.focus();
        }}
        sx={(theme) => ({
          position: 'absolute',
          left: theme.spacing(1),
          top: theme.spacing(1),
          // Hidden (still focusable) until it has focus.
          '&:not(:focus)': {
            clipPath: 'inset(50%)',
            width: '1px',
            height: '1px',
            p: 0,
            overflow: 'hidden',
            whiteSpace: 'nowrap',
          },
          zIndex: theme.zIndex.tooltip,
          padding: theme.spacing(1, 2),
          borderRadius: 1,
          bgcolor: 'background.paper',
          boxShadow: theme.shadows[4],
        })}
      >
        Skip to content
      </Link>
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
            id={mainId}
            data-ready={ready ? '' : undefined}
            tabIndex={-1}
            sx={(theme) => ({
              // The prototype's `.page` padding: 14 px on phones, 24 from the sidebar breakpoint, more room on wide monitors.
              padding: `${pagePadding.compact}px`,
              [theme.breakpoints.up('md')]: { padding: `${pagePadding.base}px` },
              [`@media (min-width:${pagePadding.wide.minWidth}px)`]: {
                padding: `${pagePadding.wide.y}px ${pagePadding.wide.x}px`,
              },
              [`@media (min-width:${pagePadding.ultra.minWidth}px)`]: {
                padding: `${pagePadding.ultra.y}px ${pagePadding.ultra.x}px`,
              },
              width: '100%',
              outline: 'none',
              flex: 1,
              minHeight: 0,
              overflowY: { md: 'auto' },
            })}
          >
            <Suspense fallback={<LoadingState />}>
              <Outlet />
            </Suspense>
          </Box>
        </Box>
      </Box>
      {/* The items that need the owner pop up once each; loaded after the first paint, renders nothing. */}
      <Suspense fallback={null}>
        <AttentionPopups />
      </Suspense>
    </ToastProvider>
  );
}
