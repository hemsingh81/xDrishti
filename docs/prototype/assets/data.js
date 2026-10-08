/* xDrishti prototype — demo data + domain logic (aggregation, FIFO lots, XIRR).
   All prices are synthetic demo values, generated deterministically. */
(function () {
  'use strict';

  /* ---------- seeded random ---------- */
  function hash(str) {
    let h = 2166136261;
    for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
    return h >>> 0;
  }
  function rng(seed) {
    let a = typeof seed === 'string' ? hash(seed) : seed >>> 0;
    return function () {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  function gauss(r) { let u = 0, v = 0; while (u === 0) u = r(); while (v === 0) v = r(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v); }
  const round2 = (x) => Math.round(x * 100) / 100;
  const tick = (x) => Math.round(x * 20) / 20; // 0.05 tick

  /* ---------- instrument master (demo) ---------- */
  const RAW = [
    ['RELIANCE', 'Reliance Industries', 'Energy', 2900], ['TCS', 'Tata Consultancy Services', 'IT', 4100], ['INFY', 'Infosys', 'IT', 1850],
    ['HDFCBANK', 'HDFC Bank', 'Banks', 1700], ['ICICIBANK', 'ICICI Bank', 'Banks', 1250], ['SBIN', 'State Bank of India', 'Banks', 820],
    ['AXISBANK', 'Axis Bank', 'Banks', 1150], ['KOTAKBANK', 'Kotak Mahindra Bank', 'Banks', 1800], ['INDUSINDBK', 'IndusInd Bank', 'Banks', 1400],
    ['BANKBARODA', 'Bank of Baroda', 'Banks', 250], ['PNB', 'Punjab National Bank', 'Banks', 110], ['CANBK', 'Canara Bank', 'Banks', 105],
    ['FEDERALBNK', 'Federal Bank', 'Banks', 190], ['IDFCFIRSTB', 'IDFC First Bank', 'Banks', 75], ['AUBANK', 'AU Small Finance Bank', 'Banks', 640],
    ['LT', 'Larsen & Toubro', 'Capital Goods', 3600], ['SIEMENS', 'Siemens', 'Capital Goods', 6500], ['ABB', 'ABB India', 'Capital Goods', 7200],
    ['BHEL', 'Bharat Heavy Electricals', 'Capital Goods', 260], ['HAL', 'Hindustan Aeronautics', 'Capital Goods', 4500], ['BEL', 'Bharat Electronics', 'Capital Goods', 290],
    ['ITC', 'ITC', 'FMCG', 480], ['HINDUNILVR', 'Hindustan Unilever', 'FMCG', 2600], ['NESTLEIND', 'Nestle India', 'FMCG', 2450],
    ['BRITANNIA', 'Britannia Industries', 'FMCG', 5600], ['TATACONSUM', 'Tata Consumer Products', 'FMCG', 1100], ['DABUR', 'Dabur India', 'FMCG', 590],
    ['GODREJCP', 'Godrej Consumer Products', 'FMCG', 1300], ['BHARTIARTL', 'Bharti Airtel', 'Telecom', 1600], ['BAJFINANCE', 'Bajaj Finance', 'Financials', 7200],
    ['BAJAJFINSV', 'Bajaj Finserv', 'Financials', 1750], ['CHOLAFIN', 'Cholamandalam Investment', 'Financials', 1450], ['MUTHOOTFIN', 'Muthoot Finance', 'Financials', 1900],
    ['SHRIRAMFIN', 'Shriram Finance', 'Financials', 3000], ['PFC', 'Power Finance Corporation', 'Financials', 480], ['RECLTD', 'REC', 'Financials', 520],
    ['SBILIFE', 'SBI Life Insurance', 'Financials', 1650], ['HDFCLIFE', 'HDFC Life Insurance', 'Financials', 700], ['MARUTI', 'Maruti Suzuki', 'Auto', 12500],
    ['M&M', 'Mahindra & Mahindra', 'Auto', 2900], ['TATAMOTORS', 'Tata Motors', 'Auto', 950], ['HEROMOTOCO', 'Hero MotoCorp', 'Auto', 5200],
    ['EICHERMOT', 'Eicher Motors', 'Auto', 4800], ['BAJAJ-AUTO', 'Bajaj Auto', 'Auto', 10500], ['TVSMOTOR', 'TVS Motor', 'Auto', 2500],
    ['ASHOKLEY', 'Ashok Leyland', 'Auto', 230], ['TATASTEEL', 'Tata Steel', 'Metals', 152], ['JSWSTEEL', 'JSW Steel', 'Metals', 980],
    ['HINDALCO', 'Hindalco Industries', 'Metals', 690], ['VEDL', 'Vedanta', 'Metals', 450], ['SAIL', 'Steel Authority of India', 'Metals', 130],
    ['NMDC', 'NMDC', 'Metals', 230], ['JINDALSTEL', 'Jindal Steel & Power', 'Metals', 950], ['COALINDIA', 'Coal India', 'Metals', 480],
    ['ULTRACEMCO', 'UltraTech Cement', 'Cement', 11200], ['GRASIM', 'Grasim Industries', 'Cement', 2650], ['AMBUJACEM', 'Ambuja Cements', 'Cement', 600],
    ['SHREECEM', 'Shree Cement', 'Cement', 26000], ['ASIANPAINT', 'Asian Paints', 'Consumer', 2950], ['BERGEPAINT', 'Berger Paints', 'Consumer', 560],
    ['PIDILITIND', 'Pidilite Industries', 'Consumer', 3100], ['TITAN', 'Titan Company', 'Consumer', 3500], ['TRENT', 'Trent', 'Consumer', 6800],
    ['HAVELLS', 'Havells India', 'Consumer', 1850], ['VOLTAS', 'Voltas', 'Consumer', 1650], ['DIXON', 'Dixon Technologies', 'Consumer', 14500],
    ['PAGEIND', 'Page Industries', 'Consumer', 44000], ['SUNPHARMA', 'Sun Pharmaceutical', 'Pharma', 1800], ['DRREDDY', "Dr. Reddy's Laboratories", 'Pharma', 1300],
    ['CIPLA', 'Cipla', 'Pharma', 1550], ['DIVISLAB', "Divi's Laboratories", 'Pharma', 5900], ['LUPIN', 'Lupin', 'Pharma', 2100],
    ['AUROPHARMA', 'Aurobindo Pharma', 'Pharma', 1350], ['ZYDUSLIFE', 'Zydus Lifesciences', 'Pharma', 1000], ['APOLLOHOSP', 'Apollo Hospitals', 'Pharma', 7000],
    ['NTPC', 'NTPC', 'Power', 390], ['POWERGRID', 'Power Grid Corporation', 'Power', 320], ['TATAPOWER', 'Tata Power', 'Power', 420],
    ['ONGC', 'Oil & Natural Gas Corporation', 'Energy', 270], ['IOC', 'Indian Oil Corporation', 'Energy', 150], ['BPCL', 'Bharat Petroleum', 'Energy', 330],
    ['GAIL', 'GAIL India', 'Energy', 210], ['ADANIENT', 'Adani Enterprises', 'Diversified', 2600], ['ADANIPORTS', 'Adani Ports & SEZ', 'Infrastructure', 1400],
    ['WIPRO', 'Wipro', 'IT', 290], ['HCLTECH', 'HCL Technologies', 'IT', 1750], ['TECHM', 'Tech Mahindra', 'IT', 1600],
    ['LTIM', 'LTIMindtree', 'IT', 5800], ['PERSISTENT', 'Persistent Systems', 'IT', 6000], ['COFORGE', 'Coforge', 'IT', 1700],
    ['MPHASIS', 'Mphasis', 'IT', 2900], ['NAUKRI', 'Info Edge', 'IT', 1450], ['DLF', 'DLF', 'Realty', 820],
    ['GODREJPROP', 'Godrej Properties', 'Realty', 2400], ['OBEROIRLTY', 'Oberoi Realty', 'Realty', 1800], ['IRCTC', 'IRCTC', 'Services', 780],
    ['INDIGO', 'InterGlobe Aviation', 'Services', 5600], ['CONCOR', 'Container Corporation', 'Services', 780], ['JUBLFOOD', 'Jubilant FoodWorks', 'Services', 680],
    ['POLYCAB', 'Polycab India', 'Capital Goods', 6900], ['CUMMINSIND', 'Cummins India', 'Capital Goods', 3500], ['BALKRISIND', 'Balkrishna Industries', 'Auto', 2700],
    ['MRF', 'MRF', 'Auto', 135000], ['APOLLOTYRE', 'Apollo Tyres', 'Auto', 480], ['TATACHEM', 'Tata Chemicals', 'Chemicals', 1050],
    ['UPL', 'UPL', 'Chemicals', 640], ['PIIND', 'PI Industries', 'Chemicals', 3900], ['SRF', 'SRF', 'Chemicals', 2900],
    ['DEEPAKNTR', 'Deepak Nitrite', 'Chemicals', 2300], ['LICHSGFIN', 'LIC Housing Finance', 'Financials', 610],
  ];
  const INSTRUMENTS = RAW.map(([symbol, name, sector, price], i) => {
    const r = rng(symbol);
    return {
      symbol, name, sector, basePrice: price, type: 'EQ', exchange: 'NSE',
      fno: r() < 0.85, lot: 1, securityId: String(1000 + i * 37),
      turnoverCr: Math.round((price > 5000 ? 150 : 40) + r() * 900),
    };
  });
  const INDICES = [
    { symbol: 'NIFTY 50', name: 'Nifty 50 Index', sector: 'Index', basePrice: 24800, type: 'IDX', exchange: 'NSE', fno: true, lot: 1, securityId: '13', turnoverCr: 0 },
    { symbol: 'NIFTY BANK', name: 'Nifty Bank Index', sector: 'Index', basePrice: 52500, type: 'IDX', exchange: 'NSE', fno: true, lot: 1, securityId: '25', turnoverCr: 0 },
    { symbol: 'INDIA VIX', name: 'India VIX', sector: 'Index', basePrice: 14.2, type: 'IDX', exchange: 'NSE', fno: false, lot: 1, securityId: '21', turnoverCr: 0 },
  ];
  const ALL = INSTRUMENTS.concat(INDICES);
  const BY_SYMBOL = Object.fromEntries(ALL.map((x) => [x.symbol, x]));

  /* ---------- calendar ---------- */
  const DAY = 86400000;
  const iso = (d) => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
  const isWeekday = (d) => d.getDay() !== 0 && d.getDay() !== 6;
  function lastTradingDay(now = new Date()) {
    const d = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const afterClose = now.getHours() * 60 + now.getMinutes() >= 15 * 60 + 30;
    if (!afterClose || !isWeekday(d)) d.setDate(d.getDate() - 1);
    while (!isWeekday(d)) d.setDate(d.getDate() - 1);
    return d;
  }
  function nextTradingDay(from = new Date()) {
    const d = new Date(from.getFullYear(), from.getMonth(), from.getDate() + 1);
    while (!isWeekday(d)) d.setDate(d.getDate() + 1);
    return d;
  }
  const DAYS = (() => {
    const out = [];
    const d = lastTradingDay();
    while (out.length < 520) { if (isWeekday(d)) out.unshift(iso(d)); d.setDate(d.getDate() - 1); }
    return out;
  })();
  const DAY_INDEX = Object.fromEntries(DAYS.map((d, i) => [d, i]));

  /* ---------- daily price series (cached) ---------- */
  const seriesCache = {};
  // common factors: the market and each sector move together, so stocks in a sector are correlated (~0.6)
  const factorCache = {};
  function factor(name) {
    if (factorCache[name]) return factorCache[name];
    const r = rng('factor:' + name);
    factorCache[name] = Array.from({ length: DAYS.length }, () => gauss(r));
    return factorCache[name];
  }
  function daily(symbol) {
    if (seriesCache[symbol]) return seriesCache[symbol];
    const inst = BY_SYMBOL[symbol];
    const r = rng('daily:' + symbol);
    const vol = symbol === 'INDIA VIX' ? 0.045 : inst.type === 'IDX' ? 0.008 : 0.011 + r() * 0.007;
    const logs = [0];
    const mkt = factor('market'), sec = inst.type === 'IDX' ? (symbol === 'NIFTY BANK' ? factor('sector:Banks') : null) : factor('sector:' + inst.sector);
    const [wm, ws, wi] = symbol === 'INDIA VIX' ? [-0.5, 0, 0.87] : symbol === 'NIFTY 50' ? [0.97, 0, 0.25] : symbol === 'NIFTY BANK' ? [0.6, 0.7, 0.3] : [0.45, 0.65, 0.6];
    for (let i = 1; i < DAYS.length; i++) logs.push(logs[i - 1] + vol * (wm * mkt[i] + ws * (sec ? sec[i] : 0) + wi * gauss(r)) + (symbol === 'INDIA VIX' ? -0.03 * logs[i - 1] : 0));
    if (symbol !== 'INDIA VIX') {
      // bridge the path so the ~2-year change is plausible: start between 0.65× and 1.2× of today's price
      const n = logs.length - 1;
      const target = -Math.log(0.65 + r() * 0.55);
      const actual = logs[n] - logs[0];
      for (let i = 0; i <= n; i++) logs[i] += (i / n) * (target - actual);
    }
    const k = Math.log(inst.basePrice) - logs[logs.length - 1];
    const out = [];
    let prev = Math.exp(logs[0] + k);
    for (let i = 0; i < DAYS.length; i++) {
      const close = Math.exp(logs[i] + k);
      const open = prev * (1 + (r() - 0.5) * vol * 0.6);
      const high = Math.max(open, close) * (1 + r() * vol * 0.7);
      const low = Math.min(open, close) * (1 - r() * vol * 0.7);
      const p = inst.type === 'IDX' ? round2 : tick;
      out.push({ date: DAYS[i], open: p(open), high: p(high), low: p(low), close: p(close), volume: Math.round((inst.turnoverCr || 50) * 1e7 / inst.basePrice * (0.5 + r())) });
      prev = close;
    }
    seriesCache[symbol] = out;
    return out;
  }
  const closeOn = (symbol, date) => { const s = daily(symbol); const i = DAY_INDEX[date]; return (i == null ? s[s.length - 1] : s[i]).close; };
  const lastClose = (symbol) => { const s = daily(symbol); return s[s.length - 1].close; };
  const prevClose = (symbol) => { const s = daily(symbol); return s[s.length - 2].close; };

  /* ---------- intraday 1-minute bars (Brownian bridge between daily open/close) ---------- */
  // Deliberate data issues on the latest trading day, so the Data quality window has something to find.
  const ISSUES = { VEDL: 'missing', DLF: 'spike', BHEL: 'official', HAL: 'stale' };
  const issueFor = (symbol, date, fixed) => (!fixed && date === DAYS[DAYS.length - 1] ? ISSUES[symbol] || null : null);

  // 1-minute bars consistent with the official daily candle (derived 1D == official 1D), plus injected issues.
  function intraday1m(symbol, date, opts = {}) {
    const s = daily(symbol);
    const d = s[DAY_INDEX[date] ?? s.length - 1];
    date = d.date;
    const r = rng('1m:' + symbol + ':' + date);
    const n = 375;
    const path = [d.open];
    const sigma = (d.high - d.low) / Math.sqrt(n) * 0.9;
    for (let i = 1; i < n; i++) {
      const pull = (d.close - path[i - 1]) / (n - i);
      path.push(path[i - 1] + pull + sigma * gauss(r) * 0.6);
    }
    const p = BY_SYMBOL[symbol].type === 'IDX' ? round2 : tick;
    const clamp = (x) => Math.min(d.high, Math.max(d.low, p(x)));
    const [y, m, dd] = date.split('-').map(Number);
    const bars = [];
    for (let i = 0; i < n; i++) {
      const o = clamp(i === 0 ? d.open : path[i - 1]);
      const c = clamp(i === n - 1 ? d.close : path[i]);
      const h = clamp(Math.max(o, c) + Math.abs(gauss(r)) * sigma * 0.4);
      const l = clamp(Math.min(o, c) - Math.abs(gauss(r)) * sigma * 0.4);
      bars.push({ t: new Date(y, m - 1, dd, 9, 15 + i).getTime(), open: o, high: Math.max(h, o, c), low: Math.min(l, o, c), close: c, volume: 0.4 + r() * 1.2 });
    }
    // the day's extremes must appear in the minute data
    let hi = 0, lo = 0;
    bars.forEach((b, i) => { if (b.high > bars[hi].high) hi = i; if (b.low < bars[lo].low) lo = i; });
    bars[hi].high = d.high; bars[lo].low = d.low;
    // volumes add up exactly to the daily volume
    const tw = bars.reduce((a, b) => a + b.volume, 0);
    let acc = 0;
    bars.forEach((b, i) => { b.volume = i === n - 1 ? d.volume - acc : Math.floor(d.volume * b.volume / tw); acc += b.volume; });
    const issue = issueFor(symbol, date, opts.fixed);
    if (issue === 'missing') bars.splice(118, 7);
    if (issue === 'spike') { const b = bars[200]; b.high = p(b.high * 1.045); b.close = p(b.close * 1.04); bars[201].open = p(bars[201].open * 1.04); }
    return bars;
  }
  // "Official" daily candle as received from the broker (may disagree with derived 1D when data is bad)
  function officialDaily(symbol, date, fixed) {
    const d = daily(symbol)[DAY_INDEX[date] ?? DAYS.length - 1];
    return issueFor(symbol, date, fixed) === 'official' ? { ...d, close: tick(d.close * 1.003) } : d;
  }
  const hasStaleAggregate = (symbol, date, fixed) => issueFor(symbol, date, fixed) === 'stale';
  function resultsDate(symbol) {
    const r = rng('results:' + symbol);
    const d = new Date(); d.setDate(d.getDate() + Math.floor(r() * 75) - 5);
    return iso(d);
  }

  /* ---------- aggregation from 1-minute bars (same rules for CSV and broker data) ---------- */
  function aggregate(bars, tf, anchor = '09:15') {
    if (tf === '1m') return bars.slice();
    const [ah, am] = anchor.split(':').map(Number);
    const out = [];
    let cur = null, key = null;
    const keyOf = (t) => {
      const d = new Date(t);
      if (tf === '1D') return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
      if (tf === '1W') { const x = new Date(d.getFullYear(), d.getMonth(), d.getDate()); x.setDate(x.getDate() - ((x.getDay() + 6) % 7)); return x.getTime(); }
      if (tf === '1M') return new Date(d.getFullYear(), d.getMonth(), 1).getTime();
      const mins = parseInt(tf, 10) * (tf.endsWith('h') ? 60 : 1);
      const start = new Date(d.getFullYear(), d.getMonth(), d.getDate(), ah, am).getTime();
      const idx = Math.floor((t - start) / 60000 / mins);
      return start + idx * mins * 60000;
    };
    for (const b of bars) {
      const k = keyOf(b.t);
      if (k !== key) { if (cur) out.push(cur); key = k; cur = { t: k, open: b.open, high: b.high, low: b.low, close: b.close, volume: b.volume, n: 1 }; }
      else { cur.high = Math.max(cur.high, b.high); cur.low = Math.min(cur.low, b.low); cur.close = b.close; cur.volume += b.volume; cur.n++; }
    }
    if (cur) out.push(cur);
    return out;
  }

  /* ---------- XIRR (Newton–Raphson with bisection fallback) ---------- */
  function xirr(flows) {
    if (flows.length < 2) return null;
    const pos = flows.some((f) => f.amount > 0), neg = flows.some((f) => f.amount < 0);
    if (!pos || !neg) return null;
    const t0 = Math.min(...flows.map((f) => f.t));
    const yrs = flows.map((f) => (f.t - t0) / (365 * DAY));
    const npv = (r) => flows.reduce((s, f, i) => s + f.amount / Math.pow(1 + r, yrs[i]), 0);
    const d = (r) => flows.reduce((s, f, i) => s - yrs[i] * f.amount / Math.pow(1 + r, yrs[i] + 1), 0);
    let r = 0.1;
    for (let i = 0; i < 50; i++) {
      const v = npv(r), dv = d(r);
      if (!isFinite(v) || !isFinite(dv) || dv === 0) break;
      const nr = r - v / dv;
      if (Math.abs(nr - r) < 1e-9) return nr > -0.9999 ? nr : null;
      r = nr;
      if (r <= -0.9999) break;
    }
    let lo = -0.9999, hi = 10;
    if (npv(lo) * npv(hi) > 0) return null;
    for (let i = 0; i < 300; i++) { const m = (lo + hi) / 2; if (npv(lo) * npv(m) <= 0) hi = m; else lo = m; }
    return (lo + hi) / 2;
  }

  /* ---------- FIFO lots → positions (long and short) ---------- */
  // A position is one "episode": it opens when quantity leaves 0 and closes when it returns to 0.
  // dir = 1 (long, opened by BUY) or -1 (short, opened by SELL). Lot cost = net cost (long) or net proceeds (short) per unit.
  function buildPositions(transactions, ltpOf) {
    const groups = {};
    const open = {};
    const sorted = transactions.slice().sort((a, b) => a.t - b.t);
    let ep = 0;
    for (const tx of sorted) {
      const base = tx.account + '|' + tx.symbol + '|' + tx.bucket;
      let g = open[base];
      if (!g) {
        const key = base + '|' + ep++;
        g = groups[key] = open[base] = { key, account: tx.account, symbol: tx.symbol, bucket: tx.bucket, dir: tx.side === 'BUY' ? 1 : -1,
          lots: [], txs: [], flows: [], realised: 0, charges: 0, invested: 0, planItem: null };
      }
      g.txs.push(tx);
      g.charges += tx.charges;
      if (tx.planItem) g.planItem = tx.planItem;
      const opening = (g.dir === 1) === (tx.side === 'BUY');
      if (opening) {
        const per = g.dir === 1 ? (tx.qty * tx.price + tx.charges) / tx.qty : (tx.qty * tx.price - tx.charges) / tx.qty;
        g.lots.push({ qty: tx.qty, cost: per, t: tx.t });
        g.invested += tx.qty * per;
        g.flows.push({ t: tx.t, amount: -tx.qty * per });
      } else {
        let q = tx.qty;
        const per = g.dir === 1 ? (tx.qty * tx.price - tx.charges) / tx.qty : (tx.qty * tx.price + tx.charges) / tx.qty;
        let back = 0;
        while (q > 0 && g.lots.length) {
          const lot = g.lots[0];
          const take = Math.min(q, lot.qty);
          g.realised += take * g.dir * (per - lot.cost);
          back += g.dir === 1 ? take * per : take * (2 * lot.cost - per);
          lot.qty -= take; q -= take;
          if (lot.qty === 0) g.lots.shift();
        }
        g.flows.push({ t: tx.t, amount: back });
        if (!g.lots.length) delete open[base];
      }
    }
    const now = Date.now();
    return Object.values(groups).map((g) => {
      const qty = g.lots.reduce((s, l) => s + l.qty, 0);
      const remainingCost = g.lots.reduce((s, l) => s + l.qty * l.cost, 0);
      const ltp = ltpOf(g.symbol);
      const unrealised = g.dir * (qty * ltp - remainingCost);
      const value = remainingCost + unrealised; // long: qty × LTP; short: committed notional + P&L
      const flows = g.flows.slice();
      if (qty > 0) flows.push({ t: now, amount: value });
      const first = (g.lots[0] || {}).t || g.txs[0].t;
      const last = g.txs[g.txs.length - 1].t;
      return {
        ...g, qty, avgCost: qty ? remainingCost / qty : 0, ltp, value, exposure: qty * ltp, remainingCost,
        unrealised, flows, firstAdded: g.txs[0].t, oldestOpenLot: qty ? first : null, closedAt: qty ? null : last,
        daysHeld: Math.max(0, Math.round(((qty ? now : last) - g.txs[0].t) / DAY)),
        status: qty > 0 ? 'open' : 'closed',
      };
    });
  }

  /* ---------- demo seed ---------- */
  function seed() {
    const r = rng('seed-v1');
    const now = Date.now();
    const dayT = (date, h = 10, m = 5) => { const [y, mo, d] = date.split('-').map(Number); return new Date(y, mo - 1, d, h, m).getTime(); };
    const ch = (v) => round2(Math.max(20, v * 0.0012));
    const tx = [];
    let id = 1;
    const add = (account, symbol, side, dayIdx, qty, bucket, planItem) => {
      const date = DAYS[Math.max(0, Math.min(DAYS.length - 1, dayIdx))];
      const price = closeOn(symbol, date);
      tx.push({ id: 'TX' + id++, account, symbol, side, date, t: dayT(date, 9 + Math.floor(r() * 5), Math.floor(r() * 59)), qty, price, charges: ch(qty * price), bucket, source: 'broker', planItem: planItem || null });
    };
    // long-term holdings (account: dhan_longterm, bucket MyLongTerm)
    const lt = ['HDFCBANK', 'TCS', 'RELIANCE', 'ITC', 'LT', 'ASIANPAINT', 'TITAN', 'SUNPHARMA', 'BHARTIARTL', 'ULTRACEMCO'];
    lt.forEach((s, i) => {
      const n = 1 + Math.floor(r() * 3);
      let start = 20 + Math.floor(r() * 300);
      for (let k = 0; k < n; k++) {
        const amt = 40000 + r() * 90000;
        add('dhan_longterm', s, 'BUY', start, Math.max(1, Math.round(amt / closeOn(s, DAYS[start]))), 'MyLongTerm');
        start += 30 + Math.floor(r() * 120);
      }
      if (i % 4 === 1) { const q = tx.filter((t) => t.symbol === s && t.side === 'BUY').reduce((a, t) => a + t.qty, 0); add('dhan_longterm', s, 'SELL', DAYS.length - 40 - i * 5, Math.max(1, Math.floor(q / 3)), 'MyLongTerm'); }
    });
    // MyChoice bucket holdings in main account
    ['PERSISTENT', 'TRENT', 'DIXON'].forEach((s) => add('dhan_main', s, 'BUY', DAYS.length - 60 - Math.floor(r() * 120), Math.max(1, Math.round(60000 / closeOn(s, DAYS[DAYS.length - 100]))), 'MyChoice'));
    // closed swing trades (trading bucket)
    const swingSyms = ['TATASTEEL', 'SBIN', 'ICICIBANK', 'AXISBANK', 'JSWSTEEL', 'HINDALCO', 'TATAMOTORS', 'BEL', 'NTPC', 'COALINDIA', 'INFY', 'WIPRO', 'M&M', 'BHEL', 'DLF', 'VEDL'];
    for (let k = 0; k < 22; k++) {
      const s = swingSyms[k % swingSyms.length];
      const acc = k % 3 === 0 ? 'dhan_main' : 'dhan_swing';
      const d0 = DAYS.length - 15 - Math.floor(r() * 220);
      const q = Math.max(1, Math.round(50000 / closeOn(s, DAYS[d0])));
      add(acc, s, 'BUY', d0, q, 'Trading', 'PLAN-' + DAYS[d0] + '-' + k);
      add(acc, s, 'SELL', d0 + 1 + Math.floor(r() * 4), q, 'Trading', 'PLAN-' + DAYS[d0] + '-' + k);
    }
    // open swing positions
    [['SBIN', 'dhan_swing', 2], ['JSWSTEEL', 'dhan_swing', 1], ['BEL', 'dhan_main', 3]].forEach(([s, acc, ago], k) => {
      const d0 = DAYS.length - ago;
      add(acc, s, 'BUY', d0, Math.max(1, Math.round(60000 / closeOn(s, DAYS[d0]))), 'Trading', 'PLAN-OPEN-' + k);
    });

    const accounts = [
      { id: 'dhan_main', name: 'Dhan · Main', broker: 'Dhan', clientId: '1100042137', credRef: 'xdrishti/dhan/main',
        roles: { data: { enabled: true, primary: true, standby: false }, trading: { enabled: true, mode: 'paper', profile: 'standard', capital: 1000000 }, portfolio: { enabled: true, defaultBucket: 'Trading' } },
        token: { status: 'valid', expiresAt: now + 20 * 3600 * 1000 }, lastSync: now - 3 * 3600 * 1000 },
      { id: 'dhan_longterm', name: 'Dhan · Long-term', broker: 'Dhan', clientId: '1100042555', credRef: 'xdrishti/dhan/longterm',
        roles: { data: { enabled: false, primary: false, standby: false }, trading: { enabled: false, mode: 'paper', profile: 'standard', capital: 0 }, portfolio: { enabled: true, defaultBucket: 'MyLongTerm' } },
        token: { status: 'valid', expiresAt: now + 18 * 3600 * 1000 }, lastSync: now - 3 * 3600 * 1000 },
      { id: 'dhan_swing', name: 'Dhan · Swing', broker: 'Dhan', clientId: '1100042901', credRef: 'xdrishti/dhan/swing',
        roles: { data: { enabled: true, primary: false, standby: true }, trading: { enabled: true, mode: 'manual_live', profile: 'conservative', capital: 300000 }, portfolio: { enabled: true, defaultBucket: 'Trading' } },
        token: { status: 'expiring', expiresAt: now + 2 * 3600 * 1000 }, lastSync: now - 3 * 3600 * 1000 },
    ];

    const tracked = {};
    const trackedList = ['RELIANCE', 'TCS', 'INFY', 'HDFCBANK', 'ICICIBANK', 'SBIN', 'AXISBANK', 'KOTAKBANK', 'LT', 'ITC', 'BHARTIARTL', 'BAJFINANCE',
      'MARUTI', 'M&M', 'TATAMOTORS', 'TATASTEEL', 'JSWSTEEL', 'HINDALCO', 'ULTRACEMCO', 'ASIANPAINT', 'TITAN', 'TRENT', 'DIXON', 'SUNPHARMA', 'NTPC',
      'COALINDIA', 'BEL', 'BHEL', 'WIPRO', 'PERSISTENT', 'DLF', 'VEDL', 'TATAPOWER', 'HAL', 'POLYCAB', 'CHOLAFIN', 'NIFTY 50', 'NIFTY BANK', 'INDIA VIX'];
    trackedList.forEach((s, i) => { tracked[s] = { since: DAYS[DAYS.length - 200 + (i % 50)], dataFrom: DAYS[0], dataTo: DAYS[DAYS.length - 1], status: 'ready', progress: 100 }; });

    const baskets = [
      { id: 'b-mychoice', name: 'MyChoice', type: 'manual', purpose: 'trading', color: '#6366f1', styles: ['intraday', 'swing'], members: ['TRENT', 'DIXON', 'PERSISTENT', 'TATAMOTORS', 'BEL', 'HAL', 'POLYCAB', 'CHOLAFIN'], createdAt: now - 200 * DAY },
      { id: 'b-longterm', name: 'MyLongTerm', type: 'manual', purpose: 'long_term', color: '#10b981', styles: ['swing'], members: lt.slice(), createdAt: now - 400 * DAY },
      { id: 'b-best', name: 'Best stocks', type: 'manual', purpose: 'trading', color: '#f59e0b', styles: ['intraday', 'swing'], members: ['RELIANCE', 'ICICIBANK', 'SBIN', 'AXISBANK', 'TATASTEEL', 'JSWSTEEL', 'HINDALCO', 'M&M', 'BHARTIARTL', 'NTPC', 'COALINDIA', 'INFY'], createdAt: now - 150 * DAY },
      { id: 'b-banks', name: 'Banks (rule)', type: 'rule', purpose: 'watch', color: '#8b5cf6', styles: ['swing'], rule: { sectors: ['Banks'], fnoOnly: true, minPrice: 100, trackedOnly: true }, members: [], createdAt: now - 90 * DAY },
    ];

    return {
      version: 1, createdAt: now, lastSeen: now,
      settings: { maxNewEntries: 3, maxPlanItems: 5, maxOpenPositions: 5, riskPct: 0.5, reviewCutoff: '09:05', unreviewedPolicy: 'lapse', idleTimeoutMin: 30, holdingsRefreshMin: 5, demoMarketOpen: true },
      accounts, tracked, baskets, transactions: tx, buckets: ['Trading', 'MyLongTerm', 'MyChoice'],
      plan: null, suggestions: [], imports: [], audit: [], notifications: [], labRuns: [], services: null,
    };
  }

  /* ---------- historical closed trades for reports (with decisions) ---------- */
  const HOUR_EDGE = {
    ORB: { 9: 0.14, 10: 0.05, 11: -0.06, 12: -0.14, 13: -0.1, 14: -0.12 }, VWAP_PULLBACK: { 9: -0.1, 10: 0.09, 11: 0.07, 12: -0.04, 13: 0.03, 14: -0.06 },
    EMA_PULLBACK: { 9: -0.06, 10: 0.07, 11: 0.04, 12: -0.09, 13: 0.06, 14: 0.02 }, PDH_PDL_BREAK: { 9: 0.03, 10: 0.06, 11: -0.03, 12: -0.11, 13: 0.0, 14: 0.04 },
  };
  const BAD_WEEKS = [[5, 8], [11, 13], [17, 19], [22, 23], [27, 30], [33, 35], [40, 42], [47, 49]];
  const HOUR_LABEL = { 9: '09:15–10:00', 10: '10:00–11:00', 11: '11:00–12:00', 12: '12:00–13:00', 13: '13:00–14:00', 14: '14:00–15:00' };
  function reportTrades(baskets) {
    const r = rng('trades-v1');
    const strategies = [['ORB', ['5m', '15m', '30m'], 'intraday'], ['VWAP_PULLBACK', ['5m', '15m'], 'intraday'], ['EMA_PULLBACK', ['15m', '30m'], 'intraday'],
      ['PDH_PDL_BREAK', ['15m', '1h'], 'intraday'], ['BASE_BREAKOUT', ['1D'], 'swing'], ['TREND_PULLBACK', ['1h', '1D'], 'swing'], ['RSI2_REVERSION', ['1D'], 'swing']];
    const exits = ['FIXED_RR 1:2', 'FIXED_RR 1:3', 'PARTIAL_TRAIL', 'ATR_TRAIL', 'PYRAMID'];
    const edge = { ORB: 0.18, VWAP_PULLBACK: 0.08, EMA_PULLBACK: 0.12, PDH_PDL_BREAK: -0.04, BASE_BREAKOUT: 0.28, TREND_PULLBACK: 0.2, RSI2_REVERSION: 0.1 };
    const bEdge = { 'MyChoice': 0.06, 'Best stocks': 0.1, 'MyLongTerm': 0.02, 'Banks (rule)': -0.02 };
    const out = [];
    const rh = rng('hours-v1'), rm = rng('meta-v1'), rs = rng('slip-v1'), rt = rng('tags-v1');
    const tradeBaskets = baskets.filter((b) => b.type !== 'system' && b.members.length);
    for (let i = 0; i < 360; i++) {
      const [strategy, tfs, style] = strategies[Math.floor(r() * strategies.length)];
      const b = tradeBaskets[Math.floor(r() * tradeBaskets.length)];
      const symbol = b.members[Math.floor(r() * b.members.length)];
      const side = style === 'swing' && r() < 0.75 ? 'BUY' : r() < 0.55 ? 'BUY' : 'SELL';
      const exit = exits[Math.floor(r() * exits.length)];
      const back = 2 + Math.floor(r() * 250);
      const date = DAYS[DAYS.length - back];
      const wk = Math.floor((255 - back) / 5); // week of the learning year (matches the Learning journey)
      const phase = BAD_WEEKS.some(([a, b]) => wk >= a - 1 && wk < b) ? -0.25 : 0.08; // edges come and go in clusters
      const hour = style === 'swing' ? 15 : 9 + Math.floor(rh() * 6); // entry hour; edges differ by time of day
      const e = (edge[strategy] || 0) + (bEdge[b.name] || 0) + (side === 'SELL' ? -0.05 : 0) + ((HOUR_EDGE[strategy] || {})[hour] || 0) + phase;
      // realistic payoff: 40–55% winners, losses ≈ −1R incl. slippage, expectancy typically +0.1 … +0.3R
      const win = r() < 0.43 + e * 0.5;
      const R = win ? round2(0.5 + r() * (exit.includes('1:3') || exit === 'PYRAMID' ? 2.3 : 1.4)) : round2(-(0.75 + r() * 0.35));
      const decisionRoll = r();
      const decision = decisionRoll < 0.72 ? 'approved' : decisionRoll < 0.82 ? 'modified' : decisionRoll < 0.93 ? 'rejected' : 'lapsed';
      const riskAmt = 5000;
      const account = style === 'swing' && r() < 0.5 ? 'dhan_swing' : 'dhan_main';
      const pWin = Math.max(0.3, Math.min(0.75, 0.43 + e * 0.5 + (r() - 0.5) * 0.16));
      const mae = win ? -round2(r() * 0.85) : -round2(0.9 + r() * 0.25);
      const mfe = win ? round2(R + r() * 0.8) : round2(r() * 1.2);
      const wd = new Date(date).getDay();
      const regime = ['UP_LOW_VOL', 'UP_HIGH_VOL', 'RANGE_LOW_VOL', 'RANGE_HIGH_VOL', 'DOWN_HIGH_VOL'][Math.floor(r() * 5)];
      const slipR = round2(0.015 + rs() * 0.06); // fills vs plan prices; R below is graded on fills
      const meta = round2(Math.max(0.05, Math.min(0.95, 0.59 + (win ? 0.017 : -0.017) + (pWin - 0.5) * 0.4 + (rm() - 0.5) * 0.3)));
      const tags = [];
      if (!win) {
        if (mae < -1.08) tags.push('Gap through stop');
        if (mfe >= 0.8) tags.push('Gave back open profit');
        if (style === 'intraday' && hour >= 13) tags.push('Late entry');
        if (['ORB', 'PDH_PDL_BREAK', 'BASE_BREAKOUT'].includes(strategy) && /HIGH_VOL/.test(regime)) tags.push('Against regime');
        if (slipR > 0.065) tags.push('High slippage');
        if (rt() < 0.08) tags.push('News / results');
        if (!tags.length) tags.push('Normal loss (variance)');
      }
      out.push({ id: 'T' + (1000 + i), date, symbol, basket: b.name, strategy, tf: tfs[Math.floor(r() * tfs.length)], style, side, exit, account,
        R, pnl: Math.round(R * riskAmt - 180), decision, taken: decision === 'approved' || decision === 'modified',
        regime, slipR, planR: round2(R + slipR), meta, tags,
        pWin: round2(pWin), mae, mfe, hour, weekday: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][wd], month: date.slice(0, 7),
        sector: BY_SYMBOL[symbol].sector, reserve: r() < 0.12 });
    }
    return out.sort((a, b) => a.date.localeCompare(b.date));
  }

  /* ---------- smart rules (doc 09 §14): correlation, market gate, equity throttle, meta-labelling, entry windows ---------- */
  const SMART_DEFAULTS = {
    throttle: { enabled: true, window: 20, cut: 0.5 },
    gate: { enabled: true, vixReduce: 18, vixNoTrade: 22, breadthMin: 0.3, simulate: false },
    meta: { enabled: true, minTake: 0.5 },
    windows: { enabled: true, minTrades: 8 },
    corr: { enabled: true, max: 0.6, lookback: 60 },
    digest: { weekday: 6 },
  };
  function smartCfg(state) {
    state.smart = state.smart || {};
    for (const k of Object.keys(SMART_DEFAULTS)) state.smart[k] = Object.assign({}, SMART_DEFAULTS[k], state.smart[k] || {});
    return state.smart;
  }
  const corrCache = {};
  function corr(a, b, n = 60) {
    const key = [a, b].sort().join('|') + n;
    if (key in corrCache) return corrCache[key];
    const ra = daily(a).slice(-n - 1), rb = daily(b).slice(-n - 1);
    const x = [], y = [];
    for (let i = 1; i < ra.length; i++) { x.push(Math.log(ra[i].close / ra[i - 1].close)); y.push(Math.log(rb[i].close / rb[i - 1].close)); }
    const mx = x.reduce((s, v) => s + v, 0) / x.length, my = y.reduce((s, v) => s + v, 0) / y.length;
    let sxy = 0, sxx = 0, syy = 0;
    for (let i = 0; i < x.length; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) ** 2; syy += (y[i] - my) ** 2; }
    return (corrCache[key] = round2(sxy / Math.sqrt(sxx * syy)));
  }
  function breadth(symbols) {
    const eq = symbols.filter((sym) => BY_SYMBOL[sym] && BY_SYMBOL[sym].type === 'EQ');
    const above = eq.filter((sym) => { const d = daily(sym); const ma = d.slice(-20).reduce((s, x) => s + x.close, 0) / 20; return d[d.length - 1].close > ma; });
    return eq.length ? above.length / eq.length : 0.5;
  }
  function upcomingEvents(forDate) {
    const base = new Date(forDate);
    const at = (days) => iso(new Date(base.getTime() + days * DAY));
    return [{ date: at(9), name: 'RBI monetary policy decision', impact: 'high' }, { date: at(15), name: 'US Fed rate decision (overnight gap risk)', impact: 'medium' }];
  }
  // Market gate: learned "no-trade" conditions. Status: normal | reduced | no-trade
  function marketGate(state, forDate) {
    const g = smartCfg(state).gate;
    const vix = g.simulate ? 23.4 : lastClose('INDIA VIX');
    const br = g.simulate ? 0.22 : breadth(Object.keys(state.tracked));
    const events = upcomingEvents(forDate).concat(g.simulate ? [{ date: forDate, name: 'RBI monetary policy decision', impact: 'high' }] : []);
    const tomorrow = events.filter((e) => e.date === forDate && e.impact === 'high');
    const factors = [
      { k: 'vix', label: 'INDIA VIX', value: vix, text: `${vix.toFixed(1)} (reduce > ${g.vixReduce}, no-trade > ${g.vixNoTrade})`, level: vix > g.vixNoTrade ? 'no-trade' : vix > g.vixReduce ? 'reduced' : 'normal', evidence: 'VIX > 22 days averaged −0.28R per trade (94 trades, 3 years)' },
      { k: 'breadth', label: 'Breadth (tracked above 20-DMA)', value: br, text: `${Math.round(br * 100)}% (reduce below ${Math.round(g.breadthMin * 100)}%)`, level: br < g.breadthMin ? 'reduced' : 'normal', evidence: 'Breadth < 30% days averaged −0.12R (210 trades)' },
      { k: 'events', label: 'High-impact events tomorrow', value: tomorrow.length, text: tomorrow.length ? tomorrow.map((e) => e.name).join(', ') : 'none', level: tomorrow.length ? 'no-trade' : 'normal', evidence: 'RBI / Budget days averaged −0.19R (41 trades)' },
    ];
    const order = ['normal', 'reduced', 'no-trade'];
    const status = !g.enabled ? 'normal' : factors.reduce((m, f) => (order.indexOf(f.level) > order.indexOf(m) ? f.level : m), 'normal');
    return { status, factors, events, enabled: g.enabled };
  }
  // Equity-curve throttle: risk × cut while the system's own equity is below its N-trade average
  function equityThrottle(trades, cfg) {
    const list = trades.filter((t) => t.taken).sort((a, b) => a.date.localeCompare(b.date));
    let eq = 0, eqW = 0, peak = 0, peakW = 0, dd = 0, ddW = 0, mult = 1;
    const hist = [], series = [];
    for (const t of list) {
      eq += t.R; eqW += t.R * mult;
      peak = Math.max(peak, eq); peakW = Math.max(peakW, eqW); dd = Math.min(dd, eq - peak); ddW = Math.min(ddW, eqW - peakW);
      hist.push(eq);
      const w = hist.slice(-cfg.window); const ma = w.reduce((s, v) => s + v, 0) / w.length;
      series.push({ date: t.date, eq: round2(eq), ma: round2(ma), mult, eqW: round2(eqW) });
      mult = cfg.enabled && hist.length >= cfg.window && eq < ma ? cfg.cut : 1;
    }
    const throttled = series.filter((x) => x.mult < 1).length;
    return { series, mult, total: round2(eq), totalW: round2(eqW), dd: round2(dd), ddW: round2(ddW), throttledPct: series.length ? throttled / series.length : 0 };
  }
  // Learned entry windows per intraday strategy — from the walk-forward backtest (thousands of trades), not only live trades
  const BASE_EDGE = { ORB: 0.18, VWAP_PULLBACK: 0.08, EMA_PULLBACK: 0.12, PDH_PDL_BREAK: -0.04 };
  function learnedWindows(trades, minTrades = 8) {
    const out = {};
    for (const st of Object.keys(HOUR_EDGE)) {
      const r = rng('wf-windows:' + st);
      const cells = Object.keys(HOUR_LABEL).map((h) => { const n = 140 + Math.floor(r() * 160); return { h: +h, n, exp: round2(BASE_EDGE[st] + HOUR_EDGE[st][h] + gauss(r) * 1.1 / Math.sqrt(n)) }; });
      const live = trades.filter((t) => t.taken && t.strategy === st && t.style === 'intraday');
      cells.forEach((c) => { const l = live.filter((t) => t.hour === c.h); c.live = l.length; c.liveExp = l.length ? round2(l.reduce((s, t) => s + t.R, 0) / l.length) : null; });
      const ok = cells.filter((c) => c.n >= minTrades * 10 && c.exp > 0).sort((a, b) => b.exp - a.exp);
      out[st] = { cells, best: ok[0] || null, label: ok[0] ? HOUR_LABEL[ok[0].h] : null };
    }
    return out;
  }
  const metaOf = (it) => round2(Math.max(0.05, Math.min(0.95, 0.36 + (it.pWin - 0.46) * 1.6 + rng('meta:' + it.id)() * 0.3)));
  const sizeOf = (m) => Math.max(0.5, Math.min(1.5, Math.round((0.5 + (m - 0.4) * 2.5) * 4) / 4));

  /* ---------- next-day proposed plan ---------- */
  function proposePlan(state, forDate) {
    const r = rng('plan:' + forDate + ':' + state.baskets.map((b) => b.members.length).join(','));
    const scope = state.baskets.filter((b) => b.purpose === 'trading').flatMap((b) => b.members.map((s) => [s, b.name]));
    const seen = new Set();
    const pool = scope.filter(([s]) => (seen.has(s) ? false : seen.add(s)) && state.tracked[s]);
    const strategies = [['ORB', '15m', 'INTRADAY'], ['VWAP_PULLBACK', '5m', 'INTRADAY'], ['EMA_PULLBACK', '30m', 'INTRADAY'], ['BASE_BREAKOUT', '1D', 'SWING'], ['TREND_PULLBACK', '1h', 'SWING'], ['PDH_PDL_BREAK', '15m', 'INTRADAY']];
    const reasonsBank = ['NR7 yesterday', 'Relative volume 1.8×', '1h & 1D uptrend', 'Sector strongest of 11', 'Closed above 20-day high', 'Pullback to rising 20 EMA',
      'Gap within 0.3 ATR', 'Regime UP_LOW_VOL favours breakouts', 'Inside day after expansion', 'RS vs Nifty at 3-month high', 'Room to next resistance 2.6R'];
    const trading = state.accounts.filter((a) => a.roles.trading.enabled);
    const sm = smartCfg(state);
    const hist = reportTrades(state.baskets);
    const gate = marketGate(state, forDate);
    const thr = equityThrottle(hist, sm.throttle);
    const wins = learnedWindows(hist, sm.windows.minTrades);
    const riskMult = thr.mult * (gate.status === 'reduced' ? 0.75 : 1);
    const items = [];
    const picks = pool.sort(() => r() - 0.5).slice(0, 8);
    picks.forEach(([symbol, basket], i) => {
      const [strategy, tf, style] = strategies[Math.floor(r() * strategies.length)];
      const side = style === 'SWING' || r() < 0.65 ? 'BUY' : 'SELL';
      const ltp = lastClose(symbol);
      const atr = ltp * (0.008 + r() * 0.01);
      const trigger = tick(side === 'BUY' ? ltp + atr * 0.4 : ltp - atr * 0.4);
      const stop = tick(side === 'BUY' ? trigger - atr * (0.9 + r() * 0.5) : trigger + atr * (0.9 + r() * 0.5));
      const risk = Math.abs(trigger - stop);
      const rr = [2, 2, 2.5, 3][Math.floor(r() * 4)];
      const target = tick(side === 'BUY' ? trigger + risk * rr : trigger - risk * rr);
      const pWin = round2(0.46 + r() * 0.16);
      const expR = round2(pWin * rr * 0.92 - (1 - pWin) - 0.06);
      const exit = rr === 3 ? 'PYRAMID +1R/+2R, ATR trail' : r() < 0.5 ? `FIXED_RR 1:${rr}` : `50% at 1R, trail ATR×2 (target ${rr}R)`;
      const alloc = trading.filter((a) => (a.roles.trading.styles || ['intraday', 'swing']).includes(style.toLowerCase())).map((a) => {
        const t = a.roles.trading;
        const prof = Object.assign({ riskPct: state.settings.riskPct }, (state.riskProfiles || []).find((p) => p.id === t.profile) || {}, t.overrides || {});
        const cap = t.capitalSource === 'broker_funds' && a.funds ? Math.min(a.funds.available, t.capital || Infinity) : t.capital || 0;
        const rp = prof.riskPct * riskMult;
        return { account: a.id, qty: Math.max(1, Math.floor((cap * rp / 100) / risk)), riskAmt: Math.round(cap * rp / 100) };
      });
      items.push({
        id: `P-${forDate}-${i + 1}`, symbol, basket, strategy, tf, style, side, trigger, stop, target, rr, exit, pWin, expR,
        window: style === 'INTRADAY' ? (sm.windows.enabled && wins[strategy] && wins[strategy].label ? wins[strategy].label : '09:30–11:30') : 'next session',
        windowLearned: style === 'INTRADAY' && sm.windows.enabled && !!(wins[strategy] && wins[strategy].label), squareOff: style === 'INTRADAY' ? '15:15' : 'max 5 sessions',
        cancelIf: `open beyond ${tick(side === 'BUY' ? trigger + atr * 0.5 : trigger - atr * 0.5)} or stop touched before trigger`,
        reasons: reasonsBank.slice().sort(() => r() - 0.5).slice(0, 3), samples: 60 + Math.floor(r() * 220),
        alloc, reserve: false, status: 'proposed', decision: null,
      });
    });
    // meta-labelling: a second model decides whether to take the signal and how big
    items.forEach((it) => {
      it.meta = metaOf(it); it.sizeMult = sm.meta.enabled ? sizeOf(it.meta) : 1;
      if (sm.meta.enabled && it.sizeMult !== 1) it.alloc.forEach((a) => { a.qty = Math.max(1, Math.round(a.qty * it.sizeMult)); a.riskAmt = Math.round(a.riskAmt * it.sizeMult); });
      if (sm.meta.enabled && it.meta < sm.meta.minTake) it.filtered = `Meta-model: skip (P(take) ${Math.round(it.meta * 100)}% < ${Math.round(sm.meta.minTake * 100)}%)`;
    });
    items.sort((a, b) => b.expR - a.expR);
    // correlation-aware selection: the same bet twice (with a better ticket or an open position) is demoted
    const openSyms = [...new Set(buildPositions(state.transactions, lastClose).filter((p) => p.status === 'open' && p.bucket === 'Trading').map((p) => p.symbol))];
    const chosen = [];
    for (const it of items) {
      if (it.filtered) continue;
      const peers = chosen.map((x) => x.symbol).concat(openSyms).filter((x) => x !== it.symbol);
      const c = peers.map((p) => [p, corr(it.symbol, p, sm.corr.lookback)]).sort((a, b) => b[1] - a[1])[0];
      if (c) { it.corrWith = c[0]; it.corr = c[1]; }
      if (sm.corr.enabled && c && c[1] > sm.corr.max) { it.filtered = `Same bet as ${c[0]} (60-day correlation ${c[1].toFixed(2)} > ${sm.corr.max})`; continue; }
      chosen.push(it);
    }
    const maxItems = gate.status === 'no-trade' ? 0 : gate.status === 'reduced' ? Math.ceil(state.settings.maxPlanItems / 2) : state.settings.maxPlanItems;
    const ordered = items.filter((x) => !x.filtered).concat(items.filter((x) => x.filtered));
    ordered.forEach((it, i) => { it.rank = i + 1; it.reserve = !!it.filtered || i >= maxItems; });
    const gateText = gate.status === 'no-trade' ? `NO-TRADE DAY: ${gate.factors.filter((f) => f.level === 'no-trade').map((f) => f.label + ' ' + f.text).join('; ')} — no tickets proposed, tickets kept on the reserve list for reference. `
      : gate.status === 'reduced' ? `Reduced day (${gate.factors.filter((f) => f.level !== 'normal').map((f) => f.label).join(', ')}): max ${maxItems} tickets, risk × 0.75. ` : '';
    return { date: forDate, generatedAt: Date.now(), items: ordered, gate, throttle: { mult: thr.mult, riskMult }, maxItems, briefing: gateText + (thr.mult < 1 ? `Equity-curve throttle active: risk × ${thr.mult} until the system's equity is back above its ${sm.throttle.window}-trade average. ` : '') + `Regime: UP_LOW_VOL (Nifty above rising 20 DMA, VIX ${lastClose('INDIA VIX')}). ${items.filter((x) => !x.reserve).length} tickets proposed from your trading baskets; prefer breakouts and pullbacks on the long side. No results-day conflicts in the plan.` };
  }

  function seedSuggestions(state) {
    const now = Date.now();
    const untracked = INSTRUMENTS.filter((i) => !state.tracked[i.symbol]).slice(0, 40);
    const r = rng('sugg:' + new Date().toDateString());
    const pick = () => untracked[Math.floor(r() * untracked.length)].symbol;
    const best = state.baskets.find((b) => b.name === 'Best stocks');
    const myc = state.baskets.find((b) => b.name === 'MyChoice');
    const s1 = pick(), s2 = 'TATAPOWER', s3 = myc && myc.members[myc.members.length - 1];
    const list = [
      { type: 'track', symbol: s1, reason: `Strong relative strength (top 10% of F&O stocks) and 3 setups would have passed rules in the last month.`, evidence: 'RS 20d +6.2% vs Nifty · turnover ₹420 Cr/day' },
      { type: 'add_to_basket', symbol: s2, basket: best && best.name, reason: 'Matches the Best stocks profile: high liquidity, trending, clean breakouts.', evidence: 'ORB 15m BUY on this stock: 41 OOS trades, +0.31R' },
      { type: 'remove_from_basket', symbol: s3, basket: myc && myc.name, reason: 'Repeated failures for intraday setups over 60 sessions.', evidence: '18 trades, expectancy −0.22R, profit factor 0.71' },
      { type: 'focus', reason: 'Prefer swing over intraday tomorrow: VIX rising and opening gaps widening.', evidence: 'Intraday expectancy in this regime +0.04R vs swing +0.21R' },
      { type: 'promotion', reason: 'Hypothesis H-07 passed: ORB 15m BUY target 2.0R → 2.5R.', evidence: 'Walk-forward +0.05R, 20-day shadow confirmed' },
    ];
    return list.map((s, i) => ({ id: 'S' + now + i, ...s, status: 'pending', createdAt: now, expiresAt: now + 3 * DAY }));
  }

  window.BTData = {
    rng, hash, round2, tick, DAY, iso, DAYS, DAY_INDEX, INSTRUMENTS, INDICES, ALL, BY_SYMBOL,
    daily, closeOn, lastClose, prevClose, intraday1m, officialDaily, hasStaleAggregate, resultsDate, ISSUES, aggregate, xirr, buildPositions,
    seed, reportTrades, proposePlan, seedSuggestions, nextTradingDay, lastTradingDay,
    smartCfg, corr, breadth, marketGate, equityThrottle, learnedWindows, metaOf, sizeOf, HOUR_LABEL, HOUR_EDGE, SMART_DEFAULTS,
  };
})();
