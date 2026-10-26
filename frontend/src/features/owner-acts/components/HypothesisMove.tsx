import { FormControlLabel, Radio, RadioGroup, Typography } from '@mui/material';
import { useState } from 'react';
import { useHypothesisAct, type HypothesisRecord } from '../api/movesApi';
import { ActForm } from './ActForm';

/** The four moves this screen can make; running and judged have their own commands and are never offered. */
const movable: Record<string, { label: string; consequence: string }> = {
  approved: {
    label: 'Approve',
    consequence: 'Nothing is applied. Approving only lets the hypothesis be started with the command line.',
  },
  rejected: {
    label: 'Reject',
    consequence: 'Nothing is applied. Rejected is final; the hypothesis and its history are kept.',
  },
  expired: {
    label: 'Expire',
    consequence: 'Nothing is applied. Expired is final; the hypothesis and its history are kept.',
  },
  accepted: {
    label: 'Accept',
    consequence:
      'Nothing is applied. Accepting records the verdict of a Better experiment; applying a change is your own separate act, recorded as a changelog row.',
  },
};

/** The moves of one hypothesis that the server lists as legal now and this screen may make. */
export function HypothesisMove({ hypothesis }: { hypothesis: HypothesisRecord }) {
  const act = useHypothesisAct();
  const options = hypothesis.nextStates.filter((state) => movable[state] !== undefined);
  const [to, setTo] = useState('');
  const chosen = movable[to];

  return (
    <>
      <Typography sx={{ overflowWrap: 'anywhere' }}>{hypothesis.rationale}</Typography>
      <Typography>
        State: <strong>{hypothesis.state}</strong>. Knob: {hypothesis.knobName} ({hypothesis.knobKind}),{' '}
        {hypothesis.baselineValue} to {hypothesis.candidateValue}.{' '}
        {options.length === 0 ? 'This screen can make no move from this state.' : 'Pick the move to make.'}
      </Typography>
      {options.length > 0 && (
        <ActForm
          label="Move this hypothesis"
          consequence={chosen?.consequence ?? 'Pick a move first.'}
          submitLabel={chosen?.label ?? 'Move'}
          fieldsReady={chosen !== undefined}
          run={(reason, password) => act({ id: hypothesis.id, to, reason, password })}
          done={(outcome) => outcome.text}
        >
          <RadioGroup
            aria-label="Move"
            value={to}
            onChange={(event) => {
              setTo(event.target.value);
            }}
          >
            {options.map((state) => (
              <FormControlLabel key={state} value={state} control={<Radio />} label={movable[state]?.label ?? state} />
            ))}
          </RadioGroup>
        </ActForm>
      )}
    </>
  );
}
