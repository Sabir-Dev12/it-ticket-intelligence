using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Application.Security;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ITTicketIntelligence.IntegrationTests.LanguageModels;

[Collection("Postgres")]
public sealed class AIChatApiTests
{
    [Fact]public async Task JwtEndpointsPersistPrivateHistoryAndFrameCompletedAndFailedStreams()
    {
        var previous=Environment.GetEnvironmentVariable("JWT__SigningKey");
        Environment.SetEnvironmentVariable("JWT__SigningKey",Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        var database=new PostgresFixture();await database.InitializeAsync();
        try
        {
            var fake=new FakeChat();
            await using var host=new WebApplicationFactory<Api.Program>().WithWebHostBuilder(b=>
            {
                b.UseEnvironment("Testing");b.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Postgres"]=database.ConnectionString}));
                b.ConfigureServices(s=>{s.RemoveAll<IAIChatService>();s.AddSingleton<IAIChatService>(fake);});
            });
            var email=Guid.NewGuid()+"@example.test";var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            using(var scope=host.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<IUserSessions>().BootstrapAsync(email,password,default);
            using var caller=host.CreateClient();
            async Task Login(HttpClient client,string address){var response=await client.PostAsJsonAsync("/api/auth/login",new{email=address,password});Assert.Equal(HttpStatusCode.OK,response.StatusCode);var body=await response.Content.ReadFromJsonAsync<JsonElement>();client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",body.GetProperty("accessToken").GetString());}
            await Login(caller,email);
            using var anonymous=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/ai/chat/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await caller.GetAsync("/api/ai/chat/health")).StatusCode);
            var created=await caller.PostAsJsonAsync("/api/ai/chat/conversations",new{title="Private chat"});Assert.Equal(HttpStatusCode.Created,created.StatusCode);
            var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var root=$"/api/ai/chat/conversations/{id}";
            var page=await caller.GetFromJsonAsync<JsonElement>("/api/ai/chat/conversations");Assert.Equal(1,page.GetProperty("items").GetArrayLength());
            var reply=await caller.PostAsJsonAsync(root+"/messages",new{message="Hi"});Assert.Equal(HttpStatusCode.OK,reply.StatusCode);
            Assert.Equal("Hello there",(await reply.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("text").GetString());
            var stream=await caller.PostAsJsonAsync(root+"/stream",new{message="Continue"});var lines=await stream.Content.ReadAsStringAsync();Assert.Contains("\"type\":\"delta\"",lines);Assert.Contains("\"type\":\"done\"",lines);
            var history=await caller.GetFromJsonAsync<JsonElement>(root);Assert.Equal(4,history.GetProperty("messages").GetArrayLength());
            fake.Fail=true;
            var failed=await caller.PostAsJsonAsync(root+"/stream",new{message="Interrupted"});var partial=await failed.Content.ReadAsStringAsync();Assert.Contains("\"type\":\"error\"",partial);Assert.DoesNotContain("\"type\":\"done\"",partial);
            history=await caller.GetFromJsonAsync<JsonElement>(root);Assert.Equal(4,history.GetProperty("messages").GetArrayLength());
            Assert.Equal(HttpStatusCode.BadRequest,(await caller.PostAsJsonAsync(root+"/messages",new{message=new string('x',8001)})).StatusCode);
            foreach(var role in new[]{"User","Agent"})
            {
                var address=Guid.NewGuid()+"@example.test";var account=await caller.PostAsJsonAsync("/api/admin/users",new{name=role,email=address,password,role});Assert.True(account.IsSuccessStatusCode);
                using var other=host.CreateClient();await Login(other,address);
                Assert.Equal(role=="User"?HttpStatusCode.Forbidden:HttpStatusCode.NotFound,(await other.GetAsync(root)).StatusCode);
                if(role=="Agent"){var privatePage=await other.GetFromJsonAsync<JsonElement>("/api/ai/chat/conversations");Assert.Equal(0,privatePage.GetProperty("items").GetArrayLength());}
            }
        }
        finally{await database.DisposeAsync();Environment.SetEnvironmentVariable("JWT__SigningKey",previous);}
    }
    private sealed class FakeChat:IAIChatService
    {
        public bool Fail;
        public Task<string> SendAsync(IReadOnlyList<AIChatMessage> messages,string? model,CancellationToken ct)=>throw new NotSupportedException();
        public Task<AIChatHealth> HealthAsync(CancellationToken ct)=>Task.FromResult(new AIChatHealth(true,"fake"));
        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<AIChatMessage> messages,string? model,[EnumeratorCancellation]CancellationToken ct)
        {await Task.Yield();ct.ThrowIfCancellationRequested();yield return "Hello";if(Fail)throw new LLMServiceException(LLMFailureKind.Unavailable);yield return " there";}
    }
}
