import { api, unwrap, type DividendChoice } from '@/shared/api';

/** A dividend list for an account; the rest is for the import (the digest of the preview the owner reviewed). */
export interface DividendUpload {
  accountId: string;
  file: File;
  digest?: string;
  /** What the owner decided for names the file left open. */
  choices?: readonly DividendChoice[];
  /** Lines of possible duplicates the owner ticked. */
  includeDuplicates?: readonly number[];
  /** Aborts the request (the preview is dropped when the dialog closes). */
  signal?: AbortSignal;
}

function formOf({ accountId, file, digest, choices, includeDuplicates }: DividendUpload): FormData {
  const form = new FormData();
  form.set('accountId', accountId);
  form.set('file', file, file.name);
  if (digest !== undefined) {
    form.set('digest', digest);
    // A form field is text: the lists travel as JSON text.
    form.set('choices', JSON.stringify(choices ?? []));
    form.set('includeDuplicates', JSON.stringify(includeDuplicates ?? []));
  }
  return form;
}

// The generated type of the form says what the fields are (`file` is a binary string there); the serializer sends the real
// multipart form, and the browser adds the boundary to the Content-Type itself.
const placeholder = (upload: DividendUpload) => ({
  accountId: upload.accountId,
  file: '',
  digest: null,
  choices: null,
  includeDuplicates: null,
});

/** Reads a dividend list against an account: what importing it would do. Nothing is stored. */
export async function previewDividends(upload: DividendUpload) {
  return unwrap(
    await api.POST('/api/portfolio/dividends/preview', {
      body: placeholder(upload),
      bodySerializer: () => formOf(upload),
      signal: upload.signal,
    }),
  );
}

/** Imports the dividend list as one batch (with the owner's choices and ticked duplicates). */
export async function importDividends(upload: DividendUpload) {
  return unwrap(
    await api.POST('/api/portfolio/dividends/import', {
      body: placeholder(upload),
      bodySerializer: () => formOf(upload),
      signal: upload.signal,
    }),
  );
}
