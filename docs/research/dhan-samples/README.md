# Dhan sample answers

Sanitised sample answers that the client tests are written against. **UNVERIFIED** files are written from Dhan's documentation (made-up ids and prices), not recorded from the real API; they are replaced by a recording once the owner has run the feature for real.

| File | Endpoint | Status |
|------|----------|--------|
| `quote-success.json` | `POST /v2/marketfeed/quote` | UNVERIFIED (from the documentation, until the owner's first real run of the `holdings-quotes` service) |
| `quote-partial.json` | `POST /v2/marketfeed/quote` (a security with no price, one missing) | UNVERIFIED |
| `quote-error-401.json` | `POST /v2/marketfeed/quote` (token refused) | UNVERIFIED |

What the owner checks on the first real run (ADR 0019): the `client-id` header is accepted, `last_price` and `ohlc.close` are the names in the answer, `ohlc.close` is the previous session's close (compare day change with the Dhan app), and the rate limit of 1 request/s holds.
