import PieChartIcon from '@mui/icons-material/PieChartOutlined';
import type { ReactNode } from 'react';
import { PageHeader } from '@/shared/ui';

/** The header every state of the page shares; `actions` are the buttons that need the loaded accounts. */
export function PortfolioHeader({ actions }: { actions?: ReactNode }) {
  return (
    <PageHeader
      dense
      title="Portfolio"
      icon={PieChartIcon}
      hue="portfolio"
      description="What you hold at Dhan: invested, current value, profit or loss and XIRR, worked out from the dates you gave each purchase."
      actions={actions}
    />
  );
}
