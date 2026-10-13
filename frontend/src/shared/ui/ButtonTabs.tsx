import { Button, Stack } from '@mui/material';
import { useRef, type KeyboardEvent } from 'react';

export interface ButtonTab<T extends string> {
  value: T;
  label: string;
  /** The id of the tab and of the panel it controls. */
  id: string;
  panelId: string;
}

interface ButtonTabsProps<T extends string> {
  tabs: readonly ButtonTab<T>[];
  value: T;
  onChange: (value: T) => void;
  /** Names the tab list. */
  label: string;
}

/**
 * A tab list (WAI-ARIA tabs: the selected tab is the one tab stop; arrow keys, Home and End move between the tabs and select
 * one) made of plain buttons rather than MUI `Tabs`, whose code would move into the first-load bundle when used in a lazy
 * panel (see the Alerts panel's tabs). Import it by path (`@/shared/ui/ButtonTabs`).
 */
export function ButtonTabs<T extends string>({ tabs, value, onChange, label }: ButtonTabsProps<T>) {
  const refs = useRef(new Map<T, HTMLButtonElement>());

  function onKeyDown(event: KeyboardEvent) {
    const at = tabs.findIndex((tab) => tab.value === value);
    const last = tabs.length - 1;
    const target =
      event.key === 'ArrowRight'
        ? tabs[at === last ? 0 : at + 1]
        : event.key === 'ArrowLeft'
          ? tabs[at <= 0 ? last : at - 1]
          : event.key === 'Home'
            ? tabs[0]
            : event.key === 'End'
              ? tabs[last]
              : undefined;
    if (!target) return;
    event.preventDefault();
    onChange(target.value);
    refs.current.get(target.value)?.focus();
  }

  return (
    <Stack
      direction="row"
      role="tablist"
      aria-label={label}
      onKeyDown={onKeyDown}
      sx={{ borderBottom: 1, borderColor: 'divider', mb: 1.5, gap: 0.5 }}
    >
      {tabs.map((tab) => {
        const selected = tab.value === value;
        return (
          <Button
            key={tab.value}
            ref={(element) => {
              if (element) refs.current.set(tab.value, element);
              else refs.current.delete(tab.value);
            }}
            role="tab"
            id={tab.id}
            aria-selected={selected}
            aria-controls={tab.panelId}
            tabIndex={selected ? 0 : -1}
            onClick={() => {
              onChange(tab.value);
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
            {tab.label}
          </Button>
        );
      })}
    </Stack>
  );
}
