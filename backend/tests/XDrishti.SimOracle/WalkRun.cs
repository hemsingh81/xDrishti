namespace XDrishti.SimOracle;

/// <summary>One walk. Loops over the list by index; every price comparison is a decimal comparison.</summary>
internal sealed class WalkRun(IReadOnlyList<OracleBar> bars, OracleCandidate cand, OracleRules rules, OracleFaults faults)
{
    private readonly HashSet<string> _rows = new(StringComparer.Ordinal);
    private readonly HashSet<DateOnly> _seen = [];

    private int _zero;
    private int _pendingZero;
    private int _examined;

    private bool _swing;
    private bool _buy;
    private decimal _d;
    private decimal _tick;
    private decimal _limit;
    private decimal _stop;
    private decimal _entry;
    private decimal _risk;
    private decimal? _target;
    private OracleEntryFill _entryFill;
    private decimal _adv;
    private decimal _fav;
    private DateOnly _entryDate;
    private DateOnly _dueDate;
    private int _dueMinute;
    private int _lastIdx;
    private long _prevEpoch;
    private DateOnly _prevDate;

    private sealed class Exit
    {
        public OracleExitReason Reason { get; init; }

        public decimal Price { get; init; }

        public int Index { get; set; }

        public bool StopGap { get; init; }

        public bool TargetGap { get; init; }

        public bool Ambiguous { get; init; }

        public bool LateSquareOff { get; init; }

        public bool LastClose { get; init; }

        public bool FromBar { get; init; }

        public bool OpenBeyondTarget { get; init; }
    }

    private readonly record struct Fill(int Index, OracleNoTradeReason? NoTrade, long Reference);

    public OracleResult Execute()
    {
        long dec = cand.DecisionEpochSeconds;
        DateOnly decDate = OracleClock.DateOf(dec - 60);
        _swing = rules.Hold is not null;
        OracleCalendar cal = rules.Hold?.Calendar ?? OracleCalendar.Default;
        bool rests = _swing && OracleClock.MinuteOfDay(dec - 60) + 1 >= cal.SessionOf(decDate).ToMinute;
        _buy = cand.Side == OracleSide.Buy;
        _d = _buy ? 1m : -1m;
        _tick = rules.Tick;
        _stop = OracleNumbers.Conv(cand.StopLevel);
        decimal trig = OracleNumbers.Conv(cand.TriggerClose);
        decimal p = OracleNumbers.Conv(rules.EntryLimitPercent) / 100m;
        _limit = ComputeLimit(trig, p);

        Row(OracleRows.E1);
        Fill search = rests ? SearchOvernight(decDate, cal) : SearchSameDay(decDate);
        if (search.NoTrade is not null)
        {
            return NoTradeResult(search.NoTrade.Value, 0);
        }

        int f = search.Index;
        if (Has(OracleFaults.OffByOneMinute) && f + 1 < bars.Count)
        {
            f++;
        }

        OracleBar fb = bars[f];
        decimal fo = OracleNumbers.Conv(fb.Open);
        decimal fh = OracleNumbers.Conv(fb.High);
        decimal fl = OracleNumbers.Conv(fb.Low);
        if (fb.EpochSeconds > search.Reference)
        {
            Row(OracleRows.E1Hole);
        }

        if (rests && fb.EpochSeconds > search.Reference)
        {
            Row(OracleRows.E9Late);
        }

        if (rests && fb.EpochSeconds == search.Reference && fo > 0m && (fh - fl) / fo >= 0.01m)
        {
            Row(OracleRows.E9Wide);
        }

        Row(OracleRows.E2);
        if (_buy ? fo <= _limit : fo >= _limit)
        {
            _entryFill = OracleEntryFill.AtOpen;
            _entry = fo;
            Row(OracleRows.E3);
            if (fo == _limit)
            {
                Row(OracleRows.TieLimit);
            }

            Row(OracleNumbers.OnGrid(fo, _tick) ? OracleRows.GridOn : OracleRows.GridOff);
        }
        else if (_buy ? fl <= _limit : fh >= _limit)
        {
            _entryFill = OracleEntryFill.AtLimit;
            _entry = _limit;
            Row(OracleRows.E4);
            if (_buy ? fl == _limit : fh == _limit)
            {
                Row(OracleRows.TieLimit);
            }
        }
        else
        {
            Row(OracleRows.E5);
            Row(OracleRows.E5Gap);
            return NoTradeResult(OracleNoTradeReason.LimitNotReached, 1);
        }

        if (_buy ? _entry <= _stop : _entry >= _stop)
        {
            Row(OracleRows.E6);
            if (_entryFill == OracleEntryFill.AtOpen)
            {
                Row(OracleRows.E6Gap);
            }

            if (_entry == _stop)
            {
                Row(OracleRows.TieStop);
            }

            return NoTradeResult(OracleNoTradeReason.EntryThroughStop, 1);
        }

        _risk = Math.Abs(_entry - _stop);
        if (rules.RewardRisk is { } rr)
        {
            _target = _entry + (_d * OracleNumbers.Conv(rr) * _risk);
        }

        decimal entryFilled = EntryFilledPrice(fh, fl);
        _entryDate = OracleClock.DateOf(fb.EpochSeconds);
        _adv = _entry;
        _fav = _entry;
        _lastIdx = f;
        _prevEpoch = fb.EpochSeconds;
        _prevDate = _entryDate;
        if (_swing)
        {
            int n = rules.Hold!.MaxHoldSessions;
            _dueDate = cal.AddTradingDays(Has(OracleFaults.MaxHoldFromDecisionDate) ? decDate : _entryDate, n - 1);
            _dueMinute = cal.DueMinute(_dueDate);
            Row(OracleRows.X7);
        }

        Exit exit = Position(f);
        return BuildTrade(exit, f, entryFilled, decDate, cal, rests);
    }

