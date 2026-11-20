namespace MarketingStudio.Domain.Users;

/// <summary>
/// Permission names used by API policies and returned to the portal (so it can hide actions).
/// Endpoints check permissions, never role names (Security Architecture.md → Authorization).
/// The role → permission map below is the code form of RBAC Matrix.md.
/// </summary>
public static class Permissions
{
    public const string UsersManage = "users.manage";
    public const string BillingManage = "billing.manage";
    public const string UsageRead = "usage.read";
    public const string ChannelsManage = "channels.manage";
    public const string BrandsRead = "brands.read";
    public const string BrandsWrite = "brands.write";
    public const string BrandContentWrite = "brands.content.write";
    public const string ProductsRead = "products.read";
    public const string ProductsWrite = "products.write";
    public const string KnowledgeWrite = "knowledge.write";
    public const string CampaignsRead = "campaigns.read";
    public const string CampaignsWrite = "campaigns.write";
    public const string AgentsRead = "agents.read";
    public const string AgentsExecute = "agents.execute";
    public const string AgentsManage = "agents.manage";
    public const string ContentSubmit = "content.submit";
    public const string ContentApprove = "content.approve";
    public const string PostsSchedule = "posts.schedule";
    public const string AnalyticsRead = "analytics.read";
    public const string InboxRead = "inbox.read";
    public const string InboxReply = "inbox.reply";
    public const string PlatformAdmin = "platform.admin";

    public static readonly IReadOnlyList<string> All =
    [
        UsersManage, BillingManage, UsageRead, ChannelsManage, BrandsRead, BrandsWrite, BrandContentWrite,
        ProductsRead, ProductsWrite, KnowledgeWrite, CampaignsRead, CampaignsWrite, AgentsRead, AgentsExecute,
        AgentsManage, ContentSubmit, ContentApprove, PostsSchedule, AnalyticsRead, InboxRead, InboxReply,
        PlatformAdmin,
    ];

    private static readonly string[] TenantAdmin = [.. All.Where(p => p != PlatformAdmin)];

    private static readonly string[] MarketingManager =
    [
        UsageRead, BrandsRead, BrandsWrite, BrandContentWrite, ProductsRead, ProductsWrite, KnowledgeWrite,
        CampaignsRead, CampaignsWrite, AgentsRead, AgentsExecute, ContentSubmit, ContentApprove, PostsSchedule,
        AnalyticsRead, InboxRead, InboxReply,
    ];

    private static readonly string[] ContentManager =
    [
        BrandsRead, BrandContentWrite, ProductsRead, KnowledgeWrite, CampaignsRead, AgentsRead, AgentsExecute,
        ContentSubmit, AnalyticsRead, InboxRead,
    ];

    private static readonly string[] Viewer =
    [
        BrandsRead, ProductsRead, CampaignsRead, AgentsRead, AnalyticsRead,
    ];

    private static readonly Dictionary<string, string[]> ByRole = new(StringComparer.Ordinal)
    {
        [Roles.SuperAdmin] = [.. All],
        [Roles.TenantAdmin] = TenantAdmin,
        [Roles.MarketingManager] = MarketingManager,
        [Roles.ContentManager] = ContentManager,
        [Roles.Viewer] = Viewer,
    };

    public static IReadOnlyList<string> ForRoles(IEnumerable<string> roles) =>
        roles.SelectMany(r => ByRole.TryGetValue(r, out var p) ? p : []).Distinct().Order(StringComparer.Ordinal).ToList();

    public static bool RolesHave(IEnumerable<string> roles, string permission) =>
        roles.Any(r => ByRole.TryGetValue(r, out var p) && p.Contains(permission));
}
