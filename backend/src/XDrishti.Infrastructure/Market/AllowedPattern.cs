using System.Text.RegularExpressions;

namespace XDrishti.Infrastructure.Market;

/// <summary>One dated archive path on the allow-list: the exact host and a path regular expression that must match the whole path (anchor it with <c>^</c> and <c>$</c>).</summary>
internal sealed record AllowedPattern(string Host, Regex Path);
