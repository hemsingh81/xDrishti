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
  let partial = '';
  let overflowed = false;
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

  const append = (segment: string) => {
    if (overflowed) return;
    partial += segment;
    if (partial.length > maxPending) {
      // A hostile endless line: skip everything up to the next line terminator, so its tail never forms a field or a frame.
      overflowed = true;
      partial = '';
    }
  };

  return {
    push(text) {
      let start = 0;
      for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (skipLf) {
          skipLf = false;
          if (c === '\n') {
            start = i + 1;
            continue;
          }
        }
        if (c === '\n' || c === '\r') {
          append(text.slice(start, i));
          if (!overflowed) line(partial);
          partial = '';
          overflowed = false;
          start = i + 1;
          skipLf = c === '\r';
        }
      }
      append(text.slice(start));
    },
    end() {
      partial = '';
      overflowed = false;
      event = '';
      data = [];
      dataSize = 0;
    },
  };
}
