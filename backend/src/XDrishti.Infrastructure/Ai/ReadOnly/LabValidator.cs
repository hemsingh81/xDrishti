using System.Text;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Infrastructure.Ai.ReadOnly;

/// <summary>
/// <c>lab_validate</c> (spec P9-02 AC 10): the Lab's own strategy-file library, so the hash is the hash of <c>POST /api/lab/validate</c>. It reads and compiles a text and stores nothing (it holds no database connection at all).
/// </summary>
internal sealed class LabValidator(IStrategyChecker checker) : IAiLabValidator
{
    private const int MaxBytes = 64 * 1024;

    public LabValidation Validate(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        if (Encoding.UTF8.GetByteCount(yaml) > MaxBytes)
        {
            return new LabValidation(false, null, [new LabValidationProblem(null, null, "lab.text_too_large", $"The strategy text is larger than {MaxBytes} bytes.", "error")]);
        }

        var check = checker.Check(yaml);
        return new LabValidation(check.Description is not null, check.Description?.Hash, [.. check.Issues.Select(i => new LabValidationProblem(i.Line, i.Column, i.Code, i.Message, i.Severity))]);
    }
}
