import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue, useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { modelsQuery } from '../api/learningQueries';
import { modelRoles, parseId } from '../lib/roleText';
import { FilterBar } from './FilterBar';
import { ModelDrawer } from './ModelDrawer';
import { ModelRow } from './ModelRow';
import { Panel } from './Panel';

/** Models: the registered models (newest 50) with the role filter and the open model in the address. */
export function ModelsTab() {
  const url = useUrlState();
  const role = url.choice('role', modelRoles, 'all');
  const open = parseId(url.text('model'));
  const query = useQuery(modelsQuery(role));
  return (
    <Panel
      title="Models"
      actions={
        <FilterBar
          label="Model role"
          values={modelRoles}
          value={role}
          onChange={(next) => {
            url.update({ role: next === 'all' ? null : next });
          }}
        />
      }
    >
      {query.isPending && <LoadingState label="Loading the models…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data?.length === 0 && (
        <Box sx={{ display: 'grid', gap: 1 }}>
          <NonValue
            state={{
              kind: 'empty',
              reason: role === 'all' ? 'No model stored yet; run `xd learn model`.' : `No ${role} model.`,
            }}
          />
          <Typography variant="body2" color="text.secondary">
            On about 2.1 years of history most checks will be NOT EVALUABLE.
          </Typography>
        </Box>
      )}
      {query.data && query.data.length > 0 && (
        <Box
          component="ul"
          aria-label="Models, newest first"
          sx={{ m: 0, p: 0, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1.5 }}
        >
          {query.data.map((model) => (
            <ModelRow
              key={model.id}
              model={model}
              onOpen={() => {
                url.update({ model: String(model.id) });
              }}
            />
          ))}
        </Box>
      )}
      {open !== null && (
        <ModelDrawer
          id={open}
          onClose={() => {
            url.update({ model: null });
          }}
        />
      )}
    </Panel>
  );
}
