using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClassHub.Core.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox. With provider <c>DevMailbox</c> the dispatcher delivers into the local
    /// <c>DevMailbox</c> table; with <c>None</c> messages are only queued (no delivery).
    /// </summary>
    public static IServiceCollection AddClassHubEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

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
            services.AddScoped<IEmailSender, DevMailboxEmailSender>();
            services.AddHostedService<EmailOutboxDispatcher>();
        }

        return services;
    }
}
