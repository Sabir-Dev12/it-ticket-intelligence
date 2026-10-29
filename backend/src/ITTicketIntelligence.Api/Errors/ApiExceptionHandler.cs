using System.Diagnostics;
using ITTicketIntelligence.Application.Common;
using ITTicketIntelligence.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ITTicketIntelligence.Application.Knowledge.Retrieval;
using ITTicketIntelligence.Application.LanguageModels;
using ITTicketIntelligence.Application.Agents;
using ITTicketIntelligence.Application.Mcp.Contracts;

namespace ITTicketIntelligence.Api.Errors;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
            return true;
        }

        var status = exception switch
        {
            ITTicketIntelligence.Application.Assistant.AssistantAccessDeniedException => 403,
            RequestValidationException => 400,
            ResourceNotFoundException => 404,
            ConflictException or DomainException or DbUpdateConcurrencyException => 409,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation } } => 409,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation or PostgresErrorCodes.StringDataRightTruncation } } => 400,
            McpUnavailableException or AgentLimitException or LLMServiceException or RagUnavailableException or NpgsqlException or TimeoutException or OperationCanceledException => 503,
            _ => 500
        };
        var title = status switch { 400 => "Requête invalide", 403 => "Accès refusé", 404 => "Ressource introuvable", 409 => "Conflit", 503 => "Service indisponible", _ => "Erreur interne" };
        var detail = exception switch
        {
            RequestValidationException or ResourceNotFoundException or ConflictException or DomainException => exception.Message,
            DbUpdateConcurrencyException => "La ressource a changé. Actualisez avant de réessayer.",
            RagUnavailableException => "Le service de recherche documentaire est temporairement indisponible.",
            LLMServiceException => "Le service de modèle de langage est temporairement indisponible.",
            AgentLimitException => "L’agent a atteint sa limite d’exécution. Réessayez avec un contexte plus court.",
            _ => status switch { 400 => "Les données ne respectent pas les contraintes de validation.", 409 => "Une référence ou une donnée a changé pendant la sauvegarde.", 503 => "Une dépendance est temporairement indisponible.", _ => "Une erreur inattendue est survenue." }
        };
        ProblemDetails problem = exception is RequestValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(pair => pair.Key, pair => pair.Value))
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = title;
        problem.Detail = detail;
        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.StatusCode = status;
        if (status >= 500) logger.LogError(exception, "Request failed with status {StatusCode}", status);
        if (!await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem }))
        {
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        }
        return true;
    }
}
