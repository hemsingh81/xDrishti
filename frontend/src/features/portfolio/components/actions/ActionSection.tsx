import { Box, Button, Divider, Stack, Typography } from '@mui/material';
import { useId, useState } from 'react';
import { formatNumber } from '@/shared/lib/format';
import { IconChip } from '@/shared/ui';
import { severityLabels, type SeverityGroup } from '../../lib/actions';
import type { ActionListProps } from './ActionList';
import { ActionRow } from './ActionRow';
import { severityLook } from './severityLook';

/** Rows drawn per severity at first, and with each "Show more": a first-time owner may have hundreds of items. */
export const rowStep = 50;

/** One severity: a heading with the count, the first rows and, when there are more, a Show more button. */
export function ActionSection({
  group,
  phone,
  onAction,
  onOpenHolding,
}: { group: SeverityGroup } & Omit<ActionListProps, 'items'>) {
  const headingId = useId();
  const [shown, setShown] = useState(rowStep);
  const { icon, hue } = severityLook[group.severity];
  const rows = group.items.slice(0, shown);
  const remaining = group.items.length - rows.length;
  return (
    <Box component="section" aria-labelledby={headingId}>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1, mb: 0.5 }}>
        <IconChip icon={icon} hue={hue} />
        <Typography id={headingId} variant="h3" component="h3">
          {`${severityLabels[group.severity]} (${formatNumber(group.items.length)})`}
        </Typography>
      </Stack>
      <Stack
        component="ul"
        aria-labelledby={headingId}
        divider={<Divider component="li" />}
        sx={{ m: 0, p: 0, listStyle: 'none' }}
      >
        {rows.map((item) => (
          <ActionRow key={item.id} item={item} phone={phone} onAction={onAction} onOpenHolding={onOpenHolding} />
        ))}
      </Stack>
      {remaining > 0 && (
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1.5, mt: 1 }}>
          <Button
            size="small"
            onClick={() => {
              setShown(shown + rowStep);
            }}
          >
            {`Show ${formatNumber(Math.min(rowStep, remaining))} more`}
          </Button>
          <Typography variant="caption" color="text.secondary" className="numeric">
            {`Showing ${formatNumber(rows.length)} of ${formatNumber(group.items.length)}`}
          </Typography>
        </Stack>
      )}
    </Box>
  );
}
