using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.SharedKernel.Errors;

namespace ClassHub.UnitTests.Platform;

public sealed class ErrorAndValidationTests
{
    [Theory]
    [InlineData(400, ErrorCodes.BadRequest)]
    [InlineData(401, ErrorCodes.Unauthorized)]
    [InlineData(403, ErrorCodes.Forbidden)]
    [InlineData(404, ErrorCodes.NotFound)]
    [InlineData(409, ErrorCodes.Conflict)]
    [InlineData(422, ErrorCodes.BusinessRuleViolation)]
    [InlineData(429, ErrorCodes.RateLimited)]
    [InlineData(500, ErrorCodes.InternalError)]
    [InlineData(503, ErrorCodes.InternalError)]
    public void Status_codes_map_to_stable_error_codes(int status, string code)
    {
        ErrorCodes.ForStatus(status).ShouldBe(code);
    }

    [Fact]
    public void Exceptions_carry_status_and_code()
    {
        new NotFoundException().StatusCode.ShouldBe(404);
        new ConflictException("x").StatusCode.ShouldBe(409);
        new BusinessRuleException("x", "PLAN_LIMIT_REACHED").Code.ShouldBe("PLAN_LIMIT_REACHED");
        new ForbiddenOperationException().StatusCode.ShouldBe(403);
    }

    [Theory]
    [InlineData("Name", "name")]
    [InlineData("Items[0].DisplayName", "items[0].displayName")]
    [InlineData("Address.PostalCode", "address.postalCode")]
    [InlineData("", "")]
    public void Validation_property_paths_become_camel_case(string input, string expected)
    {
        ValidationFilter<object>.ToCamelCasePath(input).ShouldBe(expected);
    }
}