    private bool Has(OracleFaults f) => (faults & f) != 0;

    private void Row(string id) => _rows.Add(id);

    private static int Minute(long epoch) => OracleClock.MinuteOfDay(epoch);

    private bool IsEmpty(OracleBar b) => b.Volume == 0 && !Has(OracleFaults.ZeroVolumeMinuteIsTrade);

    private decimal ComputeLimit(decimal trig, decimal p)
    {
        if (_buy)
        {
            decimal x = trig * (1m + p);
            return Has(OracleFaults.LimitRoundedToNearestTick) ? OracleNumbers.TickNearest(x, _tick) : OracleNumbers.TickDown(x, _tick);
        }

        decimal y = trig * (1m - p);
        return Has(OracleFaults.LimitRoundedToNearestTick) ? OracleNumbers.TickNearest(y, _tick) : OracleNumbers.TickUp(y, _tick);
    }

    private Fill SearchSameDay(DateOnly decDate)
    {
        long dec = cand.DecisionEpochSeconds;
        int n = bars.Count;
        int j = Has(OracleFaults.FillOnSignalMinute) ? cand.DecisionBarIndex : cand.DecisionBarIndex + 1;
        while (true)
        {
            if (j >= n)
            {
                return new Fill(-1, OracleNoTradeReason.NoFillBar, dec);
            }

            OracleBar b = bars[j];
            bool sameDate = OracleClock.DateOf(b.EpochSeconds) == decDate;
            bool inTime = b.EpochSeconds <= dec + 300;
            if (!sameDate || !inTime)
            {
                return new Fill(-1, OracleNoTradeReason.NoFillBar, dec);
            }

            if (IsEmpty(b))
            {
                _zero++;
                Row(OracleRows.E0);
                j++;
                continue;
            }

            if (!_swing && Minute(b.EpochSeconds) >= rules.SquareOffMinute)
            {
                return new Fill(-1, OracleNoTradeReason.AfterSquareOff, dec);
            }

            return new Fill(j, null, dec);
        }
    }

