import { Alert, Box, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatWeekdayDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { autoBasketsQuery, autoSuggestionsQuery, basketSymbolsQuery, type AutoSuggestion } from '../api/autoApi';
import { AutoSuggestionCard } from './AutoSuggestionCard';

function Group({ title, items }: { title: string; items: AutoSuggestion[] }) {
  if (items.length === 0) return null;
  return (
    <Stack spacing={1} component="section" aria-label={title}>
      <Typography variant="h3" component="h3">{`${title} (${items.length})`}</Typography>
      {items.map((item) => (
        <AutoSuggestionCard key={item.itemId} item={item} />
      ))}
    </Stack>
  );
}

/** Next-day suggestions with reasons, filterable by basket or by stock. Read-only. */
export function AutoSuggestions() {
  const query = useQuery(autoSuggestionsQuery);
  const baskets = useQuery(autoBasketsQuery);
  const [basket, setBasket] = useState('');
  const [stock, setStock] = useState('');
  const members = useQuery({ ...basketSymbolsQuery(basket), enabled: basket !== '' });

  if (query.isPending)
    return (
      <Box sx={{ minHeight: { xs: 228, md: 164 } }}>
        <LoadingState label="Loading suggestions…" />
      </Box>
    );
  if (query.isError) return <ErrorState error={query.error} />;
  const data = query.data;
  if (!data) {
    return (
      <Typography color="text.secondary">
        No suggestions to show: there is no plan, or the last night failed closed.
      </Typography>
    );
  }
  const inBasket = basket === '' || !members.data ? null : new Set(members.data);
  const keep = (items: AutoSuggestion[]) =>
    items.filter(
      (i) =>
        (inBasket === null || inBasket.has(i.symbol)) && i.symbol.toLowerCase().includes(stock.trim().toLowerCase()),
    );
  const groups = {
    suggestions: keep(data.suggestions),
    notSuggested: keep(data.notSuggested),
    reserve: keep(data.reserve),
    rejected: keep(data.rejected),
  };
  const shown = Object.values(groups).reduce((n, g) => n + g.length, 0);
  return (
    <Stack spacing={1.5} component="section" aria-labelledby="auto-suggestions">
      <Typography id="auto-suggestions" variant="h2">
        Suggestions for {formatWeekdayDate(data.forDate)}
      </Typography>
      <Typography color="text.secondary">{data.notice}</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
        <TextField
          select
          size="small"
          label="Basket"
          value={basket}
          onChange={(e) => {
            setBasket(e.target.value);
          }}
          sx={{ minWidth: 200 }}
        >
          <MenuItem value="" sx={{ minHeight: 44 }}>
            All baskets
          </MenuItem>
          {(baskets.data ?? [])
            .filter((b) => b.archivedAt === null)
            .map((b) => (
              <MenuItem key={b.id} value={b.id} sx={{ minHeight: 44 }}>
                {b.name}
              </MenuItem>
            ))}
        </TextField>
        <TextField
          size="small"
          label="Stock"
          value={stock}
          onChange={(e) => {
            setStock(e.target.value);
          }}
        />
      </Stack>
      {members.isError && <Alert severity="error">The basket could not be read; showing everything.</Alert>}
      {shown === 0 && <Typography color="text.secondary">Nothing matches this filter.</Typography>}
      <Group title="Suggested" items={groups.suggestions} />
      <Group title="Not suggested" items={groups.notSuggested} />
      <Group title="Reserve" items={groups.reserve} />
      <Group title="Rejected" items={groups.rejected} />
    </Stack>
  );
}
