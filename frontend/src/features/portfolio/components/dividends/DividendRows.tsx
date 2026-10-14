import { Box, Button, Stack, Typography } from '@mui/material';
import { useId, useMemo, useState } from 'react';
import type { DividendPreviewItem, DividendPreviewReport } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { ButtonTabs, type ButtonTab } from '@/shared/ui/ButtonTabs';
import {
  isOpen,
  openGroups,
  spellingsText,
  type Drafts,
  type MatchDraft,
  type NameGroup,
} from '../../lib/dividendImport';
import { ItemsTable } from './ItemsTable';
import { MatchDialog } from './MatchDialog';
import { NeedsMatchTable } from './NeedsMatchTable';

type RowsTab = 'ready' | 'open' | 'duplicates' | 'imported';

interface DividendRowsProps {
  report: DividendPreviewReport;
  accountId: string;
  drafts: Drafts;
  lines: ReadonlySet<number>;
  onChoose: (draft: MatchDraft) => void;
  onUnchoose: (key: string) => void;
  onInclude: (line: number, on: boolean) => void;
  onIncludeAll: (on: boolean) => void;
}

/**
 * The rows of the file by status, one table at a time: ready to add (matched), needing a match (per name, with the picker),
 * possible duplicates (each with a tick to include it) and already imported.
 */
export function DividendRows({
  report,
  accountId,
  drafts,
  lines,
  onChoose,
  onUnchoose,
  onInclude,
  onIncludeAll,
}: DividendRowsProps) {
  const prefix = useId();
  const parts = useMemo(
    () => ({
      ready: report.items.filter((item) => item.status === 'new'),
      groups: openGroups(report.items),
      open: report.items.filter(isOpen),
      duplicates: report.items.filter((item) => item.status === 'possible_duplicate'),
      imported: report.items.filter((item) => item.status === 'imported'),
    }),
    [report.items],
  );
  const [tab, setTab] = useState<RowsTab>(parts.groups.length > 0 ? 'open' : 'ready');
  const [picking, setPicking] = useState<{
    key: string;
    label: string;
    remembered: boolean;
    candidates: NameGroup['candidates'];
  } | null>(null);

  const tabs: readonly ButtonTab<RowsTab>[] = (
    [
      ['ready', `Ready to add (${formatNumber(parts.ready.length)})`],
      ['open', `Needs a match (${formatNumber(parts.open.length)})`],
      ['duplicates', `Possible duplicates (${formatNumber(parts.duplicates.length)})`],
      ['imported', `Already imported (${formatNumber(parts.imported.length)})`],
    ] as const
  ).map(([value, label]) => ({ value, label, id: `${prefix}-tab-${value}`, panelId: `${prefix}-panel-${value}` }));
  const change = (item: DividendPreviewItem) => {
    setPicking({
      key: item.nameKey,
      label: item.name,
      remembered: item.match.kind === 'alias',
      candidates: item.match.candidates,
    });
  };
  const none = lines.size === 0;

  return (
    <Box>
      <ButtonTabs tabs={tabs} value={tab} onChange={setTab} label="Rows of the file" />
      <Box role="tabpanel" id={`${prefix}-panel-${tab}`} aria-labelledby={`${prefix}-tab-${tab}`}>
        {tab === 'ready' && (
          <ItemsTable
            label="Rows ready to add"
            items={parts.ready}
            variant="ready"
            drafts={drafts}
            lines={lines}
            onInclude={onInclude}
            onChange={change}
            emptyText="No row is ready to add."
          />
        )}
        {tab === 'open' && (
          <NeedsMatchTable
            groups={parts.groups}
            drafts={drafts}
            onChoose={(group) => {
              setPicking({
                key: group.key,
                label: spellingsText(group.spellings),
                remembered: false,
                candidates: group.candidates,
              });
            }}
          />
        )}
        {tab === 'duplicates' && (
          <Stack spacing={1}>
            <Typography variant="body2" color="text.secondary">
              These rows equal a dividend you typed in (same date, amount and holding). They are skipped unless you tick
              them.
            </Typography>
            {parts.duplicates.length > 0 && (
              <Box>
                <Button
                  size="small"
                  onClick={() => {
                    onIncludeAll(none);
                  }}
                >
                  {none ? 'Include all' : 'Include none'}
                </Button>
              </Box>
            )}
            <ItemsTable
              label="Possible duplicates"
              items={parts.duplicates}
              variant="duplicates"
              drafts={drafts}
              lines={lines}
              onInclude={onInclude}
              onChange={change}
              emptyText="No row equals a dividend you typed in."
            />
          </Stack>
        )}
        {tab === 'imported' && (
          <ItemsTable
            label="Rows already imported"
            items={parts.imported}
            variant="imported"
            drafts={drafts}
            lines={lines}
            onInclude={onInclude}
            onChange={change}
            emptyText="None of these rows was imported before."
          />
        )}
      </Box>
      {picking && (
        <MatchDialog
          accountId={accountId}
          nameKey={picking.key}
          label={picking.label}
          remembered={picking.remembered}
          candidates={picking.candidates}
          draft={drafts.get(picking.key)}
          onSave={onChoose}
          onRemove={onUnchoose}
          onClose={() => {
            setPicking(null);
          }}
        />
      )}
    </Box>
  );
}
