import { useEffect, useRef, useState } from 'react';
import type { DividendImportResult, DividendPreviewReport } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useImportDividends, usePreviewDividends } from '../api/dividendMutations';
import { choiceOf, importedText, keepForReport, noDrafts, type Drafts, type MatchDraft } from '../lib/dividendImport';
import { staleDividendReport } from '../lib/dividendErrors';
import { problemKind } from '../lib/errors';
import { validateTradeFile } from '../lib/tradeFile';

const noLines: ReadonlySet<number> = new Set();

/**
 * The state of the dividend import: the file, its preview, the owner's decisions (a holding for a name, the possible duplicates
 * to include) and the result of the import. The account is the Cash & dividends panel's.
 *
 * Like the trade import: the preview belongs to the file it was read for; while a preview or an import runs the controls are
 * locked; every request carries the generation it was made in, so an answer that comes after the choice changed is dropped (the
 * toast of an import is still shown: the dividends were stored). A stale-preview refusal brings the fresh preview, with the
 * decisions that still apply, or, when none came, reads the file again.
 */
export function useDividendImportWizard(accountId: string) {
  const { notify } = useToast();
  const previewing = usePreviewDividends();
  const importing = useImportDividends();
  const [file, setFile] = useState<File | null>(null);
  const [fileProblem, setFileProblem] = useState<string | null>(null);
  const [report, setReport] = useState<DividendPreviewReport | null>(null);
  const [stale, setStale] = useState(false);
  const [drafts, setDrafts] = useState<Drafts>(noDrafts);
  const [lines, setLines] = useState<ReadonlySet<number>>(noLines);
  const [done, setDone] = useState<DividendImportResult | null>(null);
  const generation = useRef(0);
  const previewAbort = useRef<AbortController | null>(null);

  useEffect(
    () => () => {
      previewAbort.current?.abort();
    },
    [],
  );

  const busy = previewing.isPending || importing.isPending;

  function forget() {
    generation.current += 1;
    previewAbort.current?.abort();
    setReport(null);
    setStale(false);
    setDrafts(noDrafts);
    setLines(noLines);
    setDone(null);
    previewing.reset();
    importing.reset();
  }

  function chooseFile(next: File) {
    forget();
    const problem = validateTradeFile(next, 'dividend list');
    setFileProblem(problem ?? null);
    setFile(problem ? null : next);
  }

  function clearFile() {
    forget();
    setFile(null);
    setFileProblem(null);
  }

  /** Reads the file; `review` marks the result as the new preview that replaces a stale one. */
  async function read(review: boolean) {
    if (!file) return;
    const mine = generation.current;
    const controller = new AbortController();
    previewAbort.current = controller;
    try {
      const result = await previewing.mutateAsync({ accountId, file, signal: controller.signal });
      if (mine !== generation.current) return;
      if (review) {
        const kept = keepForReport(result, drafts, lines);
        setDrafts(kept.drafts);
        setLines(kept.lines);
      }
      setReport(result);
      setStale(review);
    } catch {
      // The form shows what the server said; an aborted preview was dropped on purpose.
    }
  }

  async function preview() {
    if (!file) return;
    forget();
    await read(false);
  }

  function choose(draft: MatchDraft) {
    setDrafts((previous) => new Map(previous).set(draft.key, draft));
  }

  function unchoose(key: string) {
    setDrafts((previous) => {
      const next = new Map(previous);
      next.delete(key);
      return next;
    });
  }

  function include(line: number, on: boolean) {
    setLines((previous) => {
      const next = new Set(previous);
      if (on) next.add(line);
      else next.delete(line);
      return next;
    });
  }

  function includeAll(on: boolean) {
    setLines(
      on
        ? new Set((report?.items ?? []).filter((item) => item.status === 'possible_duplicate').map((i) => i.line))
        : noLines,
    );
  }

  async function runImport() {
    if (!file || !report) return;
    const mine = generation.current;
    try {
      const result = await importing.mutateAsync({
        accountId,
        file,
        digest: report.digest,
        choices: [...drafts.values()].map(choiceOf),
        includeDuplicates: [...lines].sort((a, b) => a - b),
      });
      // The dividends are stored whatever the screen shows now.
      notify(importedText(result));
      if (mine !== generation.current) return;
      setReport(null);
      setDone(result);
    } catch (error) {
      if (mine !== generation.current || problemKind(error) !== 'preview_stale') return;
      // Holdings, aliases or dividends changed since the preview: nothing was stored; the owner confirms a new one.
      importing.reset();
      const fresh = staleDividendReport(error);
      if (fresh) {
        const kept = keepForReport(fresh, drafts, lines);
        setDrafts(kept.drafts);
        setLines(kept.lines);
        setReport(fresh);
        setStale(true);
        return;
      }
      setReport(null);
      await read(true);
    }
  }

  return {
    file,
    fileProblem,
    report,
    stale,
    drafts,
    lines,
    done,
    busy,
    previewing,
    importing,
    chooseFile,
    clearFile,
    preview,
    choose,
    unchoose,
    include,
    includeAll,
    runImport,
    startOver: clearFile,
  };
}
