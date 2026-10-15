import { useEffect, useRef, useState } from 'react';
import type { ImportFileError, ImportFilePreview, ImportFileResult } from '@/shared/api';
import { useToast, type ToastSeverity } from '@/shared/ui/toastContext';
import { useImportReports, usePreviewReports } from '../api/reportMutations';
import { addReportFiles } from '../lib/reportFiles';
import {
  choicesOf,
  hasOtherAccountWarning,
  importSummaryText,
  isTradeKind,
  keepDrafts,
  noBillDrafts,
  willChange,
  type BillDraft,
  type BillDrafts,
} from '../lib/reportImport';
import { rebuildTotals, type RebuildTotals } from '../lib/tradeText';

const none: ReadonlySet<number> = new Set();
const noOutcomes: ReadonlyMap<number, ImportFileResult> = new Map();
const noErrors: ReadonlyMap<number, ImportFileError> = new Map();
const noTotals: RebuildTotals = { holdings: 0, lotsBefore: 0, lotsAfter: 0, handDated: 0, sync: 0, adjusted: 0 };

/** The refusals of a file that can be tried again: nothing was stored and the file stays in the list. */
const retryable = (error: ImportFileError | null): boolean =>
  error !== null &&
  (error.code === 'portfolio.sync_running' ||
    error.code === 'portfolio.other_account' ||
    (error.code === 'portfolio.invalid' && (error.field === 'digest' || error.field === 'choices')));

const severityOf = (results: readonly ImportFileResult[]): ToastSeverity => {
  if (results.some((result) => result.status === 'rejected' || result.status === 'not_supported')) return 'warning';
  return results.some((result) => result.status === 'imported') ? 'success' : 'info';
};

/**
 * The state of the Import files screen: the files, their previews (one per file, in order), the owner's decisions (a security
 * for a name, the files of another account to import anyway, whether to rebuild lots) and what each import did.
 *
 * The previews belong to the files and account they were read for; any change of those drops them. While a preview or an import
 * runs the controls are locked, and every request carries the generation it was made in, so a late answer never replaces a
 * newer state (an import's toast is still shown: the files were stored). An import sends only the files that are still to be
 * imported, so a file that went stale (nothing was stored for it) can be confirmed again without sending the others twice.
 */