    private Fill SearchOvernight(DateOnly decDate, OracleCalendar cal)
    {
        Row(OracleRows.E8);
        Row(OracleRows.E9);
        DateOnly fillDate = Has(OracleFaults.WeekendNotSkippedInE9) ? decDate.AddDays(1) : cal.NextTradingDay(decDate);
        for (DateOnly d = decDate.AddDays(1); d < fillDate; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && !cal.IsTradingDay(d))
            {
                Row(OracleRows.E9Weekend);
            }
            else if (cal.Holidays.Contains(d))
            {
                Row(OracleRows.E9Holiday);
            }
        }

        long openEpoch = OracleClock.Epoch(fillDate, cal.SessionOf(fillDate).FromMinute);
        long windowEnd = openEpoch + (Has(OracleFaults.Minute0921AllowedAsFill) ? 360 : 300);
        int n = bars.Count;
        int j = cand.DecisionBarIndex + 1;
        while (true)
        {
            if (j >= n)
            {
                Row(OracleRows.Pending);
                return Has(OracleFaults.PendingReadAsNoFillBar)
                    ? new Fill(-1, OracleNoTradeReason.NoFillBar, openEpoch)
                    : new Fill(-1, OracleNoTradeReason.OrderPendingAtEnd, openEpoch);
            }

            OracleBar b = bars[j];
            DateOnly bd = OracleClock.DateOf(b.EpochSeconds);
            if (bd == decDate)
            {
                j++;
                continue;
            }

            if (bd != fillDate)
            {
                return new Fill(-1, OracleNoTradeReason.NoFillBar, openEpoch);
            }

            if (b.EpochSeconds < openEpoch)
            {
                j++;
                continue;
            }

            if (b.EpochSeconds > windowEnd)
            {
                Row(OracleRows.E9Past);
                return new Fill(-1, OracleNoTradeReason.NoFillBar, openEpoch);
            }

            if (IsEmpty(b))
            {
                _zero++;
                Row(OracleRows.E0);
                j++;
                continue;
            }

            return new Fill(j, null, openEpoch);
        }
    }

    private decimal EntryFilledPrice(decimal h, decimal l)
    {
        Row(OracleRows.S1);
        if (_entryFill == OracleEntryFill.AtLimit)
        {
            return _limit;
        }

        if (rules.Slippage.IsNone)
        {
            return _entry;
        }

        decimal a = (OracleNumbers.Conv(rules.Slippage.Ticks) * _tick) + (OracleNumbers.Conv(rules.Slippage.BarRangeFrac) * (h - l));
        Row(OracleRows.S2);
        Row(OracleRows.S3);
        if (_buy)
        {
            decimal v = OracleNumbers.TickUp(_entry + a, _tick);
            return Has(OracleFaults.EntrySlippedPastLimit) ? v : Math.Min(v, _limit);
        }

        decimal w = OracleNumbers.TickDown(_entry - a, _tick);
        return Has(OracleFaults.EntrySlippedPastLimit) ? w : Math.Max(w, _limit);
    }

    private decimal ExitFilledPrice(Exit ex, decimal h, decimal l)
    {
        Row(OracleRows.S1);
        if (rules.Slippage.IsNone)
        {
            return ex.Price;
        }

        if (ex.Reason == OracleExitReason.Target && !Has(OracleFaults.SlippageOnTargetExit))
        {
            Row(OracleRows.S3);
            return _buy ? OracleNumbers.TickDown(ex.Price, _tick) : OracleNumbers.TickUp(ex.Price, _tick);
        }

        decimal extra = 0m;
        if (ex.Reason == OracleExitReason.Stop || (ex.Reason == OracleExitReason.MaxHold && Has(OracleFaults.ExtraStopTicksOnMaxHold)))
        {
            extra = OracleNumbers.Conv(rules.Slippage.StopExitExtraTicks);
        }

        decimal a = ((OracleNumbers.Conv(rules.Slippage.Ticks) + extra) * _tick) + (OracleNumbers.Conv(rules.Slippage.BarRangeFrac) * (h - l));
        Row(OracleRows.S2);
        Row(OracleRows.S3);
        if (ex.Reason is OracleExitReason.MaxHold or OracleExitReason.EndOfData)
        {
            Row(OracleRows.S5);
        }

        return _buy ? OracleNumbers.TickDown(ex.Price - a, _tick) : OracleNumbers.TickUp(ex.Price + a, _tick);
    }

    private Exit Position(int f)
    {
        int n = bars.Count;
        int k = f;
        bool isFill = true;
        while (true)
        {
            if (!isFill)
            {
                if (k >= n)
                {
                    return EndOfData();
                }

                long ek = bars[k].EpochSeconds;
                DateOnly dk = OracleClock.DateOf(ek);
                if (!_swing && dk != _entryDate)
                {
                    return EndOfData();
                }

                if (_swing && (dk > _dueDate || (dk == _dueDate && Minute(ek) > _dueMinute)))
                {
                    return LastClose();
                }

                if (IsEmpty(bars[k]))
                {
                    _pendingZero++;
                    Row(OracleRows.E0);
                    k++;
                    continue;
                }
            }

            Exit? ex = Visit(k, isFill);
            if (ex is not null)
            {
                return ex;
            }

            isFill = false;
            k++;
        }
    }

    private Exit EndOfData()
    {
        Row(_swing ? OracleRows.X9 : OracleRows.X6);
        OracleBar b = bars[_lastIdx];
        return new Exit
        {
            Reason = OracleExitReason.EndOfData,
            Price = OracleNumbers.Conv(b.Close),
            Index = _lastIdx,
            FromBar = true,
        };
    }

    private Exit LastClose()
    {
        Row(OracleRows.X8);
        Row(OracleRows.X8Last);
        OracleBar b = bars[_lastIdx];
        return new Exit
        {
            Reason = OracleExitReason.MaxHold,
            Price = OracleNumbers.Conv(b.Close),
            Index = _lastIdx,
            LastClose = true,
            FromBar = true,
        };
    }

    private Exit? Visit(int k, bool isFill)
    {
        OracleBar b = bars[k];
        decimal o = OracleNumbers.Conv(b.Open);
        decimal h = OracleNumbers.Conv(b.High);
        decimal l = OracleNumbers.Conv(b.Low);
        decimal c = OracleNumbers.Conv(b.Close);
        long ek = b.EpochSeconds;
        DateOnly dk = OracleClock.DateOf(ek);
        _examined++;
        if (!isFill && (_pendingZero > 0 || (ek - _prevEpoch > 60 && dk == _prevDate)))
        {
            Row(OracleRows.XHole);
        }

        _zero += _pendingZero;
        _pendingZero = 0;
        bool firstOfDate = !isFill && dk != _prevDate && dk > _entryDate;
        _seen.Add(dk);
        bool nowDue = _swing && dk == _dueDate && Minute(ek) == _dueMinute;
        Exit? ex;
        if (!_swing && !isFill && Minute(ek) >= rules.SquareOffMinute)
        {
            ex = SquareOffMinute(o, ek);
        }
        else
        {
            ex = Triggers(o, h, l, isFill);
        }

        if (ex is null && nowDue)
        {
            Row(OracleRows.X8);
            Row(OracleRows.X8Due);
            ex = new Exit { Reason = OracleExitReason.MaxHold, Price = c, FromBar = true };
        }

        if (ex is not null)
        {
            ex.Index = k;
            AdjustExtremes(ex, o, h, l, isFill);
            if (ex.FromBar)
            {
                Row(OracleNumbers.OnGrid(ex.Price, _tick) ? OracleRows.GridOn : OracleRows.GridOff);
            }

            _overnightStop = ex.StopGap && firstOfDate;
            _overnightTarget = ex.TargetGap && firstOfDate;
            return ex;
        }

        WidenAdv(l, h);
        if (!(isFill && _entryFill == OracleEntryFill.AtLimit) || Has(OracleFaults.AtLimitHighCountedInMfe))
        {
            WidenFav(l, h);
        }

        _lastIdx = k;
        _prevEpoch = ek;
        _prevDate = dk;
        return null;
    }

    private bool _overnightStop;
    private bool _overnightTarget;

    private Exit SquareOffMinute(decimal o, long ek)
    {
        Row(OracleRows.X1);
        bool late = Minute(ek) > rules.SquareOffMinute;
        if (late)
        {
            Row(OracleRows.X1Late);
        }

        if (_buy ? o <= _stop : o >= _stop)
        {
            if (o == _stop)
            {
                Row(OracleRows.TieStop);
            }

            bool stopGap = _buy ? o < _stop : o > _stop;
            if (stopGap)
            {
                Row(OracleRows.XStopGap);
            }

            return new Exit
            {
                Reason = OracleExitReason.Stop,
                Price = Has(OracleFaults.GapFilledAtStopPrice) ? _stop : o,
                StopGap = stopGap,
                FromBar = !Has(OracleFaults.GapFilledAtStopPrice),
            };
        }

        if (_target is { } t && (_buy ? o >= t : o <= t))
        {
            if (o == t)
            {
                Row(OracleRows.TieTarget);
            }

            bool targetGap = _buy ? o > t : o < t;
            if (targetGap)
            {
                Row(OracleRows.XTargetGap);
            }

            return new Exit
            {
                Reason = OracleExitReason.Target,
                Price = t,
                TargetGap = targetGap,
                OpenBeyondTarget = true,
            };
        }

        return new Exit { Reason = OracleExitReason.SquareOff, Price = o, LateSquareOff = late, FromBar = true };
    }

    private Exit? Triggers(decimal o, decimal h, decimal l, bool isFill)
    {
        bool atLimitFill = isFill && _entryFill == OracleEntryFill.AtLimit;
        if (atLimitFill)
        {
            Row(OracleRows.X5);
        }

        bool stopHit = _buy ? l <= _stop : h >= _stop;
        decimal? target = atLimitFill ? null : _target;
        bool targetHit = target is { } tv && (_buy ? h >= tv : l <= tv);
        if (_buy ? l == _stop : h == _stop)
        {
            Row(OracleRows.TieStop);
        }

        if (target is { } tt && (_buy ? h == tt : l == tt))
        {
            Row(OracleRows.TieTarget);
        }

        if (stopHit && targetHit)
        {
            decimal t = target!.Value;
            Row(OracleRows.X4);
            bool openBeyond = _buy ? o >= t : o <= t;
            if (Has(OracleFaults.StopCheckedAfterTarget))
            {
                return TargetExit(t, o);
            }

            if (openBeyond)
            {
                Row(OracleRows.X4Beyond);
                return TargetExit(t, o);
            }

            bool between = _buy ? (o > _stop && o < t) : (o < _stop && o > t);
            if (between)
            {
                Row(OracleRows.X4Between);
            }

            return StopExit(o, between);
        }

        if (stopHit)
        {
            return StopExit(o, false);
        }

        if (targetHit)
        {
            return TargetExit(target!.Value, o);
        }

        return null;
    }

    private Exit StopExit(decimal o, bool ambiguous)
    {
        Row(OracleRows.X2);
        bool gap = _buy ? o < _stop : o > _stop;
        if (gap)
        {
            Row(OracleRows.XStopGap);
        }

        decimal price = Has(OracleFaults.GapFilledAtStopPrice) ? _stop : (_buy ? Math.Min(_stop, o) : Math.Max(_stop, o));
        return new Exit
        {
            Reason = OracleExitReason.Stop,
            Price = price,
            StopGap = gap,
            Ambiguous = ambiguous,
            FromBar = gap && !Has(OracleFaults.GapFilledAtStopPrice),
        };
    }

    private Exit TargetExit(decimal t, decimal o)
    {
        Row(OracleRows.X3);
        bool gap = _buy ? o > t : o < t;
        if (gap)
        {
            Row(OracleRows.XTargetGap);
        }

        bool beyond = _buy ? o >= t : o <= t;
        decimal price = (Has(OracleFaults.BuyTargetFilledAtOpenWhenBeyond) && _buy && o > t) ? o : t;
        return new Exit { Reason = OracleExitReason.Target, Price = price, TargetGap = gap, OpenBeyondTarget = beyond };
    }

    private void WidenAdv(decimal lo, decimal hi)
    {
        _adv = _buy ? Math.Min(_adv, lo) : Math.Max(_adv, hi);
    }

    private void WidenFav(decimal lo, decimal hi)
    {
        _fav = _buy ? Math.Max(_fav, hi) : Math.Min(_fav, lo);
    }

    private void AdjustExtremes(Exit ex, decimal o, decimal h, decimal l, bool isFill)
    {
        switch (ex.Reason)
        {
            case OracleExitReason.Stop:
                _adv = _buy ? Math.Min(_adv, ex.Price) : Math.Max(_adv, ex.Price);
                break;
            case OracleExitReason.Target:
                if (!ex.OpenBeyondTarget)
                {
                    WidenAdv(l, h);
                }

                if (_target is { } t)
                {
                    WidenFav(t, t);
                }

                break;
            case OracleExitReason.SquareOff:
                WidenAdv(o, o);
                WidenFav(o, o);
                break;
            case OracleExitReason.MaxHold when !ex.LastClose:
                WidenAdv(l, h);
                if (!(isFill && _entryFill == OracleEntryFill.AtLimit) || Has(OracleFaults.AtLimitHighCountedInMfe))
                {
                    WidenFav(l, h);
                }

                break;
            default:
                break;
        }
    }

    private OracleResult BuildTrade(Exit ex, int f, decimal entryFilled, DateOnly decDate, OracleCalendar cal, bool rests)
    {
        OracleBar xb = bars[ex.Index];
        decimal xh = OracleNumbers.Conv(xb.High);
        decimal xl = OracleNumbers.Conv(xb.Low);
        decimal exitFilled = ExitFilledPrice(ex, xh, xl);
        Row(OracleRows.S4);
        decimal rExact = _d * (ex.Price - _entry) / _risk;
        decimal rFilled = _d * (exitFilled - entryFilled) / _risk;
        decimal slipR = rFilled - rExact;
        decimal maeBase = Has(OracleFaults.MaeFromFilledEntry) ? entryFilled : _entry;
        decimal mae = Math.Max(0m, _d * (maeBase - _adv)) / _risk;
        decimal mfe = Math.Max(0m, _d * (_fav - _entry)) / _risk;
        DateOnly exitDate = OracleClock.DateOf(xb.EpochSeconds);
        int held = _swing ? 1 + cal.TradingDaysBetween(_entryDate, exitDate) : 1;
        int unseen = _swing ? Math.Max(0, held - _seen.Count) : 0;
        long holdEnd = xb.EpochSeconds;
        if (ex.LastClose)
        {
            holdEnd = OracleClock.Epoch(_dueDate, cal.SessionOf(_dueDate).ToMinute);
        }

        if (rules.Hold?.Calendar is { } calendar)
        {
            AddCalendarRows(calendar, decDate, exitDate);
        }

        OracleBar fb = bars[f];
        var trade = new OracleTrade(
            cand.Side,
            _entryDate,
            cand.DecisionEpochSeconds,
            fb.EpochSeconds,
            OracleNumbers.ToDouble(_entry),
            OracleNumbers.ToDouble(_stop),
            _target is { } tg ? OracleNumbers.ToDouble(tg) : null,
            xb.EpochSeconds,
            OracleNumbers.ToDouble(ex.Price),
            ex.Reason,
            OracleNumbers.ToDouble(rExact),
            OracleNumbers.ToDouble(mae),
            OracleNumbers.ToDouble(mfe),
            rules.Slippage.IsNone ? OracleFillBasis.Exact : OracleFillBasis.Modelled,
            OracleNumbers.ToDouble(rFilled),
            OracleNumbers.ToDouble(slipR),
            OracleNumbers.ToDouble(entryFilled),
            OracleNumbers.ToDouble(exitFilled),
            _entryFill,
            held,
            unseen);

        bool overnightEntry = _entryDate > decDate;
        long reference = rests ? OracleClock.Epoch(_entryDate, cal.SessionOf(_entryDate).FromMinute) : cand.DecisionEpochSeconds;
        var counters = new OracleCounters
        {
            Candidates = 1,
            Trades = 1,
            ExitsStop = ex.Reason == OracleExitReason.Stop ? 1 : 0,
            ExitsTarget = ex.Reason == OracleExitReason.Target ? 1 : 0,
            ExitsSquareOff = ex.Reason == OracleExitReason.SquareOff ? 1 : 0,
            ExitsEndOfData = ex.Reason == OracleExitReason.EndOfData ? 1 : 0,
            ExitsMaxHold = ex.Reason == OracleExitReason.MaxHold ? 1 : 0,
            MaxHoldAtLastClose = ex.LastClose ? 1 : 0,
            StopGapExits = ex.StopGap ? 1 : 0,
            TargetGapExits = ex.TargetGap ? 1 : 0,
            OvernightStopGaps = _overnightStop ? 1 : 0,
            OvernightTargetGaps = _overnightTarget ? 1 : 0,
            EntriesAfterMissingMinutes = fb.EpochSeconds > reference ? 1 : 0,
            BarsExamined = _examined,
            EntriesAtOpen = _entryFill == OracleEntryFill.AtOpen ? 1 : 0,
            EntriesAtLimit = _entryFill == OracleEntryFill.AtLimit ? 1 : 0,
            AmbiguousMinutes = ex.Ambiguous ? 1 : 0,
            LateSquareOffs = ex.LateSquareOff ? 1 : 0,
            ZeroVolumeMinutes = _zero,
            RiskUnderFourTicks = _risk < 4m * _tick ? 1 : 0,
            SessionsHeld = held,
            SessionsUnseen = unseen,
            OvernightEntries = overnightEntry ? 1 : 0,
        };
        return new OracleResult(trade, null, counters, _rows, holdEnd);
    }

    private void AddCalendarRows(OracleCalendar calendar, DateOnly decDate, DateOnly exitDate)
    {
        foreach (DateOnly h in calendar.Holidays)
        {
            if (h > decDate && h < exitDate && h.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                Row(OracleRows.CalHoliday);
            }
        }

        foreach (DateOnly s in calendar.SpecialSessions.Keys)
        {
            if (s >= _entryDate && s <= exitDate)
            {
                Row(OracleRows.CalSpecial);
            }
        }
    }

    private OracleResult NoTradeResult(OracleNoTradeReason reason, int barsExamined)
    {
        var c = new OracleCounters
        {
            Candidates = 1,
            NoFillBar = reason == OracleNoTradeReason.NoFillBar ? 1 : 0,
            AfterSquareOff = reason == OracleNoTradeReason.AfterSquareOff ? 1 : 0,
            EntryThroughStop = reason == OracleNoTradeReason.EntryThroughStop ? 1 : 0,
            LimitNotReached = reason == OracleNoTradeReason.LimitNotReached ? 1 : 0,
            OrderPendingAtEnd = reason == OracleNoTradeReason.OrderPendingAtEnd ? 1 : 0,
            BarsExamined = barsExamined,
            ZeroVolumeMinutes = _zero,
        };
        return new OracleResult(null, reason, c, _rows, null);
    }
}
