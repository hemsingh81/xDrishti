import { Box, Tab, Tabs } from '@mui/material';
import { lazy, Suspense } from 'react';
import { useSearchParams } from 'react-router';
import { RiskProfilesView, RoleMatrixView } from '@/features/accounts';
import { LazyBoundary, LoadingState, ReloadAlert } from '@/shared/ui';
import { ActionThresholdsCard } from './ActionThresholdsCard';

// The owner's confirmation of the charge sheet loads on demand: the first load of Settings does not carry it.
const ChargeSheetCard = lazy(() => import('./ChargeSheetCard'));
// The event-risk filter's switch card (P15-11) loads on demand too.
const EventFilterCard = lazy(() => import('./EventFilterCard'));

const settingsTabs = [
  { key: 'profiles', label: 'Risk profiles' },
  { key: 'roles', label: 'Account roles' },
  { key: 'actions', label: 'Action center' },
  { key: 'charge-sheet', label: 'Charge sheet' },
  { key: 'selection', label: 'Selection' },
] as const;

type SettingsTab = (typeof settingsTabs)[number]['key'];

const parseTab = (value: string | null): SettingsTab =>
  settingsTabs.find((tab) => tab.key === value)?.key ?? 'profiles';

/** The existing settings tabs, one step under `Advanced settings`: risk profiles, account roles, Action center thresholds, the charge sheet's confirmation and the event filter. The open tab lives in the URL. */
export function AdvancedSettings() {
  const [params, setParams] = useSearchParams();
  const tab = parseTab(params.get('tab'));

  return (
    <>
      <Tabs
        value={tab}
        onChange={(_event, next: SettingsTab) => {
          setParams({ tab: next }, { replace: true });
        }}
        variant="scrollable"
        scrollButtons="auto"
        allowScrollButtonsMobile
        aria-label="Settings sections"
        sx={{ mb: 2.5, borderBottom: 1, borderColor: 'divider' }}
      >
        {settingsTabs.map(({ key, label }) => (
          <Tab key={key} value={key} label={label} id={`settings-tab-${key}`} aria-controls={`settings-panel-${key}`} />
        ))}
      </Tabs>
      <Box role="tabpanel" id={`settings-panel-${tab}`} aria-labelledby={`settings-tab-${tab}`}>
        {tab === 'profiles' && (
          <LazyBoundary
            pending={<LoadingState label="Loading risk profiles" />}
            failed={<ReloadAlert label="Risk profiles" />}
          >
            <RiskProfilesView />
          </LazyBoundary>
        )}
        {tab === 'roles' && (
          <LazyBoundary
            pending={<LoadingState label="Loading the role matrix" />}
            failed={<ReloadAlert label="The role matrix" />}
          >
            <RoleMatrixView />
          </LazyBoundary>
        )}
        {tab === 'actions' && <ActionThresholdsCard />}
        {tab === 'charge-sheet' && (
          <Suspense fallback={<LoadingState label="Loading the confirmation" />}>
            <ChargeSheetCard />
          </Suspense>
        )}
        {tab === 'selection' && (
          <Suspense fallback={<LoadingState label="Loading the event filter" />}>
            <EventFilterCard />
          </Suspense>
        )}
      </Box>
    </>
  );
}
