import { api, unwrap } from '@/shared/api';

/** A tradebook for an account; `rebuild` and `digest` are for the import (the digest of the preview the owner reviewed). */
export interface TradeUpload {
  accountId: string;
  file: File;
  rebuild?: boolean;
  digest?: string;
  /** Aborts the request (the preview is dropped when the drawer closes). */
  signal?: AbortSignal;
}

function formOf({ accountId, file, rebuild, digest }: TradeUpload): FormData {
  const form = new FormData();
  form.set('accountId', accountId);
  form.set('file', file, file.name);
  if (rebuild !== undefined) form.set('rebuild', rebuild ? 'true' : 'false');
  if (rebuild === true && digest !== undefined) form.set('digest', digest);
  return form;
}

// The generated type of the form says what the fields are (`file` is a binary string there); the serializer sends the real
// multipart form, and the browser adds the boundary to the Content-Type itself.
const placeholder = (upload: TradeUpload) => ({ accountId: upload.accountId, file: '', rebuild: null, digest: null });

/** Reads a tradebook against an account: what importing it would do. Nothing is stored. */
export async function previewTrades(upload: TradeUpload) {
  return unwrap(
    await api.POST('/api/portfolio/trades/preview', {
      body: placeholder(upload),
      bodySerializer: () => formOf(upload),
      signal: upload.signal,
    }),
  );
}

/** Imports the tradebook (and, with `rebuild`, rebuilds the lots of the holdings the preview marked OK). */
export async function importTrades(upload: TradeUpload) {
  return unwrap(
    await api.POST('/api/portfolio/trades/import', {
      body: placeholder(upload),
      bodySerializer: () => formOf(upload),
      signal: upload.signal,
    }),
  );
}
