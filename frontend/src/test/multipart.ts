import { Blob as NodeBlob, File as NodeFile } from 'node:buffer';

/**
 * Multipart uploads in tests (the trades import). Node's `fetch` takes only Node's own `FormData`, `Blob` and `File`, while
 * the jsdom environment replaces them with its own. `stubUploads()` (call it at the top of a test file that uploads) puts Node's
 * into place for each test and takes them out afterwards; files are made with `csvFile`. The network mock then gets the form as
 * text (see `setup.ts`), which `readUpload` reads in a handler.
 */
export function stubUploads() {
  beforeEach(async () => {
    // Node builds its FormData for a form it parses; the class is the one `new FormData()` must be.
    const form = await new Response('a=1', {
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
    }).formData();
    vi.stubGlobal('Blob', NodeBlob);
    vi.stubGlobal('File', NodeFile);
    vi.stubGlobal('FormData', form.constructor);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });
}

/** A CSV file for an upload test (Node's `File`: the component reads only its `name`, `size` and bytes). */
export function csvFile(text: string, name = 'tradebook.csv', type = 'text/csv'): File {
  return new NodeFile([text], name, { type });
}

/** A file of `bytes` bytes, for the size limit, without a big string in the test. */
export function bigFile(bytes: number, name = 'big.csv'): File {
  return new NodeFile([new Uint8Array(bytes)], name, { type: 'text/csv' });
}

export interface Upload {
  /** The text fields of the form (accountId, rebuild, digest, ...). */
  fields: Record<string, string>;
  /** The file part, if any. */
  file: { name: string; text: string } | null;
}

/** Reads a multipart request the app sent: its fields and the file. The boundary comes from the request's Content-Type. */
export async function readUpload(request: Request): Promise<Upload> {
  const type = request.headers.get('content-type') ?? '';
  const boundary = /boundary=(?:"([^"]+)"|([^;]+))/.exec(type);
  const marker = `--${boundary?.[1] ?? boundary?.[2] ?? ''}`;
  const upload: Upload = { fields: {}, file: null };
  for (const part of (await request.text()).split(marker)) {
    const split = part.indexOf('\r\n\r\n');
    if (split < 0) continue;
    const head = part.slice(0, split);
    const body = part.slice(split + 4).replace(/\r\n$/, '');
    const name = /name="([^"]*)"/.exec(head)?.[1];
    const filename = /filename="([^"]*)"/.exec(head)?.[1];
    if (name === undefined) continue;
    if (filename !== undefined) upload.file = { name: filename, text: body };
    else upload.fields[name] = body;
  }
  return upload;
}
