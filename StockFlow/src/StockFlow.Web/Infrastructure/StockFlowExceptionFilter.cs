using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using StockFlow.Core.Exceptions;

namespace StockFlow.Web.Infrastructure;

/// <summary>
/// One structured place for turning exceptions into responses:
///   EntityNotFoundException        -> 404 page
///   DataAccessException (transient) -> 503 "try again" page
///   anything else                  -> left for the global error handler (500 + log)
/// Business rule failures on forms are handled in the controllers, where they
/// are added to ModelState so the user's input is kept.
/// </summary>
public sealed class StockFlowExceptionFilter : IExceptionFilter
{
    private readonly ILogger<StockFlowExceptionFilter> _logger;
    private readonly IModelMetadataProvider _metadata;

    public StockFlowExceptionFilter(ILogger<StockFlowExceptionFilter> logger, IModelMetadataProvider metadata)
    {
        _logger = logger;
        _metadata = metadata;
    }

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case EntityNotFoundException notFound:
                _logger.LogInformation("Not found: {Message}", notFound.Message);
                context.Result = View("NotFound", notFound.Message, StatusCodes.Status404NotFound);
                context.ExceptionHandled = true;
                break;

            case DataAccessException { IsTransient: true } transient:
                _logger.LogWarning(transient, "Transient database failure after retries");
                context.Result = View("Unavailable", "The database is busy or unreachable. Please try again in a moment.", StatusCodes.Status503ServiceUnavailable);
                context.ExceptionHandled = true;
                break;
        }
    }

    private ViewResult View(string viewName, string message, int statusCode) => new()
    {
        ViewName = viewName,
        StatusCode = statusCode,
        ViewData = new ViewDataDictionary(_metadata, new ModelStateDictionary()) { Model = message },
    };
}
