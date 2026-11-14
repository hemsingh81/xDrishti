using System.Reflection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// ADR 0292: costs are gross forever, so there is no path left on which the owner checks a charge sheet: no endpoint, verb, handler or writer of the confirmation, and the one confirmation source of the hosts is the one that never has a confirmation. SR-03, which needed a
/// checked sheet, is gone as well.
/// </summary>
public sealed class ChargeSheetDroppedArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly string[] RemovedTypeNames =
    [
        "ChargeSheetEndpoints", "ChargeSheetCommand", "RecordChargeSheetActHandler", "RecordChargeSheetActCommand", "GetChargeSheetStatusHandler", "ChargeSheetStatusBuilder", "IChargeSheetLog", "IChargeSheetLogWriter", "ChargeSheetEventStore",
        "ChargeSheetConfirmationStore", "StoredCostConfirmation", "CostSheetCommand", "CostSheetProgram", "LoadCostSheetHandler", "CheckCostSheetHandler", "DeriveCostSheetHandler", "ShowCostSheetHandler", "CostBillCheck",
        "RegisterHoldOutHandler", "RunHoldOutRegistrationHandler", "HoldOutRegistrationEndpoints", "HoldOutRegistrationStore", "SystemReplaySealStep", "SystemReplayNotices",
    ];

    [Fact]
    public void No_assembly_has_a_type_of_the_charge_sheet_check_or_of_sr03()
    {
        var found = new[] { Application, Infrastructure, Api }.SelectMany(a => a.GetTypes()).Where(t => RemovedTypeNames.Contains(t.Name, StringComparer.Ordinal)).Select(t => t.FullName).ToList();

        found.ShouldBeEmpty();
        Api.GetTypes().ShouldContain(t => t.Name == "BoundedFile", "the scan does see the Api's types");
    }

    [Fact]
    public void The_hosts_register_the_confirmation_source_that_never_confirms()
    {
        var registration = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "DependencyInjection.cs"));

        registration.ShouldContain("AddSingleton<IChargeSheetConfirmationSource, XDrishti.Infrastructure.Ai.ReadOnly.NoChargeSheetConfirmation>()");
        registration.ShouldNotContain("ICostConfirmationFact", Case.Sensitive, "the paper statistics keep the Application default, NotConfirmed");
    }
}
