import TuneIcon from '@mui/icons-material/TuneOutlined';
import { Button } from '@mui/material';
import { lazy } from 'react';
import { useSearchParams } from 'react-router';
import { LazyBoundary, LoadingState, PageHeader, ReloadAlert } from '@/shared/ui';
import { EssentialSettings } from './EssentialSettings';

// Everything beyond the five choices loads on demand.
const AdvancedSettings = lazy(() => import('./AdvancedSettings').then((m) => ({ default: m.AdvancedSettings })));

/** Five plain choices first; the existing tabs sit one step away under `Advanced settings`. The open state lives in the URL (`?tab=`). */
export function SettingsPage() {
  const [params, setParams] = useSearchParams();
  const advanced = params.has('tab');

  return (
    <>
      <PageHeader
        title="Settings"
        icon={TuneIcon}
        hue="settings"
        description="The few choices that matter. Anything that loosens a safety asks for your password."
      />
      <EssentialSettings />
      <Button
        variant="outlined"
        aria-expanded={advanced}
        aria-controls={advanced ? 'advanced-settings' : undefined}
        onClick={() => {
          setParams(advanced ? {} : { tab: 'profiles' }, { replace: true });
        }}
        sx={{ mb: 2.5 }}
      >
        {advanced ? 'Hide advanced settings' : 'Advanced settings'}
      </Button>
      {advanced && (
        <section id="advanced-settings" aria-label="Advanced settings">
          <LazyBoundary
            pending={<LoadingState label="Loading advanced settings" />}
            failed={<ReloadAlert label="Advanced settings" />}
          >
            <AdvancedSettings />
          </LazyBoundary>
        </section>
      )}
    </>
  );
}
