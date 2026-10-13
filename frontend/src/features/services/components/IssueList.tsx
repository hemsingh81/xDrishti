import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import type { ServiceIssue, ServiceView } from '@/shared/api';
import { fieldName, isError } from '../lib/issues';

interface IssueListProps {
  issues: readonly ServiceIssue[];
  services: readonly ServiceView[];
  /** Nothing to list and the checks have run: say that there is no conflict. */
  allClear: boolean;
}

/** What the checks found against the draft, one line each with the service and field, an icon and a word. */
export function IssueList({ issues, services, allClear }: IssueListProps) {
  const byId = new Map(services.map((s) => [s.id, s]));
  if (issues.length === 0) {
    return allClear ? (
      <Typography variant="body2" sx={{ display: 'flex', gap: 0.5, alignItems: 'center', color: 'success.main' }}>
        <CheckCircleIcon aria-hidden fontSize="small" />
        No conflicts with other services.
      </Typography>
    ) : null;
  }
  return (
    <Box component="ul" aria-label="Schedule checks" sx={{ m: 0, p: 0, listStyle: 'none', display: 'grid', gap: 0.5 }}>
      {issues.map((issue) => {
        const error = isError(issue);
        const Icon = error ? ErrorIcon : WarningIcon;
        const owner = byId.get(issue.serviceId);
        return (
          <Typography
            key={`${issue.serviceId}|${issue.field}|${issue.code}|${issue.relatedServiceId ?? ''}|${issue.message}`}
            component="li"
            variant="body2"
            sx={{ display: 'flex', gap: 0.75, alignItems: 'flex-start', color: error ? 'error.main' : 'warning.main' }}
          >
            <Icon aria-hidden fontSize="small" sx={{ flex: 'none', mt: '1px' }} />
            <span>
              <Box component="span" sx={visuallyHidden}>
                {error ? 'Error: ' : 'Warning: '}
              </Box>
              <strong>{owner?.name ?? issue.serviceId}</strong> · {fieldName(issue.field, owner?.parameters)}:{' '}
              {issue.message}
            </span>
          </Typography>
        );
      })}
    </Box>
  );
}
