using ClassHub.Core.Modules.Offers.Application;
using ClassHub.Core.Modules.Offers.Contracts;

namespace ClassHub.UnitTests.Offers;

public sealed class OfferRulesTests
{
    private static readonly SaveOfferRequestValidator Validator = new();

    private static SaveOfferRequest Valid(string kind = "SingleLesson", int? lessons = null, int? seats = null, long price = 150_000) =>
        new(kind, "Час по математика", null, Guid.NewGuid(), 60, price, null, "Online", lessons, seats, true, true);

    [Theory]
    [InlineData("SingleLesson", null, null)]
    [InlineData("Package", 10, null)]
    [InlineData("GroupProgram", null, 8)]
    public void Each_kind_has_its_own_valid_shape(string kind, int? lessons, int? seats) =>
        Validator.Validate(Valid(kind, lessons, seats)).IsValid.ShouldBeTrue();

    [Fact]
    public void The_price_cannot_be_negative_but_can_be_free()
    {
        Validator.Validate(Valid(price: -1)).Errors.ShouldContain(e => e.PropertyName == "PriceMinor");
        Validator.Validate(Valid(price: 0)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Package", null, null, "LessonCount")]  // a package needs lessons
    [InlineData("Package", 0, null, "LessonCount")]
    [InlineData("GroupProgram", null, null, "Seats")]   // a group needs seats (capacity > 0)
    [InlineData("GroupProgram", null, 0, "Seats")]
    [InlineData("SingleLesson", 5, null, "LessonCount")] // only packages have lessons
    [InlineData("SingleLesson", null, 4, "Seats")]       // only groups have seats
    [InlineData("Package", 5, 4, "Seats")]
    public void Kind_specific_fields_are_required_or_forbidden(string kind, int? lessons, int? seats, string field) =>
        Validator.Validate(Valid(kind, lessons, seats)).Errors.ShouldContain(e => e.PropertyName == field);

    [Theory]
    [InlineData("Webinar")]
    [InlineData("1")]
    [InlineData("package")]
    public void Unknown_kinds_are_rejected(string kind) =>
        Validator.Validate(Valid() with { Kind = kind }).Errors.ShouldContain(e => e.PropertyName == "Kind");

    [Fact]
    public void Durations_titles_modes_and_currencies_are_checked()
    {
        Validator.Validate(Valid() with { DurationMinutes = 10 }).IsValid.ShouldBeFalse();
        Validator.Validate(Valid() with { DurationMinutes = 601 }).IsValid.ShouldBeFalse();
        Validator.Validate(Valid() with { Title = "   " }).IsValid.ShouldBeFalse();
        Validator.Validate(Valid() with { Mode = "Telepathy" }).IsValid.ShouldBeFalse();
        Validator.Validate(Valid() with { Currency = "ABC" }).IsValid.ShouldBeFalse();
        Validator.Validate(Valid() with { Currency = "EUR" }).IsValid.ShouldBeTrue();
    }
}
