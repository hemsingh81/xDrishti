import { Card, CardContent, Typography } from '@mui/material';
import { useLocation } from 'react-router';
import { PageHeader } from '@/shared/ui';
import { navHue } from '@/theme';
import { allNavItems } from '../navigation';

/** Placeholder for screens on the roadmap; the clickable prototype shows how they will look. */
export function ComingSoonPage() {
  const { pathname } = useLocation();
  const item = allNavItems.find((i) => i.path === pathname);
  return (
    <>
      <PageHeader
        title={item?.label ?? 'Coming soon'}
        icon={item?.icon}
        hue={item ? (navHue[item.path] ?? 'slate') : undefined}
      />
      <Card sx={{ maxWidth: 720 }}>
        <CardContent>
          <Typography>
            This screen is planned for roadmap phase <strong>{item?.phase ?? '—'}</strong>. See the clickable prototype
            in <code>docs/prototype</code> for its design.
          </Typography>
        </CardContent>
      </Card>
    </>
  );
}
