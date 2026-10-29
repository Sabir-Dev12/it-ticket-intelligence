using System.Security.Cryptography;
using System.Text;
using ITTicketIntelligence.Application;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Domain.Knowledge;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Infrastructure;
using ITTicketIntelligence.Infrastructure.Persistence;
using ITTicketIntelligence.IntegrationTests.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace ITTicketIntelligence.IntegrationTests.Knowledge;
[Collection("Postgres")]
public sealed class PgVectorIndexingTests(PostgresFixture fixture)
{
    private sealed class Access:IKnowledgeAccessContext {public bool CanSearch{get;set;}=true;public bool IsAdmin{get;set;}=true;public Guid? UserId=>null;}
    private sealed class Embeddings:IEmbeddingService
    {
        public string Version="test-v1";public int Calls;public EmbeddingProfile Profile=>new(Version,3);
        public Task<IReadOnlyList<TextEmbedding>> GenerateAsync(IReadOnlyList<string> texts,EmbeddingPurpose purpose,CancellationToken cancellationToken=default)
        {Calls++;return Task.FromResult<IReadOnlyList<TextEmbedding>>(texts.Select(t=>new TextEmbedding(Version,t.Contains("SQL",StringComparison.OrdinalIgnoreCase)?new float[]{1,0,0}:new float[]{0,1,0})).ToArray());}
    }
    [Fact]public async Task DurableJobsPgVectorRankingPermissionsAndProfileSwitchWorkTogether()
    {
        var access=new Access();var embeddings=new Embeddings();var services=new ServiceCollection();services.AddApplication();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Postgres"]=fixture.ConnectionString}).Build());
        services.AddSingleton<IEmbeddingService>(embeddings);services.AddSingleton<IKnowledgeAccessContext>(access);
        await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        async Task<Guid> Document(string content,bool restricted)
        {
            var now=DateTimeOffset.UtcNow;var id=Guid.NewGuid();var document=new KnowledgeDocument(id,content,content,"pgvector/"+id,TicketCategory.Database,now);
            var chunk=new KnowledgeChunk(Guid.NewGuid(),id,1,0,content,0,content.Length,Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))),now);
            document.ApplyPreparation(content,"test-v1",[chunk],now);document.SetRestricted(restricted,now);db.KnowledgeDocuments.Add(document);await db.SaveChangesAsync();return id;
        }
        var sql=await Document("SQL Server connection timeout",true);var vpn=await Document("VPN network troubleshooting",false);
        var jobs=scope.ServiceProvider.GetRequiredService<IKnowledgeIndexQueue>();
        var first=await jobs.EnqueueAsync(sql,default);Assert.Equal(0,embeddings.Calls);Assert.Equal("Queued",(await jobs.GetAsync(first.Id,default))!.State);
        Assert.True(await jobs.ProcessNextAsync(default));Assert.Equal("Succeeded",(await jobs.GetAsync(first.Id,default))!.State);
        await jobs.EnqueueAsync(vpn,default);await jobs.ProcessNextAsync(default);
        var search=scope.ServiceProvider.GetRequiredService<SemanticSearchService>();
        var result=await search.SearchAsync(new("SQL timeout",1,-.1));Assert.Equal(sql,Assert.Single(result.Results).DocumentId);Assert.True(result.Results[0].RelevanceScore>.99);
        access.IsAdmin=false;result=await search.SearchAsync(new("SQL timeout",1,-.1));Assert.Equal(vpn,Assert.Single(result.Results).DocumentId);
        access.CanSearch=false;Assert.Empty((await search.SearchAsync(new("SQL timeout"))).Results);
        access.CanSearch=true;access.IsAdmin=true;
        var stale=await jobs.EnqueueAsync(sql,default);embeddings.Version="test-v2";await jobs.ProcessNextAsync(default);Assert.Equal("RequiresReindex",(await jobs.GetAsync(stale.Id,default))!.State);
        Assert.Empty((await search.SearchAsync(new("SQL timeout"))).Results);
        await jobs.EnqueueAsync(sql,default);await jobs.ProcessNextAsync(default);
        Assert.Equal(sql,Assert.Single((await search.SearchAsync(new("SQL timeout"))).Results).DocumentId);
    }
}
