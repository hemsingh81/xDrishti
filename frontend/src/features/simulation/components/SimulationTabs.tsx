import { Button, Stack } from '@mui/material';
import { Link } from 'react-router';

const tabs = [
  { to: '/reports/simulation', label: 'Reports' },
  { to: '/simulation/replay', label: 'System replay' },
] as const;

/** The two views of the Simulation screen. Each is its own route, so the links carry the current page. */
export function SimulationTabs({ current }: { current: (typeof tabs)[number]['to'] }) {
  return (
    <Stack component="nav" aria-label="Simulation views" direction="row" sx={{ gap: 1, mb: 2, overflowX: 'auto' }}>
      {tabs.map((tab) => (
        <Button
          key={tab.to}
          component={Link}
          to={tab.to}
          size="small"
          variant={tab.to === current ? 'contained' : 'outlined'}
          aria-current={tab.to === current ? 'page' : undefined}
          sx={{ minHeight: 44, whiteSpace: 'nowrap' }}
        >
          {tab.label}
        </Button>
      ))}
    </Stack>
  );
}
