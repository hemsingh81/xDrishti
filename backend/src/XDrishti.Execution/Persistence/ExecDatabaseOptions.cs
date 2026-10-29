using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// Section <c>Database</c> of the host: the connection string (no password) and the password from the secret file <c>Database__Password</c>. The service refuses to start under any role but <c>xd_exec</c>, so a wrong compose file or a copied
/// connection string cannot give it the owner's or the API's rights.
/// </summary>
public sealed class ExecDatabaseOptions : IValidatableObject
{
    public const string Section = "Database";
    public const string RequiredRole = "xd_exec";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public string? Password { get; set; }

    public NpgsqlConnectionStringBuilder Build()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            yield break;
        }

        string? user = null;
        try
        {
            user = new NpgsqlConnectionStringBuilder(ConnectionString).Username;
        }
        catch (ArgumentException)
        {
            // reported below as a wrong role: the text is not a usable connection string
        }

        if (user != RequiredRole)
        {
            yield return new ValidationResult($"The execution service runs only as the database role {RequiredRole}.", [nameof(ConnectionString)]);
        }
    }

    public override string ToString() => "ExecDatabaseOptions { password hidden }";
}
