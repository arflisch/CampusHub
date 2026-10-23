using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Billing.Domain.Payments;
using Dev4u.CampusHub.Errors;
using FluentResults;

namespace Dev4u.CampusHub.Billing.UseCases.Payments;

public sealed record ChargeEnrollmentCommand(Guid EnrollmentId, string? StudentId, Guid CourseId, int AmountInCents);

/// <summary>Payment as returned to the saga and to the student.</summary>
public sealed record PaymentDto(Guid EnrollmentId, Guid CourseId, int AmountInCents, string Currency, string Status,
                                string? DeclineReason, DateTimeOffset CreatedAt, DateTimeOffset? RefundedAt);

internal static class PaymentMappings
{
    public static PaymentDto ToDto(this Payment payment) =>
        new(payment.EnrollmentId, payment.CourseId, payment.AmountInCents, Payment.Currency, payment.Status.ToString(),
            payment.DeclineReason, payment.CreatedAt, payment.RefundedAt);
}

/// <summary>
/// Charges a student for an enrollment (saga step). Idempotent on the enrollment id: a retried call returns the
/// payment already made, or the decline already received, and never charges twice. If the charge succeeded but
/// could not be stored, the retry asks the provider again with the same idempotency key and gets the same charge.
/// </summary>
[Export, Scoped]
public sealed class ChargeEnrollment(IPaymentRepository payments, IPaymentProvider provider, TimeProvider timeProvider)
{
    public async Task<Result<PaymentDto>> ExecuteAsync(ChargeEnrollmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await payments.GetAsync(command.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Outcome(existing);
        }

        var valid = Payment.Validate(command.EnrollmentId, command.StudentId, command.CourseId, command.AmountInCents);
        if (valid.IsFailed)
        {
            return valid.ToResult<PaymentDto>();
        }

        var charge = await provider.ChargeAsync(command.EnrollmentId, command.AmountInCents, Payment.Currency, cancellationToken).ConfigureAwait(false);
        if (charge.IsFailed)
        {
            return charge.ToResult<PaymentDto>(); // provider unreachable: transient, the saga retries
        }

        // A decline is stored too: the decision is final, a retry of the saga step must not try the card again.
        var now = timeProvider.GetUtcNow();
        var payment = charge.Value is { Accepted: true, Reference: { } reference }
            ? Payment.Captured(command.EnrollmentId, command.StudentId!, command.CourseId, command.AmountInCents, reference, now)
            : Payment.Declined(command.EnrollmentId, command.StudentId!, command.CourseId, command.AmountInCents,
                               charge.Value.DeclineReason ?? "declined", now);

        if (!await payments.AddIfAbsentAsync(payment.Value, cancellationToken).ConfigureAwait(false))
        {
            // A concurrent attempt stored its outcome first (same idempotency key at the provider): use it.
            existing = await payments.GetAsync(command.EnrollmentId, cancellationToken).ConfigureAwait(false);
            return existing is null ? Result.Fail<PaymentDto>(PaymentErrors.ConcurrencyConflict(command.EnrollmentId)) : Outcome(existing);
        }

        return Outcome(payment.Value);
    }

    private static Result<PaymentDto> Outcome(Payment payment) => payment.Status switch
    {
        PaymentStatus.Captured => Result.Ok(payment.ToDto()),
        PaymentStatus.Declined => Result.Fail<PaymentDto>(PaymentErrors.Declined(payment.EnrollmentId, payment.DeclineReason ?? "declined")),
        _ => Result.Fail<PaymentDto>(PaymentErrors.AlreadyRefunded(payment.EnrollmentId)),
    };
}

/// <summary>
/// Gives the money of an enrollment back (saga compensation). Safe to replay: no payment, a declined one or an
/// already refunded one all succeed without doing anything.
/// </summary>
[Export, Scoped]
public sealed class RefundEnrollment(IPaymentRepository payments, IPaymentProvider provider, TimeProvider timeProvider)
{
    public async Task<Result> ExecuteAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (payment is not { Status: PaymentStatus.Captured, ProviderReference: { } reference })
        {
            return Result.Ok();
        }

        var refunded = await provider.RefundAsync(reference, enrollmentId, cancellationToken).ConfigureAwait(false);
        if (refunded.IsFailed)
        {
            return refunded;
        }

        payment.MarkRefunded(timeProvider.GetUtcNow());
        var saved = await payments.SaveAsync(payment, cancellationToken).ConfigureAwait(false);
        // Lost the race to a concurrent refund of the same payment: the money is back either way.
        return saved.IsFailed && saved.Errors.HasCode(PaymentErrors.ConcurrencyConflictCode) ? Result.Ok() : saved;
    }
}

/// <summary>The current user's latest payments.</summary>
[Export, Scoped]
public sealed class ListMyPayments(IPaymentRepository payments)
{
    public const int MaxCount = 50;

    public async Task<Result<IReadOnlyList<PaymentDto>>> ExecuteAsync(string studentId, CancellationToken cancellationToken)
    {
        var items = await payments.ListForStudentAsync(studentId, MaxCount, cancellationToken).ConfigureAwait(false);
        return Result.Ok<IReadOnlyList<PaymentDto>>([.. items.Select(p => p.ToDto())]);
    }
}
