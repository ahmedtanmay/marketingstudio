using MarketingStudio.Application.Abstractions;
using MediatR;

namespace MarketingStudio.Application.Onboarding;

/// <summary>
/// TEN-1: onboarding checklist, computed from real data so steps tick themselves. Steps for later
/// sprints (knowledge, channels, first post) report false until those features exist.
/// </summary>
public sealed record OnboardingStatus(
    bool WorkspaceCreated,
    bool EmailVerified,
    bool BrandCreated,
    bool ProductAdded,
    bool TeamInvited,
    bool KnowledgeUploaded,
    bool ChannelConnected,
    bool FirstPostGenerated);

public sealed record GetOnboardingQuery : IRequest<OnboardingStatus>;

public sealed class GetOnboardingQueryHandler(
    ITenantContext context,
    IUserRepository users,
    IInviteRepository invites,
    IBrandRepository brands,
    IProductRepository products,
    IKnowledgeDocumentRepository knowledgeDocuments,
    IFaqRepository faqs,
    TimeProvider clock) : IRequestHandler<GetOnboardingQuery, OnboardingStatus>
{
    public async Task<OnboardingStatus> Handle(GetOnboardingQuery request, CancellationToken cancellationToken)
    {
        var me = await users.GetByIdAsync(context.RequireUserId(), cancellationToken) ?? throw new UnauthenticatedException();
        var teamInvited = await users.CountActiveAsync(cancellationToken) > 1
            || await invites.CountOpenAsync(clock.GetUtcNow().UtcDateTime, cancellationToken) > 0;

        return new OnboardingStatus(
            WorkspaceCreated: true,
            EmailVerified: me.EmailConfirmed,
            BrandCreated: await brands.CountAsync(cancellationToken) > 0,
            ProductAdded: await products.CountAsync(cancellationToken) > 0,
            TeamInvited: teamInvited,
            KnowledgeUploaded: await knowledgeDocuments.CountReadyAsync(cancellationToken) > 0 || await faqs.CountAsync(cancellationToken) > 0,
            ChannelConnected: false,
            FirstPostGenerated: false);
    }
}
