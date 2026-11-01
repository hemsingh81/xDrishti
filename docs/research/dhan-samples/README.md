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

## Quote packets for the trade-tick recorder (P7-16b, ADR 0173)

The tick recorder reads the same quote packet (code 4, 50 bytes) as `feed-quote.hex`, plus two fields the live feed ignores: the **last traded quantity** at bytes 13-14 (1-based; offset 12) read as **uint16**, and the **cumulative day volume** at offset 22 read as **uint32**. The test frames are constructed in `FeedPackets.Quote` (and `TickTestData.Quote`) from the documentation: **UNVERIFIED** until the pilot - signedness of both fields, whether the last trade time is UTC or an India-time epoch (a 19,800 s offset), whether one update covers several trades, the packet rate per instrument and whether the depth socket counts toward the five. The subscription is request code 17, at most 100 instruments per message.

## 20-level depth (P7-16, ADR 0135)

| File | Endpoint | Status |
|------|----------|--------|
| `depth20-bid.hex` | Depth WebSocket `wss://depth-api-feed.dhan.co/twentydepth`, bid packet (code 41), 332 bytes, security 1333 | UNVERIFIED (constructed from the documentation, until the owner's first real depth session) |
| `depth20-ask.hex` | The ask packet (code 51) of the same book | UNVERIFIED |

Hex text as above. Little-endian (UNVERIFIED); a 12-byte header (message length int16, response code, segment, security id int32, sequence uint32) and 20 levels of 16 bytes (price float64, quantity uint32, orders uint32). The subscription is JSON text: `{"RequestCode":23,"InstrumentCount":n,"InstrumentList":[{"ExchangeSegment":"NSE_EQ","SecurityId":"1333"}]}` (50 instruments per connection); the URL is `wss://depth-api-feed.dhan.co/twentydepth?token=…&clientId=…&authType=2` (no `version`).

What the owner checks on the first real depth session: the plan includes depth (806 means it does not); the byte order is little endian; the code sits at byte 3 of the header (the documentation's text says "byte 12", its table says byte 3 - the parser follows the table and the length check); the sequence number's meaning; how many packets per second arrive per instrument; the disconnect packet's layout (reason code after the header); the codes 805 and 806. Then replace these two files with sanitised captures.

## Option chain and expiry list (P14-02, ADR 0176)

| File | Endpoint | Status |
|------|----------|--------|
| `optionchain-success.json` | `POST https://api.dhan.co/v2/optionchain`, body `{UnderlyingScrip, UnderlyingSeg, Expiry}` | UNVERIFIED (written from the documentation of 2026-11-01) |
| `optionchain-expirylist.json` | `POST https://api.dhan.co/v2/optionchain/expirylist`, body without `Expiry` | UNVERIFIED |

The tests carry the same shapes inline. What the owner checks in the five-session pilot: whether "one unique request every 3 seconds" is per user or per (underlying, expiry) (the build keeps one request per 3 seconds for the whole process); the units of `implied_volatility` (percent assumed) and `theta` (per day assumed) against one hand-computed Black-Scholes value; whether a strike with no quote sends zeros or omits the fields (zero or missing bid, ask and last price are stored as NULL with a flag); the answer after 15:30; the `UnderlyingSeg` of a share (`NSE_EQ` assumed); the `IDX_I` ids of Bank Nifty and the other indices (they come from the master list, not from the documentation, which names only Nifty's `13`); and the error codes (the annexure lists `DH-901`, `DH-902`, `DH-904`, `DH-907`, `DH-908`, `DH-909`).

## Expired options, rolling ATM offset (P14-03, ADR 0177)

| File | Endpoint | Status |
|------|----------|--------|
| `rollingoption-success.json` | `POST https://api.dhan.co/v2/charts/rollingoption`, body `{exchangeSegment, interval, securityId, instrument, expiryFlag, expiryCode, strike, drvOptionType, requiredData, fromDate, toDate}` | UNVERIFIED (written from the documentation of 2026-11-01; the three bars are 09:15, 09:20 and 09:25 India time if the timestamps are Unix seconds) |

The tests carry the same shape. The owner runs `scripts/xd-probe-backfill.sh` (at most five requests, nothing stored) and records the table of ADR 0177: the meaning of `expiryCode` (1 nearest is assumed), whether code 2 accepts offsets up to 10, the timestamp zone (the first bar of a session must read 09:15 India time), the unit of `iv` (percent assumed), whether a window before an underlying's weekly era is empty or `DH-907`, bars per window, whether `strike` is the absolute strike of each bar (assumed) and whether `interval` is sent as text (assumed). Until then every field above is a guess.
