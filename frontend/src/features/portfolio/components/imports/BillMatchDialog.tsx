import { Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import type { TypeaheadOption } from '@/shared/ui/Typeahead';
import { targetOfOption } from '../../lib/dividendImport';
import { isIsin, type BillDraft, type BillTarget, type NameGroup } from '../../lib/reportImport';
import { MatchTargetField } from '../dividends/MatchTargetField';

interface BillMatchDialogProps {
  accountId: string;
  group: NameGroup;
  /** The choice made already for the name, if any. */
  draft: BillDraft | undefined;
  onSave: (draft: BillDraft) => void;
  /** Takes the choice back: the name returns to what the preview found. */
  onRemove: (key: string) => void;
  onClose: () => void;
}

const optionOf = (target: BillTarget | undefined): TypeaheadOption | null =>
  target && target.kind !== 'isin'
    ? { value: `${target.kind === 'holding' ? 'h' : 'i'}:${target.id}`, label: target.label }
    : null;

function targetFrom(option: TypeaheadOption | null, isin: string): BillTarget | null {
  if (isin.trim() !== '') return isIsin(isin) ? { kind: 'isin', isin: isin.trim().toUpperCase() } : null;
  const target = option ? targetOfOption(option) : null;
  return target && target.kind !== 'none' ? target : null;
}

/**
 * Chooses what a scrip name of a transaction report means: one of the account's holdings, a share or ETF of the instrument
 * list, or an ISIN typed in. The choice applies to every row with that name in every file; "remember" (ticked by default)
 * saves it for later files.
 */
export function BillMatchDialog({ accountId, group, draft, onSave, onRemove, onClose }: BillMatchDialogProps) {
  const [option, setOption] = useState<TypeaheadOption | null>(optionOf(draft?.target));
  const [isin, setIsin] = useState(draft?.target.kind === 'isin' ? draft.target.isin : '');
  const [remember, setRemember] = useState(draft?.remember ?? true);
  const fieldRef = useRef<HTMLInputElement>(null);
  const target = targetFrom(option, isin);
  const isinProblem = isin.trim() !== '' && !isIsin(isin);
  return (
    <AppDialog open onClose={onClose} maxWidth="sm" initialFocusRef={fieldRef}>
      <AppDialogHeader subtitle={group.spellings.join(', ')}>Choose what this name means</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography color="text.secondary">
            {group.candidates.length > 1
              ? `More than one security fits this name (${group.candidates.map((c) => c.symbol ?? c.name).join(', ')}), so nothing is guessed. Choose one.`
              : 'Choose the holding or share this name stands for, or type its ISIN. This applies to every row with this name, in every file.'}
          </Typography>
          <MatchTargetField
            accountId={accountId}
            candidates={group.candidates.flatMap((c) =>
              c.holdingId === null ? [] : [{ holdingId: c.holdingId, symbol: c.symbol ?? c.name, name: c.name }],
            )}
            value={option}
            onChange={(next) => {
              setOption(next);
              if (next) setIsin('');
            }}
            inputRef={fieldRef}
            allowNone={false}
          />
          <TextField
            label="Or type the ISIN"
            value={isin}
            error={isinProblem}
            helperText={isinProblem ? 'An ISIN has 12 characters, such as INE000A01010.' : undefined}
            onChange={(event) => {
              setIsin(event.target.value);
              if (event.target.value.trim() !== '') setOption(null);
            }}
            slotProps={{ htmlInput: { autoCapitalize: 'characters', spellCheck: false, maxLength: 12 } }}
          />
          <FormControlLabel
            control={
              <Checkbox
                checked={remember}
                onChange={(event) => {
                  setRemember(event.target.checked);
                }}
              />
            }
            label="Remember this match for later files"
          />
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        {draft && (
          <Button
            color="inherit"
            onClick={() => {
              onRemove(group.key);
              onClose();
            }}
          >
            Remove my choice
          </Button>
        )}
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={target === null}
          onClick={() => {
            if (!target) return;
            onSave({ key: group.key, target, remember });
            onClose();
          }}
        >
          Use this match
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
