import { Alert, Button, Stack, Typography } from '@mui/material';
import { useMemo, useState, type SyntheticEvent } from 'react';
import type { ServiceIssue, ServiceView } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useToast } from '@/shared/ui/toastContext';
import { useResetSchedule, useSaveSchedule } from '../api/servicesMutations';
import { useSchedulePreview } from '../hooks/useSchedulePreview';
import { codeOf, issuesOf, saveErrorText } from '../lib/errors';
import { errorsByField, isError, listedIssues, mergeIssues, shownFields } from '../lib/issues';
import { formErrors, formOfService, inputOfForm, type ScheduleForm } from '../lib/scheduleForm';
import { ExecutionFields } from './ExecutionFields';
import { ParameterFields } from './ParameterFields';
import { PreviewPanel } from './PreviewPanel';
import { ScheduleFields } from './schedule/ScheduleFields';
import { EnabledSwitch } from './schedule/EnabledSwitch';

interface ConfigureDialogProps {
  /** The service as it was read when the dialog opened; its revision goes back with the save. */
  service: ServiceView;
  /** All services (the ones a run-after schedule can follow, and the names in the findings). */
  services: readonly ServiceView[];
  onClose: () => void;
  /** Read the services again and start over from what is stored now (after a revision conflict). */
  onReload: () => void;
}

/** Findings the server named when it refused a save or a restore; they stand while the draft is as it was. */
interface Refusal {
  /** The draft they are about; null for a restore (they are about the defaults). */
  draft: string | null;
  issues: ServiceIssue[];
}

/**
 * Configure a service: the schedule type the service allows and the fields of that type, enabled, retries, timeout,
 * catch-up, notify on failure and the parameters. As the owner types, the next three runs and the checks against all
 * the other services are asked for (debounced) and shown; Save is disabled while an error stands (warnings are listed
 * and allow saving). Restore default writes the definition's defaults. A stale revision says it was changed elsewhere.
 */
export function ConfigureDialog({ service, services, onClose, onReload }: ConfigureDialogProps) {
  const { notify } = useToast();
  const save = useSaveSchedule();
  const reset = useResetSchedule();
  const [form, setForm] = useState<ScheduleForm>(() => formOfService(service));
  const [refusal, setRefusal] = useState<Refusal | null>(null);

  const clientErrors = useMemo(() => formErrors(form, service), [form, service]);
  const incomplete = Object.keys(clientErrors).length > 0;
  const input = incomplete ? null : inputOfForm(form, service);
  const { preview, checking, failed } = useSchedulePreview(service.id, input);

  const draft = JSON.stringify(form);
  const preset = incomplete ? undefined : preview?.issues;
  const issues = useMemo(() => {
    const refused = refusal && (refusal.draft === null || refusal.draft === draft) ? refusal.issues : [];
    return mergeIssues(refused, preset ?? []);
  }, [refusal, draft, preset]);
  const shown = shownFields(form.type, service.parameters);
  const fieldErrors = { ...errorsByField(issues, service.id, shown), ...clientErrors };
  const standing = incomplete || issues.some(isError);
  const busy = save.isPending || reset.isPending;
  const stale = codeOf(save.error) === 'services.revision_changed';
  const failure = save.isError ? saveErrorText(save.error) : reset.isError ? saveErrorText(reset.error) : null;
  const followed = form.type === 'after' ? (services.find((s) => s.id === form.after)?.name ?? null) : null;

  const change = (patch: Partial<ScheduleForm>) => {
    setForm((current) => ({ ...current, ...patch }));
  };
  const submit = (event: SyntheticEvent) => {
    event.preventDefault();
    if (!input || standing || busy) return;
    save.mutate(
      { id: service.id, input },
      {
        onSuccess: () => {
          notify('Schedule saved — the service uses it from now on.');
          onClose();
        },
        onError: (error) => {
          const named = issuesOf(error, service.id);
          if (named.length > 0) setRefusal({ draft, issues: named });
        },
      },
    );
  };
  const restore = () => {
    reset.mutate(service.id, {
      onSuccess: () => {
        notify('Default schedule restored.');
        onClose();
      },
      onError: (error) => {
        const named = issuesOf(error, service.id);
        if (named.length > 0) setRefusal({ draft: null, issues: named });
      },
    });
  };

  return (
    <AppDialog open onClose={onClose} busy={busy} maxWidth="md" fullScreenOnMobile onSubmit={submit}>
      <AppDialogHeader
        subtitle={
          <>
            {service.description} · runs as <span className="mono">{service.runsAs}</span>
          </>
        }
      >
        Configure — {service.name}
      </AppDialogHeader>
      <AppDialogBody>
        <Stack sx={{ gap: 2.5, pt: 0.5 }}>
          {stale && (
            <Alert
              severity="warning"
              action={
                <Button color="inherit" size="small" onClick={onReload}>
                  Reload
                </Button>
              }
            >
              This service was changed elsewhere — reload to start from what is stored now.
            </Alert>
          )}
          {failure && <Alert severity="error">{failure}</Alert>}
          <EnabledSwitch
            enabled={form.enabled}
            onChange={(enabled) => {
              change({ enabled });
            }}
          />
          <ScheduleFields form={form} service={service} services={services} errors={fieldErrors} onChange={change} />
          <ExecutionFields form={form} errors={fieldErrors} onChange={change} />
          <ParameterFields
            parameters={service.parameters}
            form={form}
            errors={fieldErrors}
            onChange={(name, value) => {
              change({ parameters: { ...form.parameters, [name]: value } });
            }}
          />
          <PreviewPanel
            type={form.type}
            preview={preview}
            incomplete={incomplete}
            checking={checking}
            failed={failed}
            allClear={preview !== undefined && !incomplete && issues.length === 0}
            issues={listedIssues(issues, service.id, shown)}
            services={services}
            followed={followed}
          />
          <Typography variant="caption" color="text.secondary">
            A change takes effect at the next look of the scheduler (within a minute), without a restart, and is written
            to the audit log.
          </Typography>
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button onClick={restore} disabled={busy || service.isDefault}>
          Restore default
        </Button>
        <Button type="submit" variant="contained" disabled={busy || standing}>
          Save schedule
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
