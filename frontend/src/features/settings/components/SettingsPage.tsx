import { Box, Tab, Tabs } from '@mui/material';
import { useSearchParams } from 'react-router';
import { RiskProfilesView, RoleMatrixView } from '@/features/accounts';
import { PageHeader } from '@/shared/ui';
import { ToastProvider } from '@/shared/ui/ToastProvider';

const settingsTabs = [
  { key: 'profiles', label: 'Risk profiles' },
  { key: 'roles', label: 'Account roles' },
] as const;

type SettingsTab = (typeof settingsTabs)[number]['key'];

const parseTab = (value: string | null): SettingsTab =>
  settingsTabs.find((tab) => tab.key === value)?.key ?? 'profiles';

/** Settings shared across accounts: risk profiles and which account has which role. The tab lives in the URL. */
export function SettingsPage() {
  const [params, setParams] = useSearchParams();
  const tab = parseTab(params.get('tab'));

  return (
    <ToastProvider>
      <PageHeader
        title="Settings"
        description="Risk profiles shared by Trading accounts, and which account is used for data, trading and portfolio. Changes ask for your password and apply from the next service run."
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
      </Box>
    </ToastProvider>
  );
}