export function useReportImport(accountId: string) {
  const { notify } = useToast();
  const previewing = usePreviewReports();
  const importing = useImportReports();
  const [files, setFiles] = useState<File[]>([]);
  const [problems, setProblems] = useState<string[]>([]);
  const [previews, setPreviews] = useState<readonly ImportFilePreview[] | null>(null);
  const [outcomes, setOutcomes] = useState(noOutcomes);
  const [fileErrors, setFileErrors] = useState(noErrors);
  const [stale, setStale] = useState(none);
  const [asked, setAsked] = useState(none);
  const [ticks, setTicks] = useState(none);
  const [drafts, setDrafts] = useState<BillDrafts>(noBillDrafts);
  /** The decisions the preview on screen was read with . */
  const [applied, setApplied] = useState<string>('[]');
  /** The server refused the digests of the previews: they have to be read again before an import. */
  const [outdated, setOutdated] = useState(false);
  const [rebuild, setRebuild] = useState(true);
  const [confirmed, setConfirmed] = useState(false);
  const generation = useRef(0);
  const previewAbort = useRef<AbortController | null>(null);

  useEffect(
    () => () => {
      previewAbort.current?.abort();
    },
    [],
  );

  const busy = previewing.isPending || importing.isPending;
  const choices = choicesOf(drafts);
  const choicesChanged = previews !== null && applied !== JSON.stringify(choices);

  /** Drops the previews and what was decided about them; reading the same files again keeps the ticks for other accounts' files. */
  function resetReview(keepTicks = false) {
    generation.current += 1;
    previewAbort.current?.abort();
    setOutdated(false);
    setPreviews(null);
    setOutcomes(noOutcomes);
    setFileErrors(noErrors);
    setStale(none);
    setAsked(none);
    if (!keepTicks) setTicks(none);
    setConfirmed(false);
    previewing.reset();
    importing.reset();
  }

  function addFiles(added: readonly File[]) {
    const next = addReportFiles(files, added);
    setProblems(next.problems);
    if (next.files.length === files.length) return;
    resetReview();
    setFiles(next.files);
  }

  function removeFile(index: number) {
    resetReview();
    setProblems([]);
    setFiles((previous) => previous.filter((_, at) => at !== index));
  }

  function clearAll() {
    resetReview();
    setProblems([]);
    setFiles([]);
    setDrafts(noBillDrafts);
    setApplied('[]');
  }

  function changeAccount() {
    clearAll();
  }

  /** Reads the files with the decisions made so far; the previews replace the ones on screen. */
  async function preview() {
    if (files.length === 0 || accountId === '') return;
    resetReview(true);
    const mine = generation.current;
    const controller = new AbortController();
    previewAbort.current = controller;
    try {
      const response = await previewing.mutateAsync({ accountId, files, choices, signal: controller.signal });
      if (mine !== generation.current) return;
      const kept = keepDrafts(response.files, drafts);
      setDrafts(kept);
      setApplied(JSON.stringify(choicesOf(kept)));
      setPreviews(response.files);
    } catch {
      // The screen shows what the server said; a preview that was aborted was dropped on purpose.
    }
  }

  function choose(draft: BillDraft) {
    setDrafts((previous) => new Map(previous).set(draft.key, draft));
  }

  function unchoose(key: string) {
    setDrafts((previous) => {
      const next = new Map(previous);
      next.delete(key);
      return next;
    });
  }

  function tick(index: number, on: boolean) {
    setTicks((previous) => {
      const next = new Set(previous);
      if (on) next.add(index);
      else next.delete(index);
      return next;
    });
  }

  const needsTick = (index: number): boolean => {
    const preview = previews?.[index];
    return asked.has(index) || (preview !== undefined && hasOtherAccountWarning(preview));
  };

  /** The files an import would send now: read, still to import, with something to store, and ticked where they must be. */
  const entries = (previews ?? []).flatMap((preview, index) => {
    const file = files[index];
    const waiting = file !== undefined && !outcomes.has(index) && willChange(preview);
    return waiting && (!needsTick(index) || ticks.has(index)) ? [{ index, file, preview }] : [];
  });
  const totals = entries.reduce<RebuildTotals>((sum, { preview }) => {
    const part = preview.trades && isTradeKind(preview.kind) ? rebuildTotals(preview.trades.securities) : noTotals;
    return {
      holdings: sum.holdings + part.holdings,
      lotsBefore: sum.lotsBefore + part.lotsBefore,
      lotsAfter: sum.lotsAfter + part.lotsAfter,
      handDated: sum.handDated + part.handDated,
      sync: sum.sync + part.sync,
      adjusted: sum.adjusted + part.adjusted,
    };
  }, noTotals);
  const rebuilding = rebuild && totals.holdings > 0;
  const needsConfirmation = rebuilding && totals.handDated > 0;

  function applyResults(sent: typeof entries, results: readonly ImportFileResult[]) {
    if (previews === null) return;
    const nextPreviews = [...previews];
    const nextOutcomes = new Map(outcomes);
    const nextErrors = new Map(fileErrors);
    const nextStale = new Set(stale);
    const nextAsked = new Set(asked);
    let reread = false;
    for (const [position, { index }] of sent.entries()) {
      const result = results[position];
      if (!result) continue;
      nextErrors.delete(index);
      nextStale.delete(index);
      if (result.status === 'rejected' && result.error?.code === 'portfolio.preview_stale' && result.preview) {
        // Something changed since the preview: nothing was stored; the fresh preview is confirmed again.
        nextPreviews[index] = result.preview;
        nextStale.add(index);
      } else if (result.status === 'rejected' && retryable(result.error)) {
        if (result.error?.code === 'portfolio.other_account') nextAsked.add(index);
        if (result.error?.code === 'portfolio.invalid') reread = true;
        if (result.error) nextErrors.set(index, result.error);
      } else {
        nextOutcomes.set(index, result);
      }
    }
    setPreviews(nextPreviews);
    setOutcomes(nextOutcomes);
    setFileErrors(nextErrors);
    setStale(nextStale);
    setAsked(nextAsked);
    if (nextStale.size > stale.size || [...nextStale].some((index) => !stale.has(index))) {
      const kept = keepDrafts(nextPreviews, drafts);
      setDrafts(kept);
      setApplied(JSON.stringify(choicesOf(kept)));
      setConfirmed(false);
    }
    if (reread) setOutdated(true);
  }

  async function runImport() {
    if (previews === null || entries.length === 0 || accountId === '') return;
    const mine = generation.current;
    const sent = entries;
    try {
      const response = await importing.mutateAsync({
        accountId,
        files: sent.map((entry) => entry.file),
        choices,
        digests: sent.map((entry) => entry.preview.digest),
        rebuild: rebuilding,
        acceptOtherAccount: sent.flatMap((entry, position) => (ticks.has(entry.index) ? [position] : [])),
      });
      // The files are stored whatever the screen shows now.
      notify(importSummaryText(response.files), severityOf(response.files));
      if (mine !== generation.current) return;
      applyResults(sent, response.files);
    } catch {
      // The screen shows what the server said (the whole request was refused: nothing was stored).
    }
  }

  return {
    files,
    problems,
    previews,
    outcomes,
    fileErrors,
    stale,
    ticks,
    drafts,
    choicesChanged,
    outdated,
    rebuild,
    setRebuild,
    confirmed,
    setConfirmed,
    entries,
    totals,
    rebuilding,
    needsConfirmation,
    needsTick,
    busy,
    previewing,
    importing,
    addFiles,
    removeFile,
    clearAll,
    changeAccount,
    preview,
    choose,
    unchoose,
    tick,
    runImport,
  };
}
