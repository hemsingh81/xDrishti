import { Alert, Button, Stack, TextField } from '@mui/material';
import { useState, type KeyboardEvent } from 'react';
import { useToast } from '@/shared/ui/toastContext';
import { useAddBucket } from '../api/accountMutations';
import { fieldErrorsOf, problemMessage } from '../lib/fieldErrors';

/** Adds a portfolio bucket. Not a <form>, so it can sit inside another form (the wizard). */
export function BucketAdder({ onAdded }: { onAdded?: (name: string) => void }) {
  const { notify } = useToast();
  const add = useAddBucket();
  const [name, setName] = useState('');
  const fieldError = fieldErrorsOf(add.error).name ?? problemMessage(add.error);

  function submit() {
    const trimmed = name.trim();
    if (!trimmed || add.isPending) {
      return;
    }
    add.mutate(trimmed, {
      onSuccess: (bucket) => {
        setName('');
        notify(`Bucket “${bucket.name}” added.`);
        onAdded?.(bucket.name);
      },
    });
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === 'Enter') {
      event.preventDefault();
      submit();
    }
  }

  return (
    <Stack spacing={1}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'flex-start' } }}>
        <TextField
          size="small"
          label="New bucket name"
          value={name}
          onChange={(e) => {
            setName(e.target.value);
            add.reset();
          }}
          onKeyDown={onKeyDown}
          error={Boolean(fieldError)}
          slotProps={{ htmlInput: { maxLength: 40 } }}
        />
        <Button variant="outlined" onClick={submit} loading={add.isPending} disabled={!name.trim()}>
          Add bucket
        </Button>
      </Stack>
      {fieldError && <Alert severity="error">{fieldError}</Alert>}
    </Stack>
  );
}
