/** One server-sent event: its name (`message` when none was given) and its `data` lines joined by newlines. */
export interface SseFrame {
  event: string;
  data: string;
}

export interface SseParser {
  /** Feed decoded text (any split: mid-line, mid-frame). */
  push: (text: string) => void;
  /** The stream closed: a frame that was not finished by a blank line is dropped (truncated frames are never delivered). */
  end: () => void;
}

/** Longest line or frame kept while waiting for its end; a hostile stream cannot grow memory without bound. */
const maxPending = 1_000_000;

/** A pure, incremental SSE parser: lines (LF, CRLF, CR), `event:`, `data:`, comment lines, an unknown field is ignored. */
export function createSseParser(onFrame: (frame: SseFrame) => void): SseParser {
  let buffer = '';
  let event = '';
  let data: string[] = [];
  let dataSize = 0;
  let skipLf = false;

  const dispatch = () => {
    if (data.length > 0) onFrame({ event: event || 'message', data: data.join('\n') });
    event = '';
    data = [];
    dataSize = 0;
  };

  const line = (text: string) => {
    if (text === '') {
      dispatch();
      return;
    }
    if (text.startsWith(':')) return undefined;
    const colon = text.indexOf(':');
    const field = colon < 0 ? text : text.slice(0, colon);
    let value = colon < 0 ? '' : text.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    if (field === 'event') event = value;
    else if (field === 'data' && dataSize + value.length <= maxPending) {
      data.push(value);
      dataSize += value.length;
    }
    return undefined;
  };

  return {
    push(text) {
      buffer += text;
      let start = 0;
      for (let i = 0; i < buffer.length; i++) {
        const c = buffer[i];
        if (skipLf && i === start) {
          skipLf = false;
          if (c === '\n') {
            start = i + 1;
            continue;
          }
        }
        if (c === '\n' || c === '\r') {
          line(buffer.slice(start, i));
          start = i + 1;
          skipLf = c === '\r';
        }
      }
      buffer = buffer.slice(start);
      if (buffer.length > maxPending) buffer = '';
    },
    end() {
      buffer = '';
      event = '';
      data = [];
      dataSize = 0;
    },
  };
}
