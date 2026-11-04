using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ClassHub.Core.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>Environments in which the local dev mailbox may receive email.</summary>
    public static readonly string[] DevMailboxEnvironments = ["Development", "Testing", "E2E"];

    /// <summary>
    /// Registers the outbox. With provider <c>DevMailbox</c> the dispatcher delivers into the local
    /// <c>DevMailbox</c> table (Development, Testing and E2E only: anywhere else startup fails, because real
    /// recipients would never get their emails while the token-bearing bodies stay in the table); with
    /// <c>None</c> messages are only queued (no delivery).
    /// </summary>
    public static IServiceCollection AddClassHubEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AppLinkOptions>()
            .Bind(configuration.GetSection(AppLinkOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IEmailOutbox, EmailOutbox>();

        var provider = configuration.GetSection(EmailOptions.SectionName)[nameof(EmailOptions.Provider)] ?? EmailProviders.None;
        if (string.Equals(provider, EmailProviders.DevMailbox, StringComparison.Ordinal))
        {
            if (!DevMailboxEnvironments.Any(environment.IsEnvironment))
            {
                throw new InvalidOperationException(
                    $"Email:Provider '{EmailProviders.DevMailbox}' is only allowed in {string.Join(", ", DevMailboxEnvironments)} (current: {environment.EnvironmentName}).");
            }

            services.AddScoped<IEmailSender, DevMailboxEmailSender>();
            services.AddHostedService<EmailOutboxDispatcher>();
        }

        return services;
    }
}
