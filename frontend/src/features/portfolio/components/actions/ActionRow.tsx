import { Box, Chip, Stack, Typography } from '@mui/material';
import { useId } from 'react';
import type { ActionItem } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import { IconChip } from '@/shared/ui';
import { figuresOf } from '../../lib/actionFigures';
import {
  rowActionsOf,
  ruleLabel,
  severityLabels,
  severityOf,
  stateOf,
  subjectOf,
  type RowActionKey,
} from '../../lib/actions';
import { ActionFigures } from './ActionFigures';
import { ActionRowActions } from './ActionRowActions';
import { ActionSubject } from './ActionSubject';
import { severityLook } from './severityLook';

interface ActionRowProps {
  item: ActionItem;
  onAction: (item: ActionItem, action: RowActionKey) => void;
  /** The holding link (the symbol, or the largest holding of a sector) opens the holding drawer. */
  onOpenHolding: (item: ActionItem) => void;
  /** A phone: the buttons fold into an Actions menu (decided once by the list, not by every row). */
  phone: boolean;
}

/** One item of the Action center: severity (icon and word), rule, the holding, the sentence, its numbers and its buttons. */
export function ActionRow({ item, onAction, onOpenHolding, phone }: ActionRowProps) {
  const titleId = useId();
  const subjectId = useId();
  const severity = severityOf(item);
  const { icon, hue } = severityLook[severity];
  const state = stateOf(item);

  return (
    <Stack
      component="li"
      data-item-id={item.id}
      aria-labelledby={`${subjectId} ${titleId}`}
      direction={{ xs: 'column', md: 'row' }}
      sx={{ gap: 1.5, py: 1.5, alignItems: { md: 'flex-start' }, justifyContent: 'space-between' }}
    >
      <Box sx={{ minWidth: 0, flex: 1 }}>
        <Stack direction="row" sx={{ alignItems: 'center', flexWrap: 'wrap', columnGap: 1, rowGap: 0.5 }}>
          <IconChip icon={icon} hue={hue} />
          <Typography variant="caption" sx={{ fontWeight: 650 }}>
            {severityLabels[severity]}
          </Typography>
          <Chip size="small" variant="outlined" label={ruleLabel(item.rule)} />
          <ActionSubject
            item={item}
            id={subjectId}
            onOpenHolding={() => {
              onOpenHolding(item);
            }}
          />
          {item.symbol && (
            <Typography variant="caption" color="text.secondary">
              {item.accountName}
            </Typography>
          )}
        </Stack>
        <Typography id={titleId} sx={{ fontWeight: 600, mt: 0.75, overflowWrap: 'anywhere' }}>
          {item.title}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
          {item.detail}
        </Typography>
        <ActionFigures figures={figuresOf(item)} />
        {state === 'snoozed' && (
          <Typography variant="body2" sx={{ mt: 0.75, fontWeight: 600 }}>
            {item.snoozedUntil ? `Snoozed until ${formatDate(item.snoozedUntil)}` : 'Snoozed'}
          </Typography>
        )}
        {state === 'done' && (
          <Typography variant="body2" sx={{ mt: 0.75, fontWeight: 600 }}>
            Marked done. It comes back if the reason goes away and then returns.
          </Typography>
        )}
      </Box>
      <ActionRowActions
        phone={phone}
        actions={rowActionsOf(item)}
        subject={subjectOf(item)}
        onAction={(action) => {
          onAction(item, action);
        }}
      />
    </Stack>
  );
}
