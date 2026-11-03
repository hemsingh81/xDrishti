using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.OptionExpression;

/// <summary><c>xd options remark --run RUNKEY --profile HASH</c> (P14-08): re-marks every trade of a stored cash run as the option version the registered profile describes.</summary>
public sealed record RemarkRunCommand(string RunKey, string ProfileHash) : ICommand;
