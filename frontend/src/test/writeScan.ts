/**
 * Finds the ways a source file can write to the server (P8-18 criterion 15). Pure: text in, the names of what it found out.
 * Case-insensitive, so `method: 'post'` and `api.put` are caught as well as the upper-case forms.
 */
const rules: [string, RegExp][] = [
  ['api write call', /\bapi\s*\.\s*(post|put|patch|delete)\b/i],
  ['useMutation', /\buseMutation\b/i],
  ['ensureOk', /\bensureOk\b/i],
  ['method property', /\bmethod\s*:\s*[^,}\n]*?(post|put|patch|delete)\b/i],
  ['sendBeacon', /\bsendBeacon\b/i],
  ['XMLHttpRequest', /\bXMLHttpRequest\b/i],
  ['WebSocket', /\bnew\s+WebSocket\b/i],
  ['hub write', /\.\s*(invoke|send)\s*\(/i],
];

/** A `fetch(` whose options do not say the string literal 'GET' (a variable method, or none given, is a finding). */
function fetchFinding(text: string): boolean {
  for (const match of text.matchAll(/\bfetch\s*\(/gi)) {
    const rest = text.slice(match.index + match[0].length, match.index + match[0].length + 300);
    if (!/\bmethod\s*:\s*['"]GET['"]/.test(rest)) return true;
  }
  return false;
}

export function findWrites(text: string): string[] {
  const found = rules.filter(([, pattern]) => pattern.test(text)).map(([name]) => name);
  if (fetchFinding(text)) found.push('fetch that is not GET');
  return found;
}
