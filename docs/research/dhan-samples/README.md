# Dhan sample answers

Sanitised sample answers that the client tests are written against. **UNVERIFIED** files are written from Dhan's documentation (made-up ids and prices), not recorded from the real API; they are replaced by a recording once the owner has run the feature for real.

| File | Endpoint | Status |
|------|----------|--------|
| `quote-success.json` | `POST /v2/marketfeed/quote` | UNVERIFIED (from the documentation, until the owner's first real run of the `holdings-quotes` service) |
| `quote-partial.json` | `POST /v2/marketfeed/quote` (a security with no price, one missing) | UNVERIFIED |
| `quote-error-401.json` | `POST /v2/marketfeed/quote` (token refused) | UNVERIFIED |

| `feed-ticker.hex` | Market-data WebSocket, ticker packet (code 2) of a share | UNVERIFIED (constructed from the documentation, until the owner's first real session of `xd-feed`) |
| `feed-index-ticker.hex` | Ticker packet of an index (segment `IDX_I`) | UNVERIFIED |
| `feed-quote.hex` | Quote packet (code 4) of a share | UNVERIFIED |
| `feed-previous-close.hex` | Previous-close packet (code 6) | UNVERIFIED |
| `feed-multi-packet.hex` | One frame of 116 bytes: quote + quote + previous close back to back | Shape OBSERVED 2026-10-14, field values constructed |
| `feed-disconnect.hex` | Disconnect packet (code 50, reason 807) | UNVERIFIED |

The `feed-*.hex` files are binary packets as hex text (one line of bytes, `#` lines are comments); the parser tests read them. Little-endian; 8-byte header (response code, message length int16, exchange segment, security id int32), then the payload of the code. The subscription messages are JSON text: `{"RequestCode":15,"InstrumentCount":n,"InstrumentList":[{"ExchangeSegment":"NSE_EQ","SecurityId":"1333"}]}` with `RequestCode` 15 (subscribe, ticker), 17 (subscribe, quote), 16 and 18 (unsubscribe), at most 100 instruments per message; the URL is `wss://api-feed.dhan.co?version=2&token=…&clientId=…&authType=2`.

**Observed in the first real session (2026-10-14, from the heartbeat's `packetsByCode`):** the length field of a packet header is the length of that packet *including* the 8-byte header; one WebSocket frame holds **several packets back to back** (a frame of 116 bytes = 50 + 50 + 16); codes 2 (ticker, 16 bytes), 4 (quote, 50 bytes) and 6 (previous close, 16 bytes) are seen. The parser walks the frame packet by packet (a header that declares less than 8 bytes or more than is left ends the walk and counts one drop). Everything else below is still unverified.

What the owner checks on the first real session of the feed (ADR 0024) - every item below is a guess from the documentation until then:
- the URL and its four query keys are accepted; an expired token answers 401 or 403 on the upgrade (the feed reports "the Data account's token was refused");
- the request codes 15 / 16 / 17 / 18 and the JSON field names are accepted;
- ~~the message length field is the length of the packet including the header~~ OBSERVED 2026-10-14 (also: several packets per frame);
- the segment codes (0 = `IDX_I`, 1 = `NSE_EQ`) and the packet layouts above, the **last trade time**: epoch seconds in UTC (if prices show a time 5 h 30 min ahead the feed sends India time as if it were UTC; the feed clamps a time more than a minute ahead of its clock to the clock, so prices are not blocked, but report it) and a quote packet's *day close* (the previous close or today's?);
- the disconnect reason codes that mean "token problem" (`FeedDisconnectReasons`: 802, 803, 806, 807, 808, 809);
- ticks arrive for all 3 context indices (NIFTY 50, NIFTY BANK, INDIA VIX) and at the subscription of a share the last known price is sent at once.

What the owner checks on the first real run (ADR 0019): the `client-id` header is accepted, `last_price` and `ohlc.close` are the names in the answer, `ohlc.close` is the previous session's close (compare day change with the Dhan app), and the rate limit of 1 request/s holds.

## 20-level depth (P7-16, ADR 0135)

| File | Endpoint | Status |
|------|----------|--------|
| `depth20-bid.hex` | Depth WebSocket `wss://depth-api-feed.dhan.co/twentydepth`, bid packet (code 41), 332 bytes, security 1333 | UNVERIFIED (constructed from the documentation, until the owner's first real depth session) |
| `depth20-ask.hex` | The ask packet (code 51) of the same book | UNVERIFIED |

Hex text as above. Little-endian (UNVERIFIED); a 12-byte header (message length int16, response code, segment, security id int32, sequence uint32) and 20 levels of 16 bytes (price float64, quantity uint32, orders uint32). The subscription is JSON text: `{"RequestCode":23,"InstrumentCount":n,"InstrumentList":[{"ExchangeSegment":"NSE_EQ","SecurityId":"1333"}]}` (50 instruments per connection); the URL is `wss://depth-api-feed.dhan.co/twentydepth?token=…&clientId=…&authType=2` (no `version`).

What the owner checks on the first real depth session: the plan includes depth (806 means it does not); the byte order is little endian; the code sits at byte 3 of the header (the documentation's text says "byte 12", its table says byte 3 - the parser follows the table and the length check); the sequence number's meaning; how many packets per second arrive per instrument; the disconnect packet's layout (reason code after the header); the codes 805 and 806. Then replace these two files with sanitised captures.
