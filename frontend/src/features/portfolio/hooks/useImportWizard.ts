import { useEffect, useRef, useState } from 'react';
import type { TradeImportResult, TradePreviewReport } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useImportTrades, usePreviewTrades } from '../api/tradeMutations';
import { problemKind } from '../lib/errors';
import { staleReport } from '../lib/tradeErrors';
import { validateTradeFile } from '../lib/tradeFile';
import { importedText } from '../lib/tradeText';

/**
 * The state of the import wizard: the account and the file, the preview of that file, whether the owner confirmed the
 * replacement of hand-dated lots, and the result of an import.
 *
 * The preview is for the account and file it was read for. While a preview or an import runs the controls are locked (`busy`);
 * and every request carries the generation it was made in, so an answer that comes after the choice changed is dropped: a
 * late preview never replaces a newer one, and a late import result never shows "Import done" for another file (its toast is
 * still shown: the trades were stored). A preview the drawer abandons is aborted; an import is never aborted (it may already
 * have been stored). A stale-preview refusal brings the fresh preview, or, when the server did not send a usable one, reads
 * the file again.
 */
export function useImportWizard(initialAccountId: string) {
  const { notify } = useToast();
  const previewing = usePreviewTrades();
  const importing = useImportTrades();
  const [accountId, setAccountId] = useState(initialAccountId);
  const [file, setFile] = useState<File | null>(null);
  const [fileProblem, setFileProblem] = useState<string | null>(null);
  const [report, setReport] = useState<TradePreviewReport | null>(null);
  const [stale, setStale] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const [done, setDone] = useState<TradeImportResult | null>(null);
  const generation = useRef(0);
  const previewAbort = useRef<AbortController | null>(null);

  // The drawer closing drops the preview that is still being read.
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
    setConfirmed(false);
    setDone(null);
    previewing.reset();
    importing.reset();
  }

  function chooseAccount(id: string) {
    setAccountId(id);
    forget();
  }

  function chooseFile(next: File) {
    forget();
    const problem = validateTradeFile(next);
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
    if (!file || accountId === '') return;
    const mine = generation.current;
    const controller = new AbortController();
    previewAbort.current = controller;
    try {
      const result = await previewing.mutateAsync({ accountId, file, signal: controller.signal });
      if (mine !== generation.current) return;
      setReport(result);
      setStale(review);
      setConfirmed(false);
    } catch {
      // The panel shows what the server said. A preview that was aborted (the choice changed, the drawer closed) was
      // dropped on purpose: its mutation was reset, so nothing is shown for it.
    }
  }

  async function preview() {
    if (!file || accountId === '') return;
    forget();
    await read(false);
  }

  async function runImport(rebuild: boolean) {
    if (!file || !report || accountId === '') return;
    const mine = generation.current;
    try {
      const result = await importing.mutateAsync({ accountId, file, rebuild, digest: report.digest });
      // The trades are stored whatever the screen shows now.
      notify(importedText(result));
      if (mine !== generation.current) return;
      setReport(null);
      setDone(result);
    } catch (error) {
      if (mine !== generation.current || problemKind(error) !== 'preview_stale') return;
      // Holdings, lots, corporate actions or trades changed since the preview: nothing was applied; review a new one.
      importing.reset();
      setConfirmed(false);
      const fresh = staleReport(error);
      if (fresh) {
        setReport(fresh);
        setStale(true);
        return;
      }
      // No usable preview came with the refusal: the old digest is useless, so read the file again.
      setReport(null);
      await read(true);
    }
  }

  return {
    accountId,
    file,
    fileProblem,
    report,
    stale,
    confirmed,
    setConfirmed,
    done,
    busy,
    previewing,
    importing,
    chooseAccount,
    chooseFile,
    clearFile,
    preview,
    runImport,
    startOver: clearFile,
  };
}
