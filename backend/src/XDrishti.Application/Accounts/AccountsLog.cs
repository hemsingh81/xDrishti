using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Accounts;

/// <summary>
/// Structured log events of the accounts module. Only account slugs, risk profile and bucket names (validated: no control characters),
/// outcome codes and broker error codes are logged — never client ids or anything secret (API keys, secrets, tokens, codes).
/// </summary>
internal static partial class AccountsLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Account {Slug} created")]
    public static partial void AccountCreated(ILogger logger, string slug);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Connection of account {Slug} updated")]
    public static partial void ConnectionUpdated(ILogger logger, string slug);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "Roles of account {Slug} changed")]
    public static partial void RolesChanged(ILogger logger, string slug);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Primary Data account is now {Slug}")]
    public static partial void PrimaryDataChanged(ILogger logger, string slug);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information, Message = "Trading settings of account {Slug} updated")]
    public static partial void TradingUpdated(ILogger logger, string slug);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information, Message = "Portfolio settings of account {Slug} updated")]
    public static partial void PortfolioUpdated(ILogger logger, string slug);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Information, Message = "Data settings of account {Slug} updated")]
    public static partial void DataUpdated(ILogger logger, string slug);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Information, Message = "Account {Slug} archived")]
    public static partial void AccountArchived(ILogger logger, string slug);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Information, Message = "Risk profile {Name} created")]
    public static partial void RiskProfileCreated(ILogger logger, string name);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information, Message = "Risk profile {Name} updated")]
    public static partial void RiskProfileUpdated(ILogger logger, string name);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Information, Message = "Risk profile {Name} deleted")]
    public static partial void RiskProfileDeleted(ILogger logger, string name);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Information, Message = "Portfolio bucket {Name} added")]
    public static partial void BucketAdded(ILogger logger, string name);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Information, Message = "Broker secrets of account {Slug} replaced")]
    public static partial void SecretsReplaced(ILogger logger, string slug);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Information, Message = "Dhan sign-in started for account {Slug}")]
    public static partial void ConnectStarted(ILogger logger, string slug);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Information, Message = "Account {Slug} connected to Dhan")]
    public static partial void Connected(ILogger logger, string slug);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Warning, Message = "Connecting account {Slug} to Dhan failed: {Outcome} (broker error code {BrokerCode})")]
    public static partial void ConnectFailed(ILogger logger, string slug, string outcome, string? brokerCode);

    [LoggerMessage(EventId = 2018, Level = LogLevel.Warning, Message = "A Dhan sign-in callback was refused: {Outcome}")]
    public static partial void CallbackRefused(ILogger logger, string outcome);

    [LoggerMessage(EventId = 2020, Level = LogLevel.Information, Message = "Account {Slug} disconnected from Dhan")]
    public static partial void Disconnected(ILogger logger, string slug);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Warning, Message = "A change to {Subject} was refused: it collided with a concurrent change or a uniqueness rule")]
    public static partial void ChangeCollided(ILogger logger, string subject);
}
