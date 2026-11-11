import { TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { useUniverseProposeAct } from '../api/movesApi';
import { ActForm } from './ActForm';

const runKey = /^[0-9a-f]{64}$/;

/**
 * Proposes one hypothesis from a stable liquidity-tier observation. The server refuses a link that is not an observation (422, with the reason), a second
 * open proposal of the same tier (409) and a full list of open hypotheses (409); each sentence is shown as sent.
 */
export function UniverseProposeMove({ cellKey }: { cellKey: string }) {
  const act = useUniverseProposeAct();
  const [baseline, setBaseline] = useState('');
  const valid = runKey.test(baseline);

  return (
    <>
      <Typography sx={{ overflowWrap: 'anywhere' }}>
        Tier cell: <strong>{cellKey}</strong>
      </Typography>
      <ActForm
        label="Propose this tier as a hypothesis"
        consequence="Writes one proposed hypothesis (scan scope, all shares to this tier). Nothing is applied, no scan scope changes and no experiment starts; a scope hypothesis cannot be started."
        submitLabel="Propose"
        fieldsReady={valid}
        run={(reason, password) => act({ key: cellKey, baselineRunKey: baseline, reason, password })}
        done={(outcome) => `Proposed: hypothesis ${String(outcome.id)} is ${outcome.state}. ${outcome.text}`}
      >
        <TextField
          label="Baseline run key"
          value={baseline}
          onChange={(event) => {
            setBaseline(event.target.value.trim());
          }}
          error={baseline !== '' && !valid}
          helperText="The stored run the cells were made from: 64 lowercase hex characters."
          required
        />
      </ActForm>
    </>
  );
}
