/** Most entries the API resolves in one call. */
export const maxSymbols = 5000;
/** Largest file read in the browser (5,000 symbols are far smaller). */
export const maxFileBytes = 1_000_000;

/** Tickers, canonical symbols and ISINs contain no blanks: entries are separated by new lines, commas, semicolons or blanks. */
export function parseSymbols(text: string): string[] {
  return text.split(/[\s,;]+/).filter((entry) => entry !== '');
}

/** One CSV line into cells; double quotes group a cell and `""` is a quote inside it. */
function csvCells(line: string): string[] {
  const cells: string[] = [];
  let cell = '';
  let quoted = false;
  for (let i = 0; i < line.length; i += 1) {
    const char = line.charAt(i);
    if (quoted) {
      if (char === '"' && line.charAt(i + 1) === '"') {
        cell += '"';
        i += 1;
      } else if (char === '"') quoted = false;
      else cell += char;
    } else if (char === '"') quoted = true;
    else if (char === ',' || char === ';' || char === '\t') {
      cells.push(cell.trim());
      cell = '';
    } else cell += char;
  }
  cells.push(cell.trim());
  return cells;
}

const headerNames = ['symbol', 'symbols', 'ticker', 'tickers', 'isin', 'tradingsymbol', 'instrument', 'scrip'];

/**
 * The symbols of a CSV file: the column named symbol / ticker / ISIN … when the first row is a header, else the first
 * column. Other columns (names, quantities) are ignored. Returns one entry per line.
 */
export function symbolsFromCsv(text: string): string[] {
  const lines = text.split(/\r?\n/).filter((line) => line.trim() !== '');
  const first = lines[0];
  if (first === undefined) return [];
  const header = csvCells(first).map((cell) => cell.toLowerCase());
  const named = header.findIndex((cell) => headerNames.includes(cell));
  const column = named >= 0 ? named : 0;
  const body = named >= 0 ? lines.slice(1) : lines;
  return body.map((line) => csvCells(line)[column] ?? '').filter((cell) => cell !== '');
}
