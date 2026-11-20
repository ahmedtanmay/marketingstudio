using FluentAssertions;
using MarketingStudio.Domain.Users;
using MarketingStudio.Infrastructure.Identity;

namespace MarketingStudio.UnitTests.Infrastructure;

public class PasswordPolicyTests
{
    [Theory]
    [Trait("Story", "AUTH-1")]
    [InlineData("password123")]
    [InlineData("PASSWORD123")]
    [InlineData("1234567890")]
    [InlineData("qwertyuiop")]
    public async Task Common_passwords_are_rejected(string password)
    {
        var result = await new CommonPasswordValidator().ValidateAsync(null!, new User(), password);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == CommonPasswordValidator.ErrorCode);
    }

    [Fact]
    [Trait("Story", "AUTH-1")]
    public async Task Uncommon_passwords_pass()
    {
        var result = await new CommonPasswordValidator().ValidateAsync(null!, new User(), "violet-harbour-lantern");

        result.Succeeded.Should().BeTrue();
    }
}
