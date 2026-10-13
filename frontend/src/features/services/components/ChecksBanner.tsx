import { Alert, AlertTitle, Box } from '@mui/material';
import type { ServiceView } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { fieldName, isError, namedIssues } from '../lib/issues';

const plural = (count: number, one: string, many: string) => `${formatNumber(count)} ${count === 1 ? one : many}`;

/**
 * The result of the schedule checks over the whole set: all good, or the errors and warnings, each naming its service
 * and field. Said in words and with the alert's icon, not by colour alone.
 */
export function ChecksBanner({ services }: { services: readonly ServiceView[] }) {
  const issues = namedIssues(services);
  const errors = issues.filter((i) => isError(i.issue)).length;
  const warnings = issues.length - errors;
  const parameters = new Map(services.map((s) => [s.id, s.parameters]));

  if (issues.length === 0) {
    return (
      <Alert severity="success" role="status" sx={{ mb: 1.5, flexShrink: 0 }}>
        Schedule checks passed — dependencies and timing are consistent.
      </Alert>
    );
  }
  return (
    <Alert severity={errors > 0 ? 'error' : 'warning'} sx={{ mb: 1.5, flexShrink: 0 }}>
      <AlertTitle sx={{ mb: 0.5 }}>
        Schedule checks:{' '}
        {[errors > 0 && plural(errors, 'error', 'errors'), warnings > 0 && plural(warnings, 'warning', 'warnings')]
          .filter(Boolean)
          .join(', ')}
      </AlertTitle>
      <Box component="ul" aria-label="Schedule check findings" sx={{ m: 0, pl: 2.5 }}>
        {issues.map(({ issue, serviceName }) => (
          <li key={`${issue.serviceId}|${issue.field}|${issue.code}|${issue.relatedServiceId ?? ''}`}>
            <strong>{serviceName}</strong> · {fieldName(issue.field, parameters.get(issue.serviceId))} ·{' '}
            {isError(issue) ? 'Error' : 'Warning'}: {issue.message}
          </li>
        ))}
      </Box>
    </Alert>
  );
}
