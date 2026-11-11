import { Box, Button, Card, CardContent, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { Link as RouterLink } from 'react-router';
import { formatDate } from '@/shared/lib/format';
import type { BasketProfile } from '../api/profileQueries';

const windowSources = {
  query: 'the dates you chose',
  default: 'the last sessions ending at the newest stored daily bar of the index',
} as const;

interface ProfileHeaderProps {
  profile: BasketProfile;
  from: string;
  to: string;
  onWindow: (patch: { from?: string | null; to?: string | null }) => void;
}

/**
 * The header of the screen: what the basket is, the window and the index, the membership basis (in words, here and not in a
 * tooltip: survivorship changes what every figure means), and how many instruments the figures rest on, with the ones left out named.
 */
export function ProfileHeader({ profile, from, to, onWindow }: ProfileHeaderProps) {
  const [open, setOpen] = useState(false);
  const { members, window: range, index } = profile;
  const dated = profile.membershipBasis === 'dated';
  const basisLine = dated
    ? `Membership is dated${profile.membershipFrom ? ` from ${formatDate(profile.membershipFrom)}` : ''}; before that date the first known members apply.`
    : `Members as of ${formatDate(profile.membersAsOf)}, not as of each trading day; survivorship applies (ISS-22).`;
  const source = (windowSources as Record<string, string>)[range.source] ?? range.source;
  return (
    <Card component="header" aria-label="Basket profile header">
      <CardContent sx={{ display: 'grid', gap: 1.25 }}>
        <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap', alignItems: 'baseline' }}>
          <Typography variant="h1" sx={{ fontSize: { xs: '1.2rem', md: '1.5rem' } }}>
            {profile.basket.name}
          </Typography>
          <Typography variant="body2" color="text.secondary">{`${profile.basket.kind} basket`}</Typography>
          <Button component={RouterLink} to="/baskets/ranking" size="small" sx={{ minHeight: 44 }}>
            Compare baskets
          </Button>
        </Stack>
        <Typography data-testid="membership-basis" sx={{ fontWeight: 600 }}>
          {basisLine}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {`Window ${formatDate(range.from)} to ${formatDate(range.to)} (${source}). Index: ${index.name ?? index.symbol ?? 'none is set'}${index.symbol ? (index.isDefault ? ' (default)' : ' (chosen)') : ''}.`}
        </Typography>
        <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5, alignItems: { sm: 'center' } }}>
          <TextField
            label="From"
            type="date"
            size="small"
            value={from}
            onChange={(e) => {
              onWindow({ from: e.target.value || null });
            }}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { style: { minHeight: 28 } } }}
          />
          <TextField
            label="To"
            type="date"
            size="small"
            value={to}
            onChange={(e) => {
              onWindow({ to: e.target.value || null });
            }}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { style: { minHeight: 28 } } }}
          />
          <Button
            size="small"
            onClick={() => {
              onWindow({ from: null, to: null });
            }}
            sx={{ minHeight: 44 }}
          >
            Use the default window
          </Button>
        </Stack>
        <Box>
          <Typography component="span" sx={{ mr: 1 }}>
            {`${String(members.counted)} of ${String(members.total)} instruments counted`}
          </Typography>
          {members.excluded.length > 0 && (
            <Button
              size="small"
              aria-expanded={open}
              aria-controls="excluded-list"
              onClick={() => {
                setOpen((v) => !v);
              }}
              sx={{ minHeight: 44 }}
            >
              {open ? 'Hide the ones left out' : 'Show the ones left out'}
            </Button>
          )}
          {open && (
            <Box component="ul" id="excluded-list" aria-label="Instruments left out" sx={{ m: 0, pl: 2.5 }}>
              {members.excluded.map((e) => (
                <li key={`${e.symbol}-${e.reason}`}>{`${e.symbol}: ${e.reason.replaceAll('_', ' ')}`}</li>
              ))}
            </Box>
          )}
        </Box>
      </CardContent>
    </Card>
  );
}
