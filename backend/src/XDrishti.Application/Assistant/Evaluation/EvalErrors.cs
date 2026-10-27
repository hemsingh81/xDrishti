using XDrishti.Domain.Common;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>The expected failures of the eval (spec P9-09 Contract and AC 11, 13). A refusal stores nothing; none of them is ever a pass.</summary>
public static class EvalErrors
{
    public const string WrongDatabaseCode = "eval.wrong_database";
    public const string AssistantDisabledCode = "eval.assistant_disabled";
    public const string ModelUnavailableCode = "eval.model_unavailable";
    public const string ToolsUnavailableCode = "eval.tools_unavailable";
    public const string BusyCode = "eval.busy";
    public const string NightlyRunningCode = "eval.nightly_running";
    public const string FilesMissingCode = "eval.files_missing";
    public const string NotFoundCode = "assistant.eval_not_found";
    public const string InvalidCode = "assistant.invalid";
    public const string NoBaselineCode = "eval.no_baseline";

    /// <summary>The owner's own verb on the machine (<c>xd eval ...</c>): the only actor a run or an accept is made under.</summary>
    public const string CliActor = "cli:xd";

    /// <summary>The actor of the assistant under test: one per run.</summary>
    public static string RunActor(Guid runId) => "eval:" + runId.ToString("N");

    public static Error Invalid(string message) => Error.Validation(InvalidCode, message);

    public static Error NotFound(Guid id) => Error.NotFound(NotFoundCode, $"No eval run {id} is stored.");

    public static Error WrongDatabase() => Error.Conflict(WrongDatabaseCode, "The tool endpoint does not read the eval fixture database (its marker is missing or differs from eval/fixture/fixture.json). Nothing was asked of the model.");

    public static Error AssistantDisabled() => Error.Conflict(AssistantDisabledCode, "The assistant's master switch is off; the owner switches it on in the assistant's settings before an eval can run. The runner never sets it.");

    public static Error ModelUnavailable() => Error.Conflict(ModelUnavailableCode, "model_unavailable: the model is not running. A run that could not ask the model is not a result.");

    public static Error ToolsUnavailable() => Error.Conflict(ToolsUnavailableCode, "The eval tool endpoint (xd-mcp-eval) cannot be reached.");

    public static Error Busy() => Error.Conflict(BusyCode, "Another question or job is using the model; try again when it is done.");

    public static Error NightlyRunning() => Error.Conflict(NightlyRunningCode, "The nightly run is in progress; run the eval when it has finished.");

    public static Error FilesMissing(string what) => Error.Conflict(FilesMissingCode, $"The eval files cannot be read: {what}.");

    public static Error NoBaseline() => Error.Conflict(NoBaselineCode, "No eval run has been accepted yet; name the run to compare with (--baseline).");
}
