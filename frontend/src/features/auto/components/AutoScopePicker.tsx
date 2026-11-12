import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  FormControlLabel,
  FormGroup,
  FormHelperText,
  Stack,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Typeahead } from '@/shared/ui/Typeahead';
import { ErrorState, LoadingState } from '@/shared/ui';
import { autoBasketsQuery, autoInstrumentSearchQuery, autoScopeQuery, useSetScope } from '../api/autoApi';
import { autoProblem } from '../lib/problem';

/** What the system may suggest: baskets and stocks. With none set, every eligible instrument is considered. */
export function AutoScopePicker() {
  const scope = useQuery(autoScopeQuery);
  const baskets = useQuery(autoBasketsQuery);
  if (scope.isPending || baskets.isPending)
    return (
      <Box sx={{ minHeight: { xs: 327, md: 294 } }}>
        <LoadingState label="Loading scope…" />
      </Box>
    );
  if (scope.isError) return <ErrorState error={scope.error} />;
  if (baskets.isError) return <ErrorState error={baskets.error} />;
  // Keyed by version so a scope saved elsewhere resets the draft.
  return (
    <ScopeForm
      key={scope.data.version ?? 'none'}
      summary={scope.data.summary}
      savedBaskets={scope.data.basketIds}
      savedInstruments={scope.data.instrumentIds}
      baskets={baskets.data.filter((b) => b.archivedAt === null)}
    />
  );
}

interface FormProps {
  summary: string;
  savedBaskets: string[];
  savedInstruments: string[];
  baskets: { id: string; name: string }[];
}

function ScopeForm({ summary, savedBaskets, savedInstruments, baskets }: FormProps) {
  const save = useSetScope();
  const [basketIds, setBasketIds] = useState(savedBaskets);
  const [instrumentIds, setInstrumentIds] = useState(savedInstruments);
  const [symbols, setSymbols] = useState<Record<string, string>>({});
  const [text, setText] = useState('');
  const found = useQuery(autoInstrumentSearchQuery(text));
  const options = (found.data ?? []).map((row) => ({ value: row.id, label: row.symbol, hint: row.name }));
  const empty = basketIds.length === 0 && instrumentIds.length === 0;

  const toggle = (id: string) => {
    setBasketIds((now) => (now.includes(id) ? now.filter((b) => b !== id) : [...now, id]));
  };
  return (
    <Stack spacing={1.5} component="section" aria-labelledby="auto-scope">
      <Typography id="auto-scope" variant="h2">
        Scope
      </Typography>
      <Typography color="text.secondary">Now: {summary}.</Typography>
      <FormGroup aria-label="Baskets in scope" role="group">
        {baskets.map((basket) => (
          <FormControlLabel
            key={basket.id}
            label={basket.name}
            control={
              <Checkbox
                checked={basketIds.includes(basket.id)}
                onChange={() => {
                  toggle(basket.id);
                }}
              />
            }
          />
        ))}
      </FormGroup>
      <Typeahead
        label="Add a stock"
        placeholder="Search by symbol or name"
        options={options}
        value={null}
        onSearch={setText}
        onChange={(id) => {
          const option = options.find((o) => o.value === id);
          if (!id || !option) return;
          setSymbols((now) => ({ ...now, [id]: option.label }));
          setInstrumentIds((now) => (now.includes(id) ? now : [...now, id]));
        }}
        loading={found.isFetching}
        noOptionsText={text.trim() === '' ? 'Type a symbol or a name' : 'No stock matches'}
        minWidth={0}
      />
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }} aria-label="Stocks in scope" role="group">
        {instrumentIds.map((id) => (
          <Chip
            key={id}
            // shortcut: saved stocks show by id prefix after reload, upgrade when AutoScopeView returns symbols
            label={symbols[id] ?? `Stock ${id.slice(0, 8)}`}
            onDelete={() => {
              setInstrumentIds((now) => now.filter((i) => i !== id));
            }}
          />
        ))}
      </Stack>
      {empty && <FormHelperText>Choose at least one basket or stock to save a scope.</FormHelperText>}
      {save.isError && <Alert severity="error">{autoProblem(save.error)}</Alert>}
      {save.isSuccess && <Alert severity="success">Scope saved.</Alert>}
      <div>
        <Button
          variant="contained"
          disabled={empty}
          loading={save.isPending}
          onClick={() => {
            save.mutate({ basketIds, instrumentIds });
          }}
        >
          Save scope
        </Button>
      </div>
    </Stack>
  );
}
