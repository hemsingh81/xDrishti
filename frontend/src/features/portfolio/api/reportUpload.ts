import { api, unwrap } from '@/shared/api';

/**
 * What the owner decided for a scrip name of a global transaction report (it applies to every file of the request): the name is
 * the server's `nameKey`; one of `holdingId`, `instrumentId` or `isin` says what it means. `remember` saves it for later files.
 */
export interface ReportChoice {
  name: string;
  holdingId?: string;
  instrumentId?: string;
  isin?: string;
  remember: boolean;
}

/** Downloaded files for an account; the rest is for the import (the digest of each preview the owner reviewed, ...). */
export interface ReportUpload {
  accountId: string;
  files: readonly File[];
  choices?: readonly ReportChoice[];
  /** One entry per file, in order: the digest of the reviewed preview, or null. Sent by the import only. */
  digests?: readonly (string | null)[];
  /** Rebuild the lots of the holdings whose result is OK (trade files). Sent by the import only. */
  rebuild?: boolean;
  /** Indexes (from 0) of the files to import although they look like another account's. Import only. */
  acceptOtherAccount?: readonly number[];
  /** Aborts the request (the preview is dropped when the dialog closes). */
  signal?: AbortSignal;
}

function formOf({ accountId, files, choices, digests, rebuild, acceptOtherAccount }: ReportUpload): FormData {
  const form = new FormData();
  form.set('accountId', accountId);
  for (const file of files) form.append('files', file, file.name);
  // A form field is text: the lists travel as JSON text.
  form.set('choices', JSON.stringify(choices ?? []));
  if (digests !== undefined) {
    form.set('digests', JSON.stringify(digests));
    form.set('acceptOtherAccount', JSON.stringify(acceptOtherAccount ?? []));
    if (rebuild !== undefined) form.set('rebuild', rebuild ? 'true' : 'false');
  }
  return form;
}

// The generated type of the form says what the fields are (`files` are binary strings there); the serializer sends the real
// multipart form, and the browser adds the boundary to the Content-Type itself.
const placeholder = (upload: ReportUpload) => ({
  accountId: upload.accountId,
  files: [],
  choices: null,
  digests: null,
  rebuild: null,
  acceptOtherAccount: null,
});

/** Reads the files against an account: what each is and what importing it would do. Nothing is stored. */
export async function previewReports(upload: ReportUpload) {
  return unwrap(
    await api.POST('/api/portfolio/imports/preview', {
      body: placeholder(upload),
      bodySerializer: () => formOf(upload),
      signal: upload.signal,
    }),
  );
}

/** Imports the files (each on its own, all or nothing); the answer has one result per file, in order. */
export async function importReports(upload: ReportUpload) {
  return unwrap(
    await api.POST('/api/portfolio/imports/import', {
      body: placeholder(upload),
      bodySerializer: () => formOf(upload),
      signal: upload.signal,
    }),
  );
}
