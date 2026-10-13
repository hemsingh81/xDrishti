import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import NotificationsIcon from '@mui/icons-material/NotificationsNoneOutlined';
import { Button, IconButton, Stack, Typography } from '@mui/material';
import type { Ref } from 'react';

interface ActionCenterHeaderProps {
  titleId: string;
  bodyId: string;
  /** Receives focus when the owner arrives from the bell. */
  titleRef: Ref<HTMLHeadingElement>;
  /** "5 need a decision" / "Nothing needs a decision"; null while it is not known. */
  countText: string | null;
  /** The account and bucket the items are for, when the scope bar narrows them; null = everything. */
  scopeLabel: string | null;
  collapsed: boolean;
  onToggle: () => void;
  onOpenAlerts: () => void;
}

/** The card's header: the title, how many items need a decision, the Alerts button and the fold button. */
export function ActionCenterHeader({
  titleId,
  bodyId,
  titleRef,
  countText,
  scopeLabel,
  collapsed,
  onToggle,
  onOpenAlerts,
}: ActionCenterHeaderProps) {
  return (
    <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap' }}>
      <Stack direction="row" sx={{ alignItems: 'baseline', columnGap: 1.5, rowGap: 0.25, flexWrap: 'wrap' }}>
        <Typography id={titleId} ref={titleRef} tabIndex={-1} variant="h2" component="h2" sx={{ outline: 'none' }}>
          Action center
        </Typography>
        {countText !== null && (
          <Typography color="text.secondary" className="numeric">
            {countText}
          </Typography>
        )}
        {scopeLabel !== null && (
          <Typography variant="body2" color="text.secondary">
            {`For ${scopeLabel}`}
          </Typography>
        )}
      </Stack>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 0.5 }}>
        <Button size="small" startIcon={<NotificationsIcon />} onClick={onOpenAlerts}>
          Alerts
        </Button>
        <IconButton
          aria-label={collapsed ? 'Show the Action center' : 'Hide the Action center'}
          aria-expanded={!collapsed}
          aria-controls={collapsed ? undefined : bodyId}
          onClick={onToggle}
          edge="end"
        >
          {collapsed ? <ExpandMoreIcon /> : <ExpandLessIcon />}
        </IconButton>
      </Stack>
    </Stack>
  );
}
