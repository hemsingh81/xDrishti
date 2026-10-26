import { FormControlLabel, MenuItem, Radio, RadioGroup, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { useLessonAct, type LessonRecord } from '../api/movesApi';
import { ActForm } from './ActForm';

type Verb = 'link' | 'close' | 'dismiss';

/** The server's state words and the move each one is. A state the server does not list is not drawn. */
const verbOf: Record<string, { verb: Verb; label: string }> = {
  linked: { verb: 'link', label: 'Link a fix' },
  closed: { verb: 'close', label: 'Close with the stored result' },
  dismissed: { verb: 'dismiss', label: 'Dismiss' },
};

const consequences: Record<Verb, string> = {
  link: 'Nothing is applied. Linking only records which hypothesis or experiment is meant to fix this lesson.',
  close:
    'Nothing is applied. You type no number: the result is read from the fix’s stored experiment (improved only when it was Better) and shown as an ordinary result. Closed is final.',
  dismiss: 'Nothing is applied. The lesson is kept and set aside. Dismissed is final.',
};

/** The moves of one lesson that the server lists as legal now. */
export function LessonMove({ lesson }: { lesson: LessonRecord }) {
  const act = useLessonAct();
  const options = lesson.nextStates.flatMap((state) => (verbOf[state] ? [{ state, ...verbOf[state] }] : []));
  const [verb, setVerb] = useState<Verb | ''>('');
  const [fixKind, setFixKind] = useState<'hypothesis' | 'experiment'>('hypothesis');
  const [fixId, setFixId] = useState('');
  const chosen = options.find((o) => o.verb === verb);
  const fixNumber = Number(fixId);

  return (
    <>
      <Typography sx={{ overflowWrap: 'anywhere' }}>{lesson.problem}</Typography>
      <Typography>
        State: <strong>{lesson.state}</strong>.{' '}
        {options.length === 0 ? 'The lifecycle allows no move from this state.' : 'Pick the move to make.'}
      </Typography>
      {options.length > 0 && (
        <ActForm
          label="Move this lesson"
          consequence={chosen ? consequences[chosen.verb] : 'Pick a move first.'}
          submitLabel={chosen?.label ?? 'Move'}
          fieldsReady={
            chosen !== undefined && (chosen.verb !== 'link' || (Number.isInteger(fixNumber) && fixNumber >= 1))
          }
          run={(reason, password) => {
            // fieldsReady already holds the button back; never fall back to a final move.
            if (!chosen) return Promise.reject(new Error('No move chosen.'));
            return act({
              id: lesson.id,
              verb: chosen.verb,
              reason,
              password,
              ...(chosen.verb === 'link' ? { fixKind, fixId: fixNumber } : {}),
            });
          }}
          done={(outcome) => outcome.text}
        >
          <RadioGroup
            aria-label="Move"
            value={verb}
            onChange={(event) => {
              setVerb(event.target.value as Verb);
            }}
          >
            {options.map((o) => (
              <FormControlLabel key={o.state} value={o.verb} control={<Radio />} label={o.label} />
            ))}
          </RadioGroup>
          {chosen?.verb === 'link' && (
            <>
              <TextField
                select
                label="Kind of fix"
                value={fixKind}
                onChange={(event) => {
                  setFixKind(event.target.value as 'hypothesis' | 'experiment');
                }}
              >
                <MenuItem value="hypothesis">Hypothesis</MenuItem>
                <MenuItem value="experiment">Experiment</MenuItem>
              </TextField>
              <TextField
                label="Number of the fix"
                type="number"
                value={fixId}
                onChange={(event) => {
                  setFixId(event.target.value);
                }}
                required
                slotProps={{ htmlInput: { min: 1, step: 1 } }}
              />
            </>
          )}
        </ActForm>
      )}
    </>
  );
}
