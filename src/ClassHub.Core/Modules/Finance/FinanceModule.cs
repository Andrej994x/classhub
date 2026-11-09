using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Finance.Application;
using ClassHub.Core.Modules.Finance.Contracts;
using ClassHub.Core.Modules.Finance.Domain;
using ClassHub.Core.Modules.Finance.Jobs;
using ClassHub.Core.Modules.Finance.Persistence;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Finance;

/// <summary>Packages and payment tracking (M11, R2-P10). Payments are made outside ClassHub; this is the teacher's record.</summary>
public sealed class FinanceModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FinanceQueries>();
        services.AddScoped<FinanceService>();
        services.AddScoped<IFinanceStats>(sp => sp.GetRequiredService<FinanceService>());
        services.AddScoped<ILessonReferences, FinanceLessonReferences>();
        services.AddScoped<LessonConsumption>();
        services.Configure<FinanceOptions>(configuration.GetSection(FinanceOptions.SectionName));
        services.AddSingleton<LessonConsumptionJob>();
        services.AddHostedService(sp => sp.GetRequiredService<LessonConsumptionJob>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var finance = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/finance").WithTags("Finance").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.FinanceRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.FinanceManage);

        finance.MapGet("/overview", async (FinanceService service, CancellationToken ct) => TypedResults.Ok(await service.OverviewAsync(ct)))
            .RequireAuthorization(read)
            .WithName("GetFinanceOverview")
            .WithSummary("Every student with finance records: what they owe per currency, lessons left and unpaid lessons.");

        finance.MapGet("/report", async (DateOnly from, DateOnly to, FinanceService service, CancellationToken ct) => TypedResults.Ok(await service.ReportAsync(from, to, ct)))
            .RequireAuthorization(read)
            .WithName("GetFinanceReport")
            .WithSummary("A period (local days, at most a year): packages sold, charged and received per currency, lessons consumed.");

        finance.MapGet("/students/{studentId:guid}", async (Guid studentId, int? historyPage, int? historyPageSize, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetStudentAsync(studentId, historyPage ?? 1, historyPageSize ?? FinanceService.HistoryPageSize, ct)))
            .RequireAuthorization(read)
            .WithName("GetStudentFinance")
            .WithSummary("Balances per currency, packages and one page of the history (newest first).");

        finance.MapPost("/students/{studentId:guid}/packages", async (Guid studentId, AssignPackageRequest request, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AssignPackageAsync(studentId, request, ct)))
            .WithValidation<AssignPackageRequest>()
            .RequireAuthorization(manage)
            .WithName("AssignPackage")
            .WithSummary("From an offer (its terms are copied) or custom; later offer changes never alter the package.");

        finance.MapPost("/students/{studentId:guid}/payments", async (Guid studentId, RecordPaymentRequest request, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.RecordPaymentAsync(studentId, request, ct)))
            .WithValidation<RecordPaymentRequest>()
            .RequireAuthorization(manage)
            .WithName("RecordPayment");

        finance.MapPost("/students/{studentId:guid}/adjustments", async (Guid studentId, BalanceAdjustmentRequest request, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AdjustBalanceAsync(studentId, request, ct)))
            .WithValidation<BalanceAdjustmentRequest>()
            .RequireAuthorization(manage)
            .WithName("AdjustBalance");

        finance.MapPost("/packages/{packageId:guid}/close", async (Guid packageId, PackageVersionRequest request, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ClosePackageAsync(packageId, request, ct)))
            .RequireAuthorization(manage)
            .WithName("ClosePackage");

        finance.MapPost("/packages/{packageId:guid}/lesson-adjustments", async (Guid packageId, LessonAdjustmentRequest request, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AdjustLessonsAsync(packageId, request, ct)))
            .WithValidation<LessonAdjustmentRequest>()
            .RequireAuthorization(manage)
            .WithName("AdjustPackageLessons");

        finance.MapPost("/payments/{paymentId:guid}/reverse", async (Guid paymentId, ReverseRequest request, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ReversePaymentAsync(paymentId, request, ct)))
            .WithValidation<ReverseRequest>()
            .RequireAuthorization(manage)
            .WithName("ReversePayment");

        finance.MapPost("/ledger/{entryId:guid}/reverse", async (Guid entryId, ReverseRequest request, FinanceService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ReverseEntryAsync(entryId, request, ct)))
            .WithValidation<ReverseRequest>()
            .RequireAuthorization(manage)
            .WithName("ReverseLessonEntry");
    }
}

internal sealed class AssignPackageRequestValidator : AbstractValidator<AssignPackageRequest>
{
    public AssignPackageRequestValidator()
    {
        When(x => x.OfferId is null, () =>
        {
            RuleFor(x => x.Title).Must(t => t is not null && t.Trim().Length is >= 1 and <= StudentPackage.TitleMaxLength)
                .WithMessage($"Enter a title (up to {StudentPackage.TitleMaxLength} characters).");
            RuleFor(x => x.LessonCount).NotNull().InclusiveBetween(1, StudentPackage.MaxLessons);
            RuleFor(x => x.PriceMinor).NotNull().InclusiveBetween(0, StudentPackage.MaxPriceMinor);
            RuleFor(x => x.Currency).Must(FinanceService.ValidCurrency).WithMessage("Unknown currency.");
        });
    }
}

internal sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.AmountMinor).NotNull().InclusiveBetween(1, FinanceService.MaxAmount).WithMessage("Enter an amount above zero.");
        RuleFor(x => x.Currency).Must(FinanceService.ValidCurrency).WithMessage("Unknown currency.");
        RuleFor(x => x.PaidOn).NotNull();
        RuleFor(x => x.Method).Must(m => m is not null && Enum.GetNames<PaymentMethod>().Contains(m, StringComparer.Ordinal))
            .WithMessage("The method must be Cash, BankTransfer, Card or Other.");
        RuleFor(x => x.Note).MaximumLength(Payment.NoteMaxLength);
    }
}

internal sealed class ReverseRequestValidator : AbstractValidator<ReverseRequest>
{
    public ReverseRequestValidator() =>
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 1 and <= LessonLedgerEntry.ReasonMaxLength)
            .WithMessage("Give a reason (up to 500 characters).");
}

internal sealed class BalanceAdjustmentRequestValidator : AbstractValidator<BalanceAdjustmentRequest>
{
    public BalanceAdjustmentRequestValidator()
    {
        RuleFor(x => x.AmountMinor).Must(a => a is { } v && v != 0 && v >= -FinanceService.MaxAmount && v <= FinanceService.MaxAmount)
            .WithMessage("Enter an amount other than zero.");
        RuleFor(x => x.Currency).Must(FinanceService.ValidCurrency).WithMessage("Unknown currency.");
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 1 and <= BalanceAdjustment.ReasonMaxLength)
            .WithMessage("Give a reason (up to 500 characters).");
    }
}

internal sealed class LessonAdjustmentRequestValidator : AbstractValidator<LessonAdjustmentRequest>
{
    public LessonAdjustmentRequestValidator()
    {
        RuleFor(x => x.Quantity).Must(q => q is { } v && v != 0 && v >= -StudentPackage.MaxLessons && v <= StudentPackage.MaxLessons)
            .WithMessage("Change at least one lesson.");
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 1 and <= LessonLedgerEntry.ReasonMaxLength)
            .WithMessage("Give a reason (up to 500 characters).");
    }
}
