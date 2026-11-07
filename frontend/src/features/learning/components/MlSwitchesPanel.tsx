import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Suspense, useState } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { rollbackQuery } from '../api/mlQueries';
import type { SwitchKey } from '../lib/mlText';
import { MlSwitchDialog } from './ActDialogs';
import { MlSwitchList } from './MlSwitchList';
import { Panel } from './Panel';

interface Turn {
  key: SwitchKey;
  on: boolean;
}

/** The three ML switches with their owner-act buttons. All are OFF until the owner turns one on; none places an order. */
export function MlSwitchesPanel() {
  const query = useQuery(rollbackQuery);
  const [turn, setTurn] = useState<Turn | null>(null);
  return (
    <Panel title="ML switches">
      {query.isPending && <LoadingState label="Loading the ML switches…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            All three are OFF by default. Turning influence or automatic promotion ON asks for your password again;
            automatic rollback and any change to OFF do not. A switch ON opens nothing alone, and no switch places an
            order.
          </Typography>
          <MlSwitchList
            settings={query.data.settings}
            onTurn={(key, on) => {
              setTurn({ key, on });
            }}
          />
        </>
      )}
      {turn && (
        <Suspense fallback={<LoadingState label="Loading the form…" />}>
          <MlSwitchDialog
            switchKey={turn.key}
            on={turn.on}
            onClose={() => {
              setTurn(null);
            }}
          />
        </Suspense>
      )}
    </Panel>
  );
}
