import { Chip, MenuItem, Stack, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material';
import { SearchField } from '@/shared/ui/SearchField';
import type { AuditEntry, AuditKinds } from '../api/auditQueries';
import { listOf, presetLabels, presets, toggled, words, type AuditFilter, type Preset } from '../lib/auditParams';

interface Props {
  filter: AuditFilter;
  kinds: AuditKinds | undefined;
  /** Rows loaded, so the account list offers the accounts seen. */
  seen: readonly AuditEntry[];
  onChange: (patch: Readonly<Record<string, string | null>>) => void;
}

const sourceCodes = ['auth', 'account', 'approval', 'plan', 'autonomy', 'learning'] as const;

function Pick({
  label,
  value,
  options,
  onPick,
}: {
  label: string;
  value: string;
  options: readonly { value: string; label: string }[];
  onPick: (value: string | null) => void;
}) {
  return (
    <TextField
      select
      size="small"
      label={label}
      value={value}
      onChange={(event) => {
        onPick(event.target.value === '' ? null : event.target.value);
      }}
      sx={{ minWidth: 150 }}
    >
      <MenuItem value="">Any</MenuItem>
      {options.map((o) => (
        <MenuItem key={o.value} value={o.value}>
          {o.label}
        </MenuItem>
      ))}
    </TextField>
  );
}

/** The filter controls: range presets, source chips, category, outcome, actor, account, kind and a text search. Every change goes to the URL. */
export function AuditFilterControls({ filter, kinds, seen, onChange }: Props) {
  const sources = listOf(filter.source);
  const accounts = new Map<string, string>();
  for (const e of seen) if (e.accountId) accounts.set(e.accountId, e.accountName ?? e.accountId);
  if (filter.account && !accounts.has(filter.account)) accounts.set(filter.account, filter.account);
  const kindOptions = (kinds?.kinds ?? [])
    .filter(
      (k) =>
        (sources.length === 0 || sources.includes(k.source)) && (!filter.category || k.category === filter.category),
    )
    .map((k) => ({ value: k.kind, label: k.label }));
  const mark = (name: string) => (value: string | null) => {
    onChange({ [name]: value });
  };

  return (
    <Stack sx={{ gap: 1.5 }}>
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup
          size="small"
          exclusive
          aria-label="Range"
          value={filter.preset}
          onChange={(_event, next: Preset | null) => {
            if (next)
              onChange({ range: next === '30d' ? null : next, ...(next === 'custom' ? {} : { from: null, to: null }) });
          }}
        >
          {presets.map((p) => (
            <ToggleButton key={p} value={p}>
              {presetLabels[p]}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
        {filter.preset === 'custom' && (
          <>
            <TextField
              size="small"
              type="date"
              label="From"
              value={filter.from ?? ''}
              onChange={(e) => {
                onChange({ from: e.target.value || null });
              }}
              slotProps={{ inputLabel: { shrink: true } }}
            />
            <TextField
              size="small"
              type="date"
              label="To"
              value={filter.to ?? ''}
              onChange={(e) => {
                onChange({ to: e.target.value || null });
              }}
              slotProps={{ inputLabel: { shrink: true } }}
            />
          </>
        )}
        <SearchField
          label="Search the summaries"
          placeholder="Search text (3 to 64 characters)"
          value={filter.q ?? ''}
          onCommit={(text) => {
            onChange({ q: text.trim().length >= 3 ? text.trim() : null });
          }}
        />
      </Stack>
      <Stack direction="row" role="group" aria-label="Record" sx={{ gap: 0.75, flexWrap: 'wrap' }}>
        {sourceCodes.map((s) => (
          <Chip
            key={s}
            label={words(s)}
            clickable
            aria-pressed={sources.includes(s)}
            variant={sources.includes(s) ? 'filled' : 'outlined'}
            color={sources.includes(s) ? 'primary' : 'default'}
            onClick={() => {
              onChange({ source: toggled(filter.source, s) });
            }}
          />
        ))}
      </Stack>
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap' }}>
        <Pick
          label="Category"
          value={filter.category ?? ''}
          options={(kinds?.categories ?? []).map((c) => ({ value: c, label: words(c) }))}
          onPick={mark('category')}
        />
        <Pick
          label="Outcome"
          value={filter.outcome ?? ''}
          options={(kinds?.outcomes ?? []).map((o) => ({ value: o, label: o === 'ok' ? 'Recorded' : words(o) }))}
          onPick={mark('outcome')}
        />
        <Pick
          label="Actor"
          value={filter.actor ?? ''}
          options={(kinds?.actors ?? []).map((a) => ({ value: a, label: words(a) }))}
          onPick={mark('actor')}
        />
        <Pick
          label="Account"
          value={filter.account ?? ''}
          options={[...accounts].map(([value, label]) => ({ value, label }))}
          onPick={mark('account')}
        />
        <Pick label="Kind" value={filter.kind ?? ''} options={kindOptions} onPick={mark('kind')} />
      </Stack>
    </Stack>
  );
}
