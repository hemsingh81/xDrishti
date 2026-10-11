import { Button, Grid, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import type { RiskProfile } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useCreateRiskProfile, useUpdateRiskProfile } from '../api/riskProfileMutations';
import { fieldErrorsOf, problemMessage, type FieldErrors } from '../lib/fieldErrors';
import { focusFirstInvalid } from '../lib/focus';
import { weeklyLossActionLabels } from '../lib/labels';
import {
  riskFields,
  riskProfileFormFrom,
  toRiskProfileInput,
  validateRiskProfile,
  type RiskProfileForm,
} from '../lib/riskProfileForm';
import { FormAlert } from './FormAlert';

export type RiskProfileDialogMode =
  { kind: 'create'; startFrom?: RiskProfile } | { kind: 'edit'; profile: RiskProfile };

interface RiskProfileDialogProps {
  mode: RiskProfileDialogMode;
  /** For the "start from" picker when creating. */
  profiles: RiskProfile[];
  onClose: () => void;
}

/** Create or edit a risk profile. Saving asks for the password when the API requires it. */
export function RiskProfileDialog({ mode, profiles, onClose }: RiskProfileDialogProps) {
  const { notify } = useToast();
  const create = useCreateRiskProfile();
  const update = useUpdateRiskProfile();
  const save = mode.kind === 'edit' ? update : create;
  const bodyRef = useRef<HTMLDivElement>(null);
  const nameRef = useRef<HTMLInputElement>(null);
  const [form, setForm] = useState<RiskProfileForm>(() =>
    mode.kind === 'edit'
      ? riskProfileFormFrom(mode.profile, mode.profile.name)
      : riskProfileFormFrom(mode.startFrom, mode.startFrom ? `${mode.startFrom.name} copy` : ''),
  );
  const [startFrom, setStartFrom] = useState(mode.kind === 'create' ? (mode.startFrom?.id ?? '') : '');
  const [clientErrors, setClientErrors] = useState<FieldErrors>({});
  const errors = { ...fieldErrorsOf(save.error), ...clientErrors };

  function change(patch: Partial<RiskProfileForm>) {
    setForm((previous) => ({ ...previous, ...patch }));
    setClientErrors({});
    save.reset();
  }

  function startFromProfile(id: string) {
    const source = profiles.find((p) => p.id === id);
    setStartFrom(id);
    setForm((previous) => riskProfileFormFrom(source, previous.name || (source ? `${source.name} copy` : '')));
    setClientErrors({});
    save.reset();
  }

  function submit(event: SyntheticEvent) {
    event.preventDefault();
    const found = validateRiskProfile(form);
    setClientErrors(found);
    if (Object.keys(found).length > 0) {
      focusFirstInvalid(bodyRef.current);
      return;
    }
    const body = toRiskProfileInput(form);
    const options = {
      onSuccess: () => {
        notify(mode.kind === 'edit' ? 'Risk profile saved.' : 'Risk profile created.');
        onClose();
      },
      onError: () => {
        focusFirstInvalid(bodyRef.current);
      },
    };
    if (mode.kind === 'edit') {
      update.mutate({ id: mode.profile.id, body }, options);
    } else {
      create.mutate(body, options);
    }
  }

  return (
    <AppDialog open onClose={onClose} busy={save.isPending} maxWidth="md" initialFocusRef={nameRef} onSubmit={submit}>
      <AppDialogHeader>{mode.kind === 'edit' ? `Edit ${mode.profile.name}` : 'New risk profile'}</AppDialogHeader>
      <AppDialogBody ref={bodyRef}>
        <Stack spacing={2.5}>
          {mode.kind === 'create' && (
            <TextField
              select
              label="Start from"
              value={startFrom}
              onChange={(e) => {
                startFromProfile(e.target.value);
              }}
              helperText="Copy the values of an existing profile, then adjust them."
              slotProps={{ select: { native: true } }}
              sx={{ maxWidth: 420 }}
            >
              <option value="">Blank (sensible defaults)</option>
              {profiles.map((profile) => (
                <option key={profile.id} value={profile.id}>
                  {profile.name}
                </option>
              ))}
            </TextField>
          )}
          <TextField
            fullWidth
            required
            inputRef={nameRef}
            label="Name"
            value={form.name}
            onChange={(e) => {
              change({ name: e.target.value });
            }}
            error={Boolean(errors.name)}
            helperText={errors.name}
            slotProps={{ htmlInput: { maxLength: 60 } }}
          />
          <Grid container spacing={2}>
            {riskFields.map(({ key, label, step }) => (
              <Grid key={key} size={{ xs: 12, sm: 6, md: 4 }}>
                <TextField
                  fullWidth
                  required
                  type="number"
                  label={label}
                  value={form[key]}
                  onChange={(e) => {
                    change({ [key]: e.target.value });
                  }}
                  error={Boolean(errors[key])}
                  helperText={errors[key]}
                  slotProps={{ htmlInput: { step, min: 0, inputMode: 'decimal' } }}
                />
              </Grid>
            ))}
            <Grid size={{ xs: 12, sm: 6, md: 4 }}>
              <TextField
                select
                fullWidth
                label="When the weekly limit is hit"
                value={form.weeklyLossAction}
                onChange={(e) => {
                  change({ weeklyLossAction: e.target.value });
                }}
                error={Boolean(errors.weeklyLossAction)}
                helperText={errors.weeklyLossAction}
                slotProps={{ select: { native: true } }}
              >
                {Object.entries(weeklyLossActionLabels).map(([value, text]) => (
                  <option key={value} value={value}>
                    {text}
                  </option>
                ))}
              </TextField>
            </Grid>
          </Grid>
          <FormAlert message={problemMessage(save.error)} />
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={save.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" loading={save.isPending}>
          {mode.kind === 'edit' ? 'Save profile' : 'Create profile'}
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
