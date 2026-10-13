import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Typography } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import type { ServiceIssue } from '@/shared/api';
import { isError } from '../lib/issues';

/** The findings of the schedule checks about one service, under its schedule: an icon and a word, not colour alone. */
export function ServiceIssues({ issues }: { issues: readonly ServiceIssue[] }) {
  if (issues.length === 0) return null;
  return (
    <Box component="ul" aria-label="Schedule checks" sx={{ m: 0, mt: 0.5, p: 0, listStyle: 'none' }}>
      {issues.map((issue) => {
        const error = isError(issue);
        const Icon = error ? ErrorIcon : WarningIcon;
        return (
          <Typography
            key={`${issue.field}|${issue.code}|${issue.message}`}
            component="li"
            variant="caption"
            sx={{ display: 'flex', gap: 0.5, alignItems: 'flex-start', color: error ? 'error.main' : 'warning.main' }}
          >
            <Icon aria-hidden sx={{ fontSize: 15, mt: '1px', flex: 'none' }} />
            <span>
              <Box component="span" sx={visuallyHidden}>
                {error ? 'Error: ' : 'Warning: '}
              </Box>
              {issue.message}
            </span>
          </Typography>
        );
      })}
    </Box>
  );
}
