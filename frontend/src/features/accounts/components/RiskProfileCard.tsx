import { Box, Button, Card, CardActions, CardContent, Chip, Stack, Tooltip, Typography } from '@mui/material';
import type { RiskProfile } from '@/shared/api';
import { deleteBlockReason, riskFields } from '../lib/riskProfileForm';
import { weeklyLossActionLabels } from '../lib/labels';

interface RiskProfileCardProps {
  profile: RiskProfile;
  onEdit: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
}

/** One risk profile with every limit, how many accounts use it, and its actions. */
export function RiskProfileCard({ profile, onEdit, onDuplicate, onDelete }: RiskProfileCardProps) {
  const blocked = deleteBlockReason(profile);
  const unit = (value: number, u: string) => (u === '%' ? `${value}%` : u === 'R' ? `${value}R` : String(value));
  return (
    <Card
      component="article"
      aria-label={`Risk profile ${profile.name}`}
      sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}
    >
      <CardContent sx={{ flex: 1 }}>
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap', mb: 0.5 }}>
          <Typography variant="h3" component="h3">
            {profile.name}
          </Typography>
          {profile.isSeeded && <Chip size="small" variant="outlined" label="Built-in" />}
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          {profile.accountCount === 0
            ? 'Not used by any account'
            : `Used by ${profile.accountCount} account${profile.accountCount === 1 ? '' : 's'}`}
        </Typography>
        <Box
          component="dl"
          sx={{
            m: 0,
            display: 'grid',
            gridTemplateColumns: 'minmax(0, 1fr) auto',
            columnGap: 2,
            rowGap: 0.5,
            '& dt': { color: 'text.secondary' },
            '& dd': { m: 0, textAlign: 'right', fontVariantNumeric: 'tabular-nums' },
          }}
        >
          {riskFields.map(({ key, label, unit: u }) => (
            <Box key={key} sx={{ display: 'contents' }}>
              <dt>{label.replace(/ \((%|R)\)$/, '')}</dt>
              <dd>{unit(profile[key], u)}</dd>
            </Box>
          ))}
          <Box sx={{ display: 'contents' }}>
            <dt>When the weekly limit is hit</dt>
            <dd>{weeklyLossActionLabels[profile.weeklyLossAction] ?? profile.weeklyLossAction}</dd>
          </Box>
        </Box>
      </CardContent>
      <CardActions sx={{ flexWrap: 'wrap', px: 2, pb: 2 }}>
        <Button size="small" onClick={onEdit} aria-label={`Edit ${profile.name}`}>
          Edit
        </Button>
        <Button size="small" onClick={onDuplicate} aria-label={`Start a new profile from ${profile.name}`}>
          Start new from this
        </Button>
        <Tooltip title={blocked ?? ''}>
          <span>
            <Button
              size="small"
              color="error"
              disabled={Boolean(blocked)}
              onClick={onDelete}
              aria-label={`Delete ${profile.name}`}
            >
              Delete
            </Button>
          </span>
        </Tooltip>
        {blocked && (
          <Typography variant="caption" color="text.secondary" sx={{ flexBasis: '100%' }}>
            {blocked}
          </Typography>
        )}
      </CardActions>
    </Card>
  );
}
