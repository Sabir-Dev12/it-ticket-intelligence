using Microsoft.Extensions.Configuration;
using ITTicketIntelligence.Infrastructure.Knowledge.Retrieval;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using var service=new OllamaEmbeddingService(new HttpClient(),OllamaEmbeddingOptions.FromConfiguration(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddEnvironmentVariables().Build()),NullLogger<OllamaEmbeddingService>.Instance);
string[] documents=["SQL Server connection timeout: check database TCP port 1433 and SQL service availability.","VPN authentication fails: renew VPN credentials and reconnect the network tunnel.","Invoice PDF download failure: regenerate the billing invoice from the payment portal."];
string[] queries=["SQL Server connection timeout","VPN login authentication failure","Cannot download invoice PDF"];
var docs=await service.GenerateAsync(documents,EmbeddingPurpose.Document);var questions=await service.GenerateAsync(queries,EmbeddingPurpose.Query);
for(var q=0;q<queries.Length;q++){var scores=docs.Select(d=>d.Values.Zip(questions[q].Values,(a,b)=>(double)a*b).Sum()).ToArray();var best=Array.IndexOf(scores,scores.Max());if(best!=q)throw new Exception("Incorrect semantic ranking for query "+q);Console.WriteLine($"Query {q+1}: expected document ranked first; cosine {scores[best]:F4}; dimension {docs[best].Values.Count}");}

