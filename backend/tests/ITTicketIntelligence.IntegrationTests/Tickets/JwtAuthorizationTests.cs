using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ITTicketIntelligence.Application.Realtime;
using ITTicketIntelligence.Application.Tickets.DTOs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Infrastructure.Persistence;
using ITTicketIntelligence.Infrastructure;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace ITTicketIntelligence.IntegrationTests.Tickets;
[Collection("Postgres")]
public sealed class JwtAuthorizationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task FailedLoginsLockTheAccountAndAdminPasswordResetRestoresAccess()
    {
        var isolated = new PostgresFixture();
        await isolated.InitializeAsync();
        try
        {
        var actor = Guid.NewGuid();
        await using (var db = isolated.CreateContext())
        {
            db.Users.Add(new User(actor, "Credential test administrator", ITTicketIntelligence.Domain.ValueObjects.EmailAddress.Create(actor + "@example.test"), UserRole.Admin, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddInfrastructure(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:Postgres"] = isolated.ConnectionString }).Build());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IUserSessions>();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var email = Guid.NewGuid() + "@example.test";
        var account = await sessions.CreateAsync(actor, "Credential test user", email, password, UserRole.User, default);
        var initial = await sessions.LoginAsync(email, password, default);
        Assert.NotNull(initial);
        for (var attempt = 0; attempt < 5; attempt++) Assert.Null(await sessions.LoginAsync(email, "incorrect-password", default));
        Assert.Null(await sessions.LoginAsync(email, password, default));
        var replacement = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await sessions.PasswordAsync(actor, account.Id, replacement, default);
        Assert.Null(await sessions.LoginAsync(email, password, default));
        var restored = await sessions.LoginAsync(email, replacement, default);
        Assert.NotNull(restored);
        Assert.NotEqual(initial.Version, restored.Version);
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task JwtRolesOwnershipForgeryAndRevocationAreEnforced()
    {
        var previous = Environment.GetEnvironmentVariable("JWT__SigningKey");
        Environment.SetEnvironmentVariable("JWT__SigningKey",Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        try
        {
            await using var host = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(b=>{ b.UseEnvironment("Testing"); b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Postgres"]=fixture.ConnectionString})); });
            var email=Guid.NewGuid().ToString("N")+"@example.test";var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            using(var scope=host.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IUserSessions>().BootstrapAsync(email,password,default);
            using var admin=host.CreateClient();
            async Task<string> Login(HttpClient client,string address,string secret) { using var response=await client.PostAsJsonAsync("/api/auth/login",new{email=address,password=secret}); Assert.Equal(HttpStatusCode.OK,response.StatusCode); var data=await response.Content.ReadFromJsonAsync<JsonElement>();var token=data.GetProperty("accessToken").GetString()!;Assert.Equal(3,token.Split('.').Length);client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return token; }
            var adminToken = await Login(admin,email,password);
            Assert.False(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(adminToken).Payload.ContainsKey("exp"));
            // A valid signed session issued a month ago must still be accepted before revocation.
            var original = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(adminToken);
            var aged = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("it-ticket-intelligence","it-ticket-dashboard",original.Claims.Where(c=>c.Type is "session.user" or "session.version" or "jti"),DateTime.UtcNow.AddDays(-30),null,new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT__SigningKey")!)),Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
            using var longstanding = host.CreateClient();
            longstanding.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(aged));
            Assert.Equal(HttpStatusCode.OK,(await longstanding.GetAsync("/api/tickets")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("/api/admin/users")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("/api/admin/teams")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync("/api/admin/audit")).StatusCode);
            using var anonymous=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/tickets")).StatusCode);
            var actors=new Dictionary<string,(HttpClient client,Guid id,string token)>();
            foreach(var role in new[]{"User","Agent","Support"})
            {
                var address=Guid.NewGuid().ToString("N")+"@example.test";
                using var response=await admin.PostAsJsonAsync("/api/admin/users",new{name=role,email=address,password,role}); Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
                var id=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var client=host.CreateClient();var token=await Login(client,address,password);actors[role]=(client,id,token);
            }
            var user=actors["User"];using var created=await user.client.PostAsJsonAsync("/api/tickets",new{title="Owned JWT test",description="Private reported issue",createdById=Guid.NewGuid()});Assert.Equal(HttpStatusCode.Created,created.StatusCode);
            var ticket=await created.Content.ReadFromJsonAsync<JsonElement>();var ticketId=ticket.GetProperty("id").GetGuid();Assert.Equal(user.id,ticket.GetProperty("createdById").GetGuid());
            Assert.Equal(HttpStatusCode.OK,(await user.client.GetAsync($"/api/tickets/{ticketId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/tickets?access_token="+user.token)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.client.PostAsync($"/api/tickets/{ticketId}/analyze",null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.client.GetAsync("/api/knowledge")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.client.GetAsync("/api/admin/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await actors["Agent"].client.GetAsync($"/api/tickets/{ticketId}")).StatusCode);
            var listed=await actors["Agent"].client.GetFromJsonAsync<JsonElement>("/api/tickets");Assert.Equal(0,listed.GetProperty("totalCount").GetInt32());
            var team=Guid.NewGuid();await using(var db=fixture.CreateContext()){var entity=new Team(team,"JWT "+team,DateTimeOffset.UtcNow);entity.AddMember(actors["Agent"].id,DateTimeOffset.UtcNow);db.Teams.Add(entity);await db.SaveChangesAsync();}
            using var assigned=await actors["Support"].client.PatchAsJsonAsync($"/api/tickets/{ticketId}/assignment",new{teamId=team,revision=ticket.GetProperty("revision").GetInt32()});Assert.Equal(HttpStatusCode.OK,assigned.StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await actors["Agent"].client.GetAsync($"/api/tickets/{ticketId}")).StatusCode);
            await using var hub = new HubConnectionBuilder().WithUrl(new Uri(host.Server.BaseAddress,"/hubs/tickets"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(actors["Agent"].token);
            }).AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter())).Build();
            var predictionEvent = new TaskCompletionSource<TicketEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            var forbiddenEvent = new TaskCompletionSource<TicketEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.On<TicketEvent>("PredictionCompleted", e => predictionEvent.TrySetResult(e));
            hub.On<TicketEvent>("CommentAdded", e => forbiddenEvent.TrySetResult(e));
            await hub.StartAsync();
            await hub.InvokeAsync<TicketDto>("JoinTicket",ticketId);
            Assert.Equal(HttpStatusCode.OK,(await actors["Agent"].client.PostAsync($"/api/tickets/{ticketId}/analyze",null)).StatusCode);
            Assert.Equal(ticketId,(await predictionEvent.Task.WaitAsync(TimeSpan.FromSeconds(10))).TicketId);
            using(var negotiate=new HttpRequestMessage(HttpMethod.Post,"/hubs/tickets/negotiate?negotiateVersion=1")){negotiate.Headers.Authorization=new AuthenticationHeaderValue("Bearer",actors["Agent"].token);Assert.Equal(HttpStatusCode.OK,(await actors["Agent"].client.SendAsync(negotiate)).StatusCode);}
            System.Security.Claims.ClaimsPrincipal stalePrincipal; using(var snapshotScope=host.Services.CreateScope()) stalePrincipal=Api.Security.SessionSecurity.Principal((await snapshotScope.ServiceProvider.GetRequiredService<IUserSessions>().FindAsync(actors["Agent"].id,default))!);
            using(var scope=host.Services.CreateScope()){
                var access=scope.ServiceProvider.GetRequiredService<IUserSessions>();Assert.True(await Api.Security.LiveConnections.CanRead(stalePrincipal,ticketId,access,default));
                var current = (await access.FindAsync(actors["Agent"].id,default))!;
                Assert.False(await Api.Security.LiveConnections.CanRead(Api.Security.SessionSecurity.Principal(current,DateTimeOffset.UtcNow.AddSeconds(-1)),ticketId,access,default));
            }
            using(var removed=await admin.DeleteAsync($"/api/admin/teams/{team}/members/{actors["Agent"].id}"))Assert.Equal(HttpStatusCode.NoContent,removed.StatusCode);
            using(var scope=host.Services.CreateScope())Assert.False(await Api.Security.LiveConnections.CanRead(stalePrincipal,ticketId,scope.ServiceProvider.GetRequiredService<IUserSessions>(),default));
            var latest = await user.client.GetFromJsonAsync<JsonElement>($"/api/tickets/{ticketId}");
            using var comment = await user.client.PostAsJsonAsync($"/api/tickets/{ticketId}/comments",new { authorId=user.id, message="Visible only to current authorized subscribers", revision=latest.GetProperty("revision").GetInt32() });
            Assert.Equal(HttpStatusCode.Created,comment.StatusCode);
            await Assert.ThrowsAsync<TimeoutException>(async()=>await forbiddenEvent.Task.WaitAsync(TimeSpan.FromMilliseconds(500)));
            using(var mcp=new HttpRequestMessage(HttpMethod.Post,"/mcp")){
                mcp.Headers.Accept.ParseAdd("application/json");mcp.Headers.Accept.ParseAdd("text/event-stream");mcp.Headers.Add("MCP-Protocol-Version","2025-11-25");mcp.Headers.Authorization=new AuthenticationHeaderValue("Bearer",user.token);
                mcp.Content=JsonContent.Create(new{jsonrpc="2.0",id=1,method="tools/list",@params=new{}});
                using var response=await user.client.SendAsync(mcp);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.DoesNotContain("get_system_status",await response.Content.ReadAsStringAsync());
            }
            using var forged=host.CreateClient();forged.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",user.token[..^10]+"invalid000");Assert.Equal(HttpStatusCode.Unauthorized,(await forged.GetAsync("/api/tickets")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await user.client.PostAsync("/api/auth/logout",null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await user.client.GetAsync("/api/tickets")).StatusCode);
            using var changed=await admin.PatchAsJsonAsync($"/api/admin/users/{actors["Agent"].id}",new{role="User",isActive=true});Assert.Equal(HttpStatusCode.NoContent,changed.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await actors["Agent"].client.GetAsync($"/api/tickets/{ticketId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await admin.PostAsync("/api/auth/logout",null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await longstanding.GetAsync("/api/tickets")).StatusCode);
            foreach(var actor in actors.Values)actor.client.Dispose();
        }
        finally{Environment.SetEnvironmentVariable("JWT__SigningKey",previous);}
    }
}
