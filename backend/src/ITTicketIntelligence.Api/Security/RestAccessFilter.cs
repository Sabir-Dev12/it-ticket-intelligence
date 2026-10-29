using System.Security.Claims;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Application.Tickets.Queries;
using ITTicketIntelligence.Api.Contracts.Tickets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace ITTicketIntelligence.Api.Security;
public sealed class RestAccessFilter(IUserSessions sessions) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext c, ActionExecutionDelegate next)
    {
        var path = (c.HttpContext.Request.Path.Value ?? "").TrimEnd('/').ToLowerInvariant();
        c.HttpContext.Response.Headers.CacheControl = "no-store";
        if (path.StartsWith("/api/auth/")) { await next(); return; }
        var principal = c.HttpContext.User;
        if (!Guid.TryParse(principal.FindFirstValue("session.user"), out var id)) { c.Result = new UnauthorizedResult(); return; }
        var user = await sessions.FindAsync(id, c.HttpContext.RequestAborted);
        if (user is null) { c.Result = new UnauthorizedResult(); return; }
        var manage = user.Role is Domain.Enums.UserRole.Admin or Domain.Enums.UserRole.Support;
        var ai = user.Role != Domain.Enums.UserRole.User;
        var read = HttpMethods.IsGet(c.HttpContext.Request.Method);
        var allowed = user.Role == Domain.Enums.UserRole.Admin || path.StartsWith("/api/tickets") || path.StartsWith("/api/assistant") && ai || path.StartsWith("/api/ai/chat") && ai || path.StartsWith("/api/predictions") && ai;
        if (path.StartsWith("/api/knowledge") && read && ai) allowed=true;
        if (path.StartsWith("/api/tickets"))
        {
            if (c.ActionArguments.Values.OfType<GetTicketsRequest>().FirstOrDefault() is { } filter) c.HttpContext.Items["allowedTickets"] = manage ? null : user.TicketIds;
            var ticketId = c.ActionArguments.TryGetValue("id", out var value) && value is Guid g ? g : c.ActionArguments.TryGetValue("ticketId", out value) && value is Guid h ? h : (Guid?)null;
            if (ticketId.HasValue && !manage && !user.TicketIds.Contains(ticketId.Value)) { await sessions.AuditAsync(id, "Authorization.TicketDenied." + ticketId, c.HttpContext.RequestAborted); c.Result = new NotFoundResult(); return; }
            if (!read)
            {
                var create = path == "/api/tickets";
                var comment = path.EndsWith("/comments"); var analyze = path.EndsWith("/analyze") || path.EndsWith("/investigate");
                allowed = create ? user.Role is Domain.Enums.UserRole.User or Domain.Enums.UserRole.Admin or Domain.Enums.UserRole.Support : comment ? true : analyze ? ai : path.EndsWith("/attachments") && HttpMethods.IsPost(c.HttpContext.Request.Method) ? true : manage;
                if (create && c.ActionArguments["request"] is CreateTicketRequest request) { if (!manage && request.AssignedTeamId.HasValue) allowed=false; c.ActionArguments["request"] = request with { CreatedById = id }; }
                if (comment && c.ActionArguments["request"] is AddTicketCommentRequest input) c.ActionArguments["request"] = input with { AuthorId = id };
            }
        }
        if (!allowed) { await sessions.AuditAsync(id, "Authorization.Denied", c.HttpContext.RequestAborted); c.Result = new ForbidResult(); return; }
        var result = await next();
        if (!read && result.Exception is null && result.Result is not ObjectResult { StatusCode: >= 400 }) await sessions.AuditAsync(id, "Authorized." + c.ActionDescriptor.RouteValues["controller"] + "." + c.ActionDescriptor.RouteValues["action"], c.HttpContext.RequestAborted);
    }
}
