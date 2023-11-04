﻿using MediatR;
using ProjectTemplate.Application.Events.Logs;
using ProjectTemplate.Domain.Context;
using System.Text.Json;

namespace ProjectTemplate.Application.Pipelines;

public class LoggingBehaviour<TRequest, TResponse> :
    IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private readonly SessionContext _sessionContext;
    private readonly IPublisher _publisher;

    public LoggingBehaviour(
        SessionContext sessionContext,
        IPublisher publisher)
    {
        _sessionContext = sessionContext;
        _publisher = publisher;
    }

    public async Task<TResponse> Handle(TRequest request,
        CancellationToken cancellationToken,
        RequestHandlerDelegate<TResponse> next)
    {
        var log = new TransactionLogEvent
        {
            CorrelationId = _sessionContext.Correlationid,
            HandlerName = typeof(TRequest).Name,
            Request = JsonSerializer.Serialize(request),
            SessionContext = JsonSerializer.Serialize(_sessionContext)
        };
        var response = await next();
        log.Response = JsonSerializer.Serialize(response);
        await _publisher.Publish(log, cancellationToken);
        return response;
    }
}