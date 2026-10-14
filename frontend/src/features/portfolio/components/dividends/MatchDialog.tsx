import { Button, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import { useRef, useState } from 'react';
import type { DividendCandidate } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import type { TypeaheadOption } from '@/shared/ui/Typeahead';
import { optionOfTarget, targetOfOption, type MatchDraft } from '../../lib/dividendImport';
import { MatchTargetField } from './MatchTargetField';

interface MatchDialogProps {
  accountId: string;
  /** The server's key of the name: the choice is kept (and sent) under it. */
  nameKey: string;
  /** The name as the file wrote it (plain text; several spellings are listed by the caller). */
  label: string;
  /** The name matched a remembered match (alias): choosing again with "remember" ticked retargets it. */
  remembered?: boolean;
  candidates: readonly DividendCandidate[];
  /** The choice made already for the name, if any. */
  draft: MatchDraft | undefined;
  onSave: (draft: MatchDraft) => void;
  /** Takes the choice back: the name returns to what the preview found. */
  onRemove: (key: string) => void;
  onClose: () => void;
}

/**
 * Chooses where the dividends of one name go: a holding of the account, an instrument of the list, or nowhere (unallocated).
 * The choice applies to every row with that name; "remember" (ticked by default) saves it for the account, so the next file
 * with this name needs no question.
 */
export function MatchDialog({
  accountId,
  nameKey,
  label,
  remembered = false,
  candidates,
  draft,
  onSave,
  onRemove,
  onClose,
}: MatchDialogProps) {
  const [option, setOption] = useState<TypeaheadOption | null>(draft ? optionOfTarget(draft.target) : null);
  const [remember, setRemember] = useState(draft?.remember ?? true);
  const fieldRef = useRef<HTMLInputElement>(null);
  return (
    <AppDialog open onClose={onClose} maxWidth="sm" initialFocusRef={fieldRef}>
      <AppDialogHeader subtitle={label}>Choose where these dividends go</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography color="text.secondary">
            {candidates.length > 1
              ? `More than one holding fits this name (${candidates.map((c) => c.symbol).join(', ')}), so nothing is guessed. Choose one.`
              : 'Choose the holding the dividends of this name belong to. This applies to every row with this name.'}
          </Typography>
          {remembered && (
            <Typography color="text.secondary">
              Remembered match: choose another holding and keep “Remember this match” ticked to retarget it for later
              files.
            </Typography>
          )}
          <MatchTargetField
            accountId={accountId}
            candidates={candidates}
            value={option}
            onChange={setOption}
            inputRef={fieldRef}
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
              onRemove(nameKey);
              onClose();
            }}
          >
            Remove my choice
          </Button>
        )}
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={option === null}
          onClick={() => {
            if (!option) return;
            onSave({ key: nameKey, target: targetOfOption(option), remember });
            onClose();
          }}
        >
          Use this match
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
