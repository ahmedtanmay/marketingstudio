using System.Reflection;
using FluentAssertions;

namespace MarketingStudio.UnitTests;

/// <summary>
/// Keeps Clean Architecture dependencies pointing inward, and keeps database clients out of the
/// Domain and Application layers (Multi-Tenant Design.md → Enforcement and Guard Rails).
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(MarketingStudio.Domain.Users.User).Assembly;
    private static readonly Assembly Application = typeof(MarketingStudio.Application.DependencyInjection).Assembly;

    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    [Fact]
    public void Domain_depends_on_nothing_but_the_runtime() =>
        References(Domain).Should().OnlyContain(name => name.StartsWith("System", StringComparison.Ordinal));

    [Theory]
    [InlineData("MongoDB")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("MarketingStudio.Infrastructure")]
    [InlineData("Qdrant")]
    [InlineData("Minio")]
    public void Application_does_not_reference_infrastructure_or_web(string forbiddenPrefix) =>
        References(Application).Should().NotContain(name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
}
