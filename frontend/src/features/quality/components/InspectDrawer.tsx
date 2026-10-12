import { Drawer } from '@mui/material';
import type { QualityWatch } from '../hooks/useQualityWatch';
import { InspectPanel } from './InspectPanel';

interface InspectDrawerProps {
  /** The open instrument; null keeps the drawer closed. */
  id: string | null;
  watch: QualityWatch;
  onClose: () => void;
}

/**
 * Side panel with everything the checks found for one instrument and the buttons that fix it. Wide (760 px from `md`),
 * the full width of the screen on a phone.
 */
export function InspectDrawer({ id, watch, onClose }: InspectDrawerProps) {
  return (
    <Drawer
      anchor="right"
      open={id !== null}
      onClose={onClose}
      slotProps={{
        paper: { 'aria-label': 'Inspect', sx: { width: { xs: '100%', sm: 560, md: 760 }, maxWidth: '100%' } },
      }}
    >
      {id !== null && <InspectPanel key={id} id={id} watch={watch} onClose={onClose} />}
    </Drawer>
  );
}
