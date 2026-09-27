namespace Blazor_WebAssembly.Pages;

/// <summary>
/// The limits rippled's <c>LoanSet::preflight</c> puts on a loan's terms, which it answers with
/// <c>temINVALID</c>. Checked before a preview or a submission so the page can name the rule.
/// </summary>
public static class LoanTermsRules
{
    public static string? Problem(
        decimal principal,
        decimal interestPercent,
        uint paymentTotal,
        uint paymentInterval,
        uint gracePeriod,
        decimal originationFee,
        decimal serviceFee)
    {
        if (principal <= 0)
            return "The principal must be above zero.";
        if (interestPercent < 0 || interestPercent > 100)
            return "The interest rate must be between 0% and 100% a year.";
        if (paymentTotal == 0)
            return "A loan needs at least one payment.";
        if (paymentInterval < 60)
            return "The payment interval must be at least 60 seconds.";
        if (gracePeriod < 60 || gracePeriod > paymentInterval)
            return $"The grace period must be between 60 seconds and the payment interval ({paymentInterval} s); it is {gracePeriod} s.";
        if (originationFee < 0 || originationFee > principal)
            return "The origination fee must be between zero and the principal.";
        if (serviceFee < 0)
            return "The service fee cannot be negative.";

        return null;
    }
}
