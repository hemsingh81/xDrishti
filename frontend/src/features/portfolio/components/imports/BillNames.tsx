import { Alert, Box, Button, Typography } from '@mui/material';
import { useState } from 'react';
import type { BillDetails, BillNameView } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import {
  nameKindLabels,
  openNameGroups,
  resolvedNames,
  spellingsLine,
  targetText,
  type BillDraft,
  type BillDrafts,
  type NameGroup,
} from '../../lib/reportImport';
import { Disclosure } from '../trades/Disclosure';
import { BillMatchDialog } from './BillMatchDialog';
import { NameSpellings } from './NameSpellings';
import { PagedTable, type PagedColumn } from './PagedTable';

interface BillNamesProps {
  bill: BillDetails;
  accountId: string;
  drafts: BillDrafts;
  /** The decisions differ from the ones this preview was read with. */
  choicesChanged: boolean;
  busy: boolean;
  onChoose: (draft: BillDraft) => void;
  onUnchoose: (key: string) => void;
  onPreviewAgain: () => void;
}

const resolvedColumns: readonly PagedColumn<BillNameView>[] = [
  { header: 'Name in the file', render: (name) => name.name },
  {
    header: 'Matched to',
    render: (name) => (
      <>
        <Typography component="span" sx={{ fontWeight: 600 }}>
          {name.symbol ?? name.securityName ?? name.isin ?? '—'}
        </Typography>
        {name.isin && (
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {name.isin}
          </Typography>
        )}
      </>
    ),
  },
  { header: 'Matched by', render: (name) => nameKindLabels[name.kind] ?? name.kind },
  { header: 'Rows', numeric: true, render: (name) => formatNumber(name.rows) },
  { header: 'Trades', numeric: true, render: (name) => formatNumber(name.trades) },
];

/**
 * The scrip names of a transaction report: the ones nothing (or more than one security) fits are the owner's questions, one
 * line per name key with its spellings; the rest are listed behind a button so a match by part of the name can be checked.
 * Choosing changes nothing in the preview until it is read again with the choices.
 */
export function BillNames({
  bill,
  accountId,
  drafts,
  choicesChanged,
  busy,
  onChoose,
  onUnchoose,
  onPreviewAgain,
}: BillNamesProps) {
  const [editing, setEditing] = useState<NameGroup | null>(null);
  const groups = openNameGroups(bill);
  const resolved = resolvedNames(bill);
  const columns: readonly PagedColumn<NameGroup>[] = [
    { header: 'Name in the file', render: (group) => <NameSpellings group={group} /> },
    { header: 'Rows', numeric: true, render: (group) => formatNumber(group.rows) },
    { header: 'Trades', numeric: true, render: (group) => formatNumber(group.trades) },
    {
      header: 'Your choice',
      render: (group) => {
        const draft = drafts.get(group.key);
        return draft ? (
          <>
            <Typography component="span" sx={{ fontWeight: 600 }}>
              {targetText(draft.target)}
            </Typography>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
              {draft.remember ? 'remembered for later files' : 'this file only'}
            </Typography>
          </>
        ) : (
          <Typography variant="body2" color="text.secondary">
            Not chosen: kept without an ISIN
          </Typography>
        );
      },
    },
    {
      header: 'Action',
      render: (group) => {
        const draft = drafts.get(group.key);
        return (
          <Button
            size="small"
            variant={draft ? 'text' : 'outlined'}
            aria-label={`${draft ? 'Change the match for' : 'Choose a match for'} ${spellingsLine(group.spellings)}`}
            onClick={() => {
              setEditing(group);
            }}
          >
            {draft ? 'Change' : 'Choose'}
          </Button>
        );
      },
    },
  ];
  return (
    <Box>
      <Typography component="h5" variant="h4" sx={{ mb: 0.5 }}>
        Names
      </Typography>
      <Typography variant="body2" sx={{ mb: 1 }}>
        {`${formatNumber(bill.namesTotal)} names in the file; ${formatNumber(bill.namesTotal - bill.needsMatch)} matched to a security, ${formatNumber(bill.needsMatch)} need a match.`}
      </Typography>
      {groups.length > 0 && (
        <>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            These names match no security, or more than one. A name you leave alone is imported without an ISIN (its
            trades are kept but cannot rebuild a holding). Choose a match, then preview again to apply your choices.
          </Typography>
          <PagedTable
            label="Names that need a match"
            noun="names"
            rows={groups}
            columns={columns}
            rowKey={(group) => group.key}
            minWidth={640}
          />
          {choicesChanged && (
            <Alert
              severity="info"
              sx={{ mt: 1 }}
              action={
                <Button color="inherit" size="small" disabled={busy} onClick={onPreviewAgain}>
                  Preview again with my matches
                </Button>
              }
            >
              You changed matches. Preview again to apply them before importing.
            </Alert>
          )}
        </>
      )}
      {resolved.length > 0 && (
        <Disclosure
          label={`Show the ${formatNumber(resolved.length)} matched names`}
          openLabel="Hide the matched names"
        >
          <PagedTable
            label="Matched names"
            noun="names"
            rows={resolved}
            total={bill.namesTotal - bill.needsMatch}
            columns={resolvedColumns}
            rowKey={(name, index) => `${name.nameKey}-${String(index)}`}
            minWidth={560}
          />
        </Disclosure>
      )}
      {editing && (
        <BillMatchDialog
          key={editing.key}
          accountId={accountId}
          group={editing}
          draft={drafts.get(editing.key)}
          onSave={onChoose}
          onRemove={onUnchoose}
          onClose={() => {
            setEditing(null);
          }}
        />
      )}
    </Box>
  );
}
