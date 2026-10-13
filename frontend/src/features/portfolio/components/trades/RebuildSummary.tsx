import { Alert, Box, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { TradeRebuild } from '@/shared/api';
import { capitalise, formatInr } from '@/shared/lib/format';
import { Facts, type Fact } from '@/shared/ui';
import {
  costDeltaText,
  costDiffers,
  costNoteText,
  costWhy,
  countOf,
  reasonText,
  replacedSentence,
  statusOf,
} from '../../lib/tradeText';
import { Pnl } from '../Figures';
import { Change } from './Change';
import { RebuildStatusChip } from './RebuildStatusChip';

/** The cost of the open lots (OK and Unchanged only: the API gives it when the trades fit) and, when it differs from the broker's, by how much and why. */
function costParts(rebuild: TradeRebuild, label: string): { facts: Fact[]; notes: ReactNode } {
  if (rebuild.openCostAfter === null || rebuild.openCostAfter === undefined) return { facts: [], notes: null };
  const delta = costDeltaText(rebuild);
  const note = costNoteText(rebuild);
  return {
    facts: [{ label, value: formatInr(rebuild.openCostAfter) }],
    notes: (
      <>
        {costDiffers(rebuild) && delta && (
          <Alert severity="info">
            {`${delta}${rebuild.brokerCost !== null && rebuild.brokerCost !== undefined ? ` (${formatInr(rebuild.brokerCost)})` : ''}. ${costWhy(rebuild)}`}
          </Alert>
        )}
        {note && <Alert severity="warning">{`Cost approximated: ${note}`}</Alert>}
      </>
    ),
  };
}

/** What a rebuild of one holding's lots does (or did): the status, the lots and the opening quantity before → after, the replaced lots and the cost. */
export function RebuildSummary({ rebuild }: { rebuild: TradeRebuild }) {
  const status = statusOf(rebuild);
  const reason = reasonText(rebuild);
  const replaced = replacedSentence(rebuild.handDatedLotsReplaced, rebuild.syncLotsReplaced);
  const cost = costParts(rebuild, status === 'ok' ? 'Cost of the open lots after' : 'Cost of the open lots');
  const facts: Fact[] =
    status === 'ok'
      ? [
          { label: 'Lots', value: <Change from={rebuild.lotsBefore} to={rebuild.lotsAfter} /> },
          {
            label: 'Opening quantity',
            value: <Change from={rebuild.openingQty.before} to={rebuild.openingQty.after} />,
          },
          {
            label: 'Realised profit from your sells',
            value: rebuild.realisedPnl === null ? '—' : <Pnl value={rebuild.realisedPnl} />,
          },
          ...cost.facts,
        ]
      : status === 'unchanged'
        ? cost.facts
        : [];
  return (
    <Stack spacing={1.5}>
      <Box>
        <RebuildStatusChip rebuild={rebuild} />
      </Box>
      {reason && <Typography>{reason}</Typography>}
      {status === 'unchanged' && (
        <Typography>The lots already match the trades, so there is nothing to change.</Typography>
      )}
      {facts.length > 0 && <Facts items={facts} />}
      {status === 'ok' && (
        <Typography variant="body2" color="text.secondary">
          Rebuilding replaces all the lots of the holding, including lots you dated by hand and lots a sync added.
          {rebuild.adjustedFor > 0
            ? ` Trades before a split or bonus are converted to today’s shares (${countOf(rebuild.adjustedFor, 'corporate action')}).`
            : ''}
        </Typography>
      )}
      {status === 'ok' && replaced && <Alert severity="warning">{`${capitalise(replaced)}.`}</Alert>}
      {(status === 'ok' || status === 'unchanged') && cost.notes}
    </Stack>
  );
}
