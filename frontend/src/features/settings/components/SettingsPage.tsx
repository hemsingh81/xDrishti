import TuneIcon from '@mui/icons-material/TuneOutlined';
import { Box, Tab, Tabs } from '@mui/material';
import { lazy, Suspense } from 'react';
import { useSearchParams } from 'react-router';
import { RiskProfilesView, RoleMatrixView } from '@/features/accounts';
import { LoadingState, PageHeader } from '@/shared/ui';
import { ActionThresholdsCard } from './ActionThresholdsCard';

// The owner's confirmation of the charge sheet loads on demand: the first load of Settings does not carry it.
const ChargeSheetCard = lazy(() => import('./ChargeSheetCard'));

const settingsTabs = [
  { key: 'profiles', label: 'Risk profiles' },
  { key: 'roles', label: 'Account roles' },
  { key: 'actions', label: 'Action center' },
  { key: 'charge-sheet', label: 'Charge sheet' },
] as const;

type SettingsTab = (typeof settingsTabs)[number]['key'];

const parseTab = (value: string | null): SettingsTab =>
  settingsTabs.find((tab) => tab.key === value)?.key ?? 'profiles';

/** Settings shared across accounts: risk profiles, which account has which role and the Action center thresholds. in the URL. */
export function SettingsPage() {
  const [params, setParams] = useSearchParams();
  const tab = parseTab(params.get('tab'));

  return (
    <>
      <PageHeader
        title="Settings"
        icon={TuneIcon}
        hue="settings"
        description="Risk profiles shared by Trading accounts, which account is used for data, trading and portfolio, and when the Action center flags a holding. Risk and role changes ask for your password and apply from the next service run."
      />
      <Tabs
        value={tab}
        onChange={(_event, next: SettingsTab) => {
          setParams(next === 'profiles' ? {} : { tab: next }, { replace: true });
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
        {tab === 'profiles' && <RiskProfilesView />}
        {tab === 'roles' && <RoleMatrixView />}
        {tab === 'actions' && <ActionThresholdsCard />}
        {tab === 'charge-sheet' && (
          <Suspense fallback={<LoadingState label="Loading the confirmation" />}>
            <ChargeSheetCard />
          </Suspense>
        )}
      </Box>
    </>
  );
}
