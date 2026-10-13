import { Button, Stack } from '@mui/material';
import { useRef, type KeyboardEvent } from 'react';
import { formatNumber } from '@/shared/lib/format';
import { alertPanelId, alertStatusLabels, alertStatuses, alertTabId, type AlertStatus } from '../../lib/alerts';

interface AlertTabsProps {
  value: AlertStatus;
  /** How many alerts each tab holds; undefined while that is not known. */
  counts: Record<AlertStatus, number | undefined>;
  onChange: (tab: AlertStatus) => void;
}

/**
 * Active · Triggered · Cancelled as a tab list (WAI-ARIA tabs: the selected tab is the one tab stop, arrow keys, Home and
 * End move between them and select). Plain buttons rather than MUI `Tabs`, measured: with `Tabs` in this lazy panel the
 * bundler hoists the shared Menu/Popover code into the first-load chunk (119.97 KB gzip with this component, 123.98 KB with
 * `Tabs`); the code of `Tabs` itself stays lazy, but the five-user threshold moves its neighbours.
 */
export function AlertTabs({ value, counts, onChange }: AlertTabsProps) {
  const refs = useRef(new Map<AlertStatus, HTMLButtonElement>());

  function onKeyDown(event: KeyboardEvent) {
    const at = alertStatuses.indexOf(value);
    const target =
      event.key === 'ArrowRight'
        ? alertStatuses[(at + 1) % alertStatuses.length]
        : event.key === 'ArrowLeft'
          ? alertStatuses[(at + alertStatuses.length - 1) % alertStatuses.length]
          : event.key === 'Home'
            ? alertStatuses[0]
            : event.key === 'End'
              ? alertStatuses[alertStatuses.length - 1]
              : undefined;
    if (!target) return;
    event.preventDefault();
    onChange(target);
    refs.current.get(target)?.focus();
  }

  return (
    <Stack
      direction="row"
      role="tablist"
      aria-label="Alert status"
      onKeyDown={onKeyDown}
      sx={{ borderBottom: 1, borderColor: 'divider', mb: 1, gap: 0.5 }}
    >
      {alertStatuses.map((status) => {
        const selected = status === value;
        const count = counts[status];
        return (
          <Button
            key={status}
            ref={(element) => {
              if (element) refs.current.set(status, element);
              else refs.current.delete(status);
            }}
            role="tab"
            id={alertTabId(status)}
            aria-selected={selected}
            aria-controls={alertPanelId(status)}
            tabIndex={selected ? 0 : -1}
            onClick={() => {
              onChange(status);
            }}
            color={selected ? 'primary' : 'inherit'}
            sx={{
              borderRadius: 0,
              mb: '-1px',
              borderBottom: 2,
              borderColor: selected ? 'primary.main' : 'transparent',
              fontWeight: selected ? 700 : 500,
            }}
          >
            {count === undefined ? alertStatusLabels[status] : `${alertStatusLabels[status]} (${formatNumber(count)})`}
          </Button>
        );
      })}
    </Stack>
  );
}
