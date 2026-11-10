using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.AutoSwitch;
using XDrishti.Application.Abstractions.LearnedSettings;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.AutoSwitch;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Planning;
using XDrishti.Application.Platform.Services;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Application.Tests.LearnedSettings;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.AutoSwitch;

/// <summary>An <see cref="AutoSwitchService"/> on in-memory fakes: the five chain services (all paused), a scope, a connected Data account and a confirmed charge sheet, so a test breaks only the thing it is about.</summary>
internal sealed class AutoSwitchHarness : IServiceScheduleReader, IAutoSwitch, IAutoSwitchLog
{
    public const string Owner = "owner:me@x.test";

    public static readonly string[] Chain = [EodService.Id, PortfolioBrokerHistoryService.Id, WeeklyService.Id, NightlyService.Id, SuggestionsService.Id];

    public AutoSwitchHarness()
    {
        foreach (var id in Chain)
        {
            Enabled[id] = false;
        }

        Scopes.Row = new StoredAutoScope(1, [Guid.NewGuid()], [], new string('a', 64), Owner, DateTimeOffset.UnixEpoch);
        DataAccount.GetAsync(Arg.Any<CancellationToken>()).Returns(new DataAccountState(Guid.NewGuid(), "Dhan", DataAccountStatuses.Connected, null));
        Charges.FindAsync(Arg.Any<CancellationToken>()).Returns(new ChargeSheetConfirmation("sheet", "abc", new DateOnly(2026, 11, 1)));
        Plans.ListRunsAsync(Arg.Any<DateOnly?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<PlanRunRow>());
        Resume.HandleAsync(Arg.Any<ResumeServiceCommand>(), Arg.Any<CancellationToken>()).Returns(call => Set(call.Arg<ResumeServiceCommand>().ServiceId, true, call.Arg<ResumeServiceCommand>().Actor));
        Pause.HandleAsync(Arg.Any<PauseServiceCommand>(), Arg.Any<CancellationToken>()).Returns(call => Set(call.Arg<PauseServiceCommand>().ServiceId, false, call.Arg<PauseServiceCommand>().Actor));
    }

    public Dictionary<string, bool> Enabled { get; } = [];

    public List<string> ServiceActors { get; } = [];

    public HashSet<string> FailResume { get; } = [];

    public HashSet<string> FailPause { get; } = [];

    public List<AutoSwitchChange> Rows { get; } = [];

    public InMemoryEssentialStore Store { get; } = new();

    public FakeScopes Scopes { get; } = new();

    public IDataAccountStatus DataAccount { get; } = Substitute.For<IDataAccountStatus>();

    public IChargeSheetConfirmationSource Charges { get; } = Substitute.For<IChargeSheetConfirmationSource>();

    public IPlanStore Plans { get; } = Substitute.For<IPlanStore>();

    public ICommandHandler<ResumeServiceCommand, ServiceView> Resume { get; } = Substitute.For<ICommandHandler<ResumeServiceCommand, ServiceView>>();

    public ICommandHandler<PauseServiceCommand, ServiceView> Pause { get; } = Substitute.For<ICommandHandler<PauseServiceCommand, ServiceView>>();

    public AutoSwitchService Service() => new(this, Store, this, new ServiceRegistry([]), Resume, Pause, Scopes, DataAccount, Charges, this, Plans, new FakeTimeProvider(), NullLogger<AutoSwitchService>.Instance);

    public async ValueTask<bool> IsOnAsync(CancellationToken cancellationToken) => (await Store.GetAsync(AutoSwitchService.Key, cancellationToken))?.Value == "true";

    public ValueTask<ServiceScheduleInfo?> GetAsync(string serviceId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ServiceScheduleInfo?>(Enabled.TryGetValue(serviceId, out var on) ? new ServiceScheduleInfo(on, null, "", null) : null);

    public ValueTask<IReadOnlyList<AutoSwitchChange>> RecentAsync(int limit, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<AutoSwitchChange>>([.. Enumerable.Reverse(Rows).Take(limit)]);

    public ValueTask<AutoSwitchChange> AppendAsync(string kind, string actor, DateTimeOffset at, IReadOnlyList<string> changed, IReadOnlyDictionary<string, string> snapshot, CancellationToken cancellationToken)
    {
        var row = new AutoSwitchChange(Rows.Count + 1, kind, actor, at, changed, snapshot);
        Rows.Add(row);
        return ValueTask.FromResult(row);
    }

    private Result<ServiceView> Set(string id, bool on, string actor)
    {
        if ((on && FailResume.Contains(id)) || (!on && FailPause.Contains(id)))
        {
            return Result.Failure<ServiceView>(Error.Conflict("services.revision_changed", "x"));
        }

        Enabled[id] = on;
        ServiceActors.Add(actor);
        return Result.Success<ServiceView>(null!);
    }
}
