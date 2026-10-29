using System.Security.Claims;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
namespace ITTicketIntelligence.Api.Security;
public sealed class KnowledgeAccessContext(IHttpContextAccessor accessor):IKnowledgeAccessContext
{
    public bool CanSearch=>accessor.HttpContext?.User is { Identity.IsAuthenticated:true } p && (p.IsInRole("Admin")||p.IsInRole("Agent")||p.IsInRole("Support")||p.HasClaim("mcp.permission","knowledge.read"));
    public bool IsAdmin=>accessor.HttpContext?.User.IsInRole("Admin")==true;
    public Guid? UserId=>Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:null;
}
