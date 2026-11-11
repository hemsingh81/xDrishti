import { Box } from '@mui/material';
import { BasketSettingsSection } from './BasketSettingsSection';
import { BudgetsSection } from './BudgetsSection';
import { LooseningSection } from './LooseningSection';
import { SizingSection } from './SizingSection';

/** The owner's limit settings (BK-10 sizing and basket budgets, BK-08 basket settings, BK-10b banner): each change is an act with the password typed again. Loosening one is shown on the next plan until dismissed. */
export default function LimitsCard() {
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <SizingSection />
      <BudgetsSection />
      <BasketSettingsSection />
      <LooseningSection />
    </Box>
  );
}
