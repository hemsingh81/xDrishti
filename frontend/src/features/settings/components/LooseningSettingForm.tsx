import { Checkbox, FormControlLabel } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { components } from '@/shared/api/schema';
import { looseningSettingsQuery } from '@/shared/api/looseningRead';
import { changeLooseningSetting } from '../api/limitsApi';
import { LimitForm } from './LimitForm';

type View = components['schemas']['LooseningSettingsView'];

/** The automatic dismissal of the plan banner (BK-10b): OFF by default; ON hides a loosened limit after one plan has shown it, recorded as automatic. The owner's act, with the password every time. */
export function LooseningSettingForm({ view }: { view: View }) {
  const queryClient = useQueryClient();
  const [auto, setAuto] = useState(view.autoDismiss);

  return (
    <LimitForm
      label="Change the automatic dismissal"
      submitLabel="Change the automatic dismissal"
      consequence="When on, a loosened limit hides from the plan banner after one plan has shown it; the dismissal is recorded as automatic, never as you. It changes no limit and places no order."
      fieldsReady={auto !== view.autoDismiss}
      run={async (password) => {
        queryClient.setQueryData(
          looseningSettingsQuery.queryKey,
          await changeLooseningSetting(auto, view.version, password),
        );
        return false;
      }}
    >
      <FormControlLabel
        control={
          <Checkbox
            checked={auto}
            onChange={(event) => {
              setAuto(event.target.checked);
            }}
          />
        }
        label="Hide a loosening automatically after one plan"
      />
    </LimitForm>
  );
}
