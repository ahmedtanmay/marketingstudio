using FluentAssertions;
using FluentValidation;
using MarketingStudio.Application.Abstractions;
using MarketingStudio.Application.Auth;
using MarketingStudio.Domain.Audit;
using MarketingStudio.Domain.Users;
using NSubstitute;

namespace MarketingStudio.UnitTests.Application;

public class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IAccessTokenService _accessTokens = Substitute.For<IAccessTokenService>();
    private readonly IRefreshTokenService _refreshTokens = Substitute.For<IRefreshTokenService>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly IClientInfo _client = Substitute.For<IClientInfo>();

    private LoginCommandHandler Handler() =>
        new(_identity, _accessTokens, _refreshTokens, _audit, _client, new FixedTimeProvider(Now));

    private static User AnUser() => User.Create("t1", "ana@example.com", "Ana", Roles.TenantAdmin, Now.UtcDateTime);

    [Fact]
    [Trait("Story", "AUTH-2")]
    public async Task Unknown_email_gets_the_generic_error_and_is_audited()
    {
        var act = () => Handler().Handle(new LoginCommand("nobody@example.com", "whatever-pass"), default);

        await act.Should().ThrowAsync<UnauthenticatedException>().WithMessage(LoginCommandHandler.GenericFailure);
        await _audit.Received(1).WriteAsync(
            Arg.Is<AuditEntry>(e => e.Action == AuditActions.SignInFailed), Arg.Any<CancellationToken>());
    }

    [Theory]
    [Trait("Story", "AUTH-2")]
    [InlineData(PasswordSignInResult.Failed, AuditActions.SignInFailed)]
    [InlineData(PasswordSignInResult.LockedOut, AuditActions.AccountLockedOut)]
    [InlineData(PasswordSignInResult.NotAllowed, AuditActions.SignInFailed)]
    public async Task Every_failure_gets_the_same_generic_error(PasswordSignInResult outcome, string auditAction)
    {
        var user = AnUser();
        _identity.FindByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _identity.CheckPasswordSignInAsync(user, "wrong-password", Arg.Any<CancellationToken>()).Returns(outcome);

        var act = () => Handler().Handle(new LoginCommand(user.Email, "wrong-password"), default);

        await act.Should().ThrowAsync<UnauthenticatedException>().WithMessage(LoginCommandHandler.GenericFailure);
        await _audit.Received(1).WriteAsync(
            Arg.Is<AuditEntry>(e => e.Action == auditAction && e.ActorUserId == user.Id), Arg.Any<CancellationToken>());
        await _refreshTokens.DidNotReceiveWithAnyArgs().IssueAsync(default!, default);
    }

    [Fact]
    [Trait("Story", "AUTH-2")]
    public async Task Successful_sign_in_returns_both_tokens()
    {
        var user = AnUser();
        _identity.FindByEmailAsync(user.Email, Arg.Any<CancellationToken>()).Returns(user);
        _identity.CheckPasswordSignInAsync(user, "right-password", Arg.Any<CancellationToken>())
            .Returns(PasswordSignInResult.Succeeded);
        _accessTokens.CreateAccessToken(user).Returns(new AccessToken("access", Now.UtcDateTime.AddMinutes(15)));
        _refreshTokens.IssueAsync(user, Arg.Any<CancellationToken>())
            .Returns(new IssuedRefreshToken("refresh", Now.UtcDateTime.AddDays(7)));

        var result = await Handler().Handle(new LoginCommand(user.Email, "right-password"), default);

        result.AccessToken.Should().Be("access");
        result.RefreshToken.Should().Be("refresh");
        result.User.TenantId.Should().Be("t1");
        await _identity.Received(1).RecordSuccessfulSignInAsync(user, Arg.Any<CancellationToken>());
    }
}

public class RegisterCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly IAppLinks _links = Substitute.For<IAppLinks>();

    private RegisterCommandHandler Handler() => new(
        _identity,
        Substitute.For<ITenantRepository>(),
        Substitute.For<ISubscriptionRepository>(),
        _unitOfWork,
        _email,
        _links,
        Substitute.For<IAuditLog>(),
        Substitute.For<IClientInfo>(),
        new FixedTimeProvider(Now));

    [Fact]
    [Trait("Story", "AUTH-1")]
    public async Task Existing_email_creates_nothing_and_notifies_the_owner()
    {
        var existing = User.Create("t1", "ana@example.com", "Ana", Roles.TenantAdmin, Now.UtcDateTime);
        _identity.FindByEmailAsync("ana@example.com", Arg.Any<CancellationToken>()).Returns(existing);

        await Handler().Handle(new RegisterCommand("Someone", "ana@example.com", "another-password", "Other Co"), default);

        await _unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync(default!, default);
        await _email.Received(1).SendAsync(
            Arg.Is<EmailMessage>(m => m.To == "ana@example.com" && m.Subject.Contains("Sign-up attempt")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Story", "AUTH-1")]
    public async Task Password_policy_violations_are_validation_errors()
    {
        _identity.ValidatePasswordAsync("password123").Returns(["This password is too common."]);

        var act = () => Handler().Handle(new RegisterCommand("Ana", "new@example.com", "password123", "Acme"), default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "Password");
        await _unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync(default!, default);
    }
}

public class ValidatorTests
{
    [Theory]
    [Trait("Story", "AUTH-1")]
    [InlineData("", "ana@example.com", "long-enough-pass", "Acme", "Name")]
    [InlineData("Ana", "not-an-email", "long-enough-pass", "Acme", "Email")]
    [InlineData("Ana", "ana@example.com", "short", "Acme", "Password")]
    [InlineData("Ana", "ana@example.com", "long-enough-pass", "", "CompanyName")]
    public void Register_rejects_invalid_input(string name, string email, string password, string company, string field)
    {
        var result = new RegisterCommandValidator().Validate(new RegisterCommand(name, email, password, company));

        result.Errors.Should().Contain(e => e.PropertyName == field);
    }

    [Fact]
    public void Register_accepts_valid_input() =>
        new RegisterCommandValidator()
            .Validate(new RegisterCommand("Ana", "ana@example.com", "violet-harbour-lantern", "Acme"))
            .IsValid.Should().BeTrue();
}
