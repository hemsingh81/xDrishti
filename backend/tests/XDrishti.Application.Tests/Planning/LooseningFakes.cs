#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Inert loosening dependencies for tests of the plan use cases that are not about the banner: nothing is undismissed and the automatic setting is OFF.</summary>
internal static class LooseningFakes
{
    private static ILooseningStore Store()
    {
        var store = Substitute.For<ILooseningStore>();
        store.ListUndismissedAsync(Arg.Any<CancellationToken>()).Returns(new List<LooseningNotice>());
        store.CurrentSettingAsync(Arg.Any<CancellationToken>()).Returns(new LooseningSwitch(null, false, null));
        return store;
    }

    public static LooseningReader Reader() => new(Store(), NullLogger<LooseningReader>.Instance);

    public static LooseningAutoDismissal Auto(TimeProvider time) =>
        new(Store(), Substitute.For<IAccountEventLog>(), Substitute.For<IAccountUnitOfWork>(), time, NullLogger<LooseningAutoDismissal>.Instance);
}
