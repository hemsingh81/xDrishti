import { Alert } from '@mui/material';
import type { AccountSummary } from '@/shared/api';

const names = (accounts: AccountSummary[]) => accounts.map((a) => a.name);

function list(items: string[]) {
  return items.map((item, index) => (
    <span key={item}>
      {index > 0 && ', '}
      <strong>{item}</strong>
    </span>
  ));
}

/** One sentence on where market data comes from and where allocations go — or a warning when there is no primary. */
export function AccountsBanner({ accounts }: { accounts: AccountSummary[] }) {
  const primary = accounts.find((a) => a.roles.data.primary);
  const standby = accounts.find((a) => a.roles.data.standby);
  const trading = accounts.filter((a) => a.roles.trading.enabled);
  const portfolioCount = accounts.filter((a) => a.roles.portfolio.enabled).length;

  if (!primary) {
    return (
      <Alert role="status" severity="warning" sx={{ mb: 2 }}>
        <strong>No primary Data account</strong> — market-data services cannot run. Give an account the Data role and
        make it primary.
      </Alert>
    );
  }
  return (
    <Alert role="status" severity="info" sx={{ mb: 2 }}>
      Market data comes from <strong>{primary.name}</strong> (primary Data)
      {standby ? (
        <>
          ; <strong>{standby.name}</strong> is standby
        </>
      ) : (
        '; no standby configured'
      )}
      . Trading allocations go to {trading.length > 0 ? list(names(trading)) : 'no account'}; portfolio covers{' '}
      {portfolioCount} account{portfolioCount === 1 ? '' : 's'}.
    </Alert>
  );
}
