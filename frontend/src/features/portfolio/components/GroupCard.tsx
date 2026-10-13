import { Box, Card, Stack, Typography } from '@mui/material';
import { useId, type ReactNode } from 'react';
import type { HoldingGroup } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { GroupChildren, type GroupScope } from './GroupChildren';
import { GroupExpander } from './GroupExpander';
import {
  GroupDayChange,
  GroupInvested,
  GroupPnl,
  GroupValue,
  GroupWeight,
  GroupXirr,
  UnpricedFlag,
  type GroupFigures,
} from './GroupFigures';

function Figure({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography component="dt" variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Box component="dd" sx={{ m: 0, ...numeric }}>
        {children}
      </Box>
    </Box>
  );
}

/** The figures of a group or of the total as labelled pairs, two to a row (a phone has no room for columns). */
function Figures({ figures }: { figures: GroupFigures }) {
  return (
    <Box
      component="dl"
      sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 1.5, m: 0, mt: 1 }}
    >
      <Figure label="Value">
        <GroupValue group={figures} />
      </Figure>
      <Figure label="Invested">
        <GroupInvested group={figures} />
      </Figure>
      <Figure label="P&L">
        <GroupPnl group={figures} align="flex-start" />
      </Figure>
      <Figure label="XIRR">
        <GroupXirr group={figures} align="flex-start" />
      </Figure>
      <Figure label="Day change">
        <GroupDayChange group={figures} align="flex-start" />
      </Figure>
      <Figure label="Weight">
        <GroupWeight weightPct={figures.weightPct} />
      </Figure>
    </Box>
  );
}

/** The scope total on a phone: the card "All holdings" at the top of the list. */
export function TotalCard({ total }: { total: GroupFigures }) {
  return (
    <Card component="li" sx={{ listStyle: 'none', p: 1.5, borderColor: 'primary.main' }}>
      <Typography sx={{ fontWeight: 600 }}>All holdings</Typography>
      <Typography variant="caption" color="text.secondary">
        {`${formatNumber(total.holdings)} in this view`}
      </Typography>
      <UnpricedFlag holdings={total.holdings} unpriced={total.unpriced} />
      <Figures figures={total} />
    </Card>
  );
}

interface GroupCardProps {
  group: HoldingGroup;
  expanded: boolean;
  scope: GroupScope;
  onToggle: () => void;
}

/** One group on a phone: the name (the button that opens it), the figures, and the holdings when opened. */
export function GroupCard({ group, expanded, scope, onToggle }: GroupCardProps) {
  const regionId = useId();
  return (
    <Card component="li" sx={{ listStyle: 'none' }}>
      <Box sx={{ p: 1.5 }}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
          <GroupExpander name={group.name} expanded={expanded} controls={regionId} onToggle={onToggle} />
          <Typography variant="caption" color="text.secondary" sx={{ flex: 'none' }}>
            {`${formatNumber(group.holdings)} ${group.holdings === 1 ? 'holding' : 'holdings'}`}
          </Typography>
        </Stack>
        <UnpricedFlag holdings={group.holdings} unpriced={group.unpriced} />
        <Figures figures={group} />
      </Box>
      {expanded && <GroupChildren group={group} scope={scope} id={regionId} />}
    </Card>
  );
}
