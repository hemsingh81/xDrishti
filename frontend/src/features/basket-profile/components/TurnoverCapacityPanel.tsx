import { Typography } from '@mui/material';
import type { BasketProfile } from '../api/profileQueries';
import { countOf, croreOf, fractionOf } from '../lib/format';
import { FigureTable } from './FigureTable';
import { Section } from './Section';

/** How much the membership changed, and what the basket can absorb: ADV20 is the median of the last 20 daily traded values (close times volume). */
export function TurnoverCapacityPanel({ profile }: { profile: BasketProfile }) {
  const { turnover, capacity } = profile;
  return (
    <Section
      title="Turnover and capacity"
      note="Capacity is the share of each member's ADV20 set in the settings; a member with fewer than 20 sessions of volume is listed, never guessed."
    >
      <FigureTable
        label="Turnover and capacity figures"
        rows={[
          {
            id: 'turnover',
            label: `Member turnover (${String(turnover.adds)} added, ${String(turnover.removes)} removed)`,
            view: turnover.turnover,
            format: fractionOf,
          },
          { id: 'median', label: 'Median ADV20 of the members', view: capacity.medianAdv, format: croreOf },
          { id: 'p10', label: 'ADV20 of the 10th-percentile member', view: capacity.p10Adv, format: croreOf },
          {
            id: 'capacity',
            label: `Capacity at ${fractionOf(capacity.capacityPct)} of each ADV20`,
            view: capacity.basketCapacity,
            format: croreOf,
          },
          { id: 'asm', label: 'Members under ASM or GSM', view: capacity.asmGsmShare, format: fractionOf },
          { id: 'fno', label: 'Members in F&O', view: capacity.fnoShare, format: fractionOf },
        ]}
      />
      <Typography variant="body2" color="text.secondary">
        {`${countOf(capacity.counted)} members have a full window of volume.`}
        {capacity.shortMembers.length > 0
          ? ` Not counted for lack of volume: ${capacity.shortMembers.join(', ')}.`
          : ''}
      </Typography>
    </Section>
  );
}
