import { Alert, Box, Button, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink, useParams } from 'react-router';
import { EvidenceBlock, useUrlState } from '@/shared/analytics';
import { LoadingState } from '@/shared/ui';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import { profileQuery, type BasketProfile } from '../api/profileQueries';
import { ContributionBars } from './ContributionBars';
import { CorrelationPanel } from './CorrelationPanel';
import { DrawdownRibbon } from './DrawdownRibbon';
import { InsidePanel } from './InsidePanel';
import { MemberTable } from './MemberTable';
import { ProfileHeader } from './ProfileHeader';
import { RebasedChart } from './RebasedChart';
import { RegimePanel } from './RegimePanel';
import { RelativeStrengthPanel } from './RelativeStrengthPanel';
import { RiskTable } from './RiskTable';
import { RollingBetaChart } from './RollingBetaChart';
import { StrategyPanel } from './StrategyPanel';
import { TurnoverCapacityPanel } from './TurnoverCapacityPanel';

const tabs = ['overview', 'inside', 'liquidity', 'strategy'] as const;
type Tab = (typeof tabs)[number];

const tabList: readonly ButtonTab<Tab>[] = [
  { value: 'overview', label: 'Performance and risk', id: 'bp-tab-overview', panelId: 'bp-panel' },
  { value: 'inside', label: 'Inside the basket', id: 'bp-tab-inside', panelId: 'bp-panel' },
  { value: 'liquidity', label: 'Regimes and capacity', id: 'bp-tab-liquidity', panelId: 'bp-panel' },
  { value: 'strategy', label: 'Strategies', id: 'bp-tab-strategy', panelId: 'bp-panel' },
];

const grid = {
  display: 'grid',
  gap: 2,
  gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 340px), 1fr))',
  alignItems: 'start',
  minWidth: 0,
} as const;

function Panels({ tab, profile }: { tab: Tab; profile: BasketProfile }) {
  switch (tab) {
    case 'overview':
      return (
        <Box sx={grid}>
          <RebasedChart profile={profile} />
          <RiskTable profile={profile} />
          <DrawdownRibbon profile={profile} />
          <RollingBetaChart profile={profile} />
          <RelativeStrengthPanel profile={profile} />
        </Box>
      );
    case 'inside':
      return (
        <Box sx={grid}>
          <InsidePanel profile={profile} />
          <CorrelationPanel profile={profile} />
          <ContributionBars profile={profile} />
          <MemberTable profile={profile} />
        </Box>
      );
    case 'liquidity':
      return (
        <Box sx={grid}>
          <RegimePanel profile={profile} />
          <TurnoverCapacityPanel profile={profile} />
        </Box>
      );
    case 'strategy':
      return (
        <Box sx={grid}>
          <StrategyPanel profile={profile} />
        </Box>
      );
  }
}

/** `/baskets/:basketId/profile`: one basket's own performance insight. A read-only screen; every figure carries its n or its honest state. */
export function BasketProfilePage() {
  const { basketId = '' } = useParams();
  const url = useUrlState();
  const params = { from: url.text('from'), to: url.text('to'), index: url.text('index'), asOf: url.text('asOf') };
  const tab = url.choice('tab', tabs, 'overview');
  const result = useQuery(profileQuery(basketId, params));

  if (result.isPending) return <LoadingState label="Loading the basket profile…" />;
  if (result.isError) {
    return (
      <Box sx={{ display: 'grid', gap: 1.5 }}>
        <Alert
          severity="error"
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void result.refetch();
              }}
              sx={{ minHeight: 44 }}
            >
              Try again
            </Button>
          }
        >
          {result.error.message}
        </Alert>
        <Button component={RouterLink} to="/baskets" sx={{ minHeight: 44, justifySelf: 'start' }}>
          Back to the baskets
        </Button>
      </Box>
    );
  }
  const profile = result.data;
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <ProfileHeader
        profile={profile}
        from={profile.window.from}
        to={profile.window.to}
        onWindow={(patch) => {
          url.update({
            ...('from' in patch ? { from: patch.from ?? null } : {}),
            ...('to' in patch ? { to: patch.to ?? null } : {}),
          });
        }}
      />
      {profile.members.total === 0 ? (
        <Typography>This basket has no members in the window, so there is nothing to profile.</Typography>
      ) : (
        <>
          <ButtonTabs
            label="Profile sections"
            tabs={tabList}
            value={tab}
            onChange={(value) => {
              url.update({ tab: value === 'overview' ? null : value });
            }}
          />
          <Box role="tabpanel" id="bp-panel" aria-labelledby={`bp-tab-${tab}`} sx={{ minWidth: 0 }}>
            <Panels tab={tab} profile={profile} />
          </Box>
        </>
      )}
      <EvidenceBlock
        n={profile.evidence.n}
        range={{ from: profile.evidence.range.from, to: profile.evidence.range.to }}
        basis="unknown"
        basisText={profile.evidence.basis}
        limits={[...profile.evidence.limits, ...profile.evidence.unavailableInputs]}
      />
    </Box>
  );
}
