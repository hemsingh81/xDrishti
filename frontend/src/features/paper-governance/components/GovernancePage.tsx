import { PageHeader } from '@/shared/ui';
import { GovernanceTab } from './GovernanceTab';

/** Stand-alone route of the Governance tab until the paper screen hosts it as a tab (spec P10-05). */
export function GovernancePage() {
  return (
    <>
      <PageHeader
        title="Paper: configuration governance"
        description="The paper count is measured against one configuration. A change that adds risk starts a new epoch; nothing here changes a setting or applies a hypothesis."
      />
      <GovernanceTab />
    </>
  );
}
