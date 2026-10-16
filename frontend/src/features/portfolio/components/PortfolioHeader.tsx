import PaymentsIcon from '@mui/icons-material/PaymentsOutlined';
import PieChartIcon from '@mui/icons-material/PieChartOutlined';
import ReceiptIcon from '@mui/icons-material/ReceiptLongOutlined';
import { Button, Stack } from '@mui/material';
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

/** The buttons that open the side panels: the account's cash movements and the trades. */
export function PortfolioHeaderActions({ onCash, onTrades }: { onCash: () => void; onTrades: () => void }) {
  return (
    <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
      <Button
        variant="outlined"
        startIcon={<PaymentsIcon />}
        onClick={() => {
          onCash();
        }}
      >
        Cash &amp; dividends
      </Button>
      <Button
        variant="outlined"
        startIcon={<ReceiptIcon />}
        onClick={() => {
          onTrades();
        }}
      >
        Trades
      </Button>
    </Stack>
  );
}
