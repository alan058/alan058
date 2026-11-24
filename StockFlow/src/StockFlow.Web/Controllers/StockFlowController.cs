using Microsoft.AspNetCore.Mvc;
using StockFlow.Core.Exceptions;

namespace StockFlow.Web.Controllers;

/// <summary>Shared helpers for StockFlow controllers.</summary>
public abstract class StockFlowController : Controller
{
    public const string SuccessKey = "Success";
    public const string ErrorKey = "Error";

    /// <summary>The user recorded in audit columns. Windows/AD authentication would supply the real name.</summary>
    protected string CurrentUser =>
        User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(User.Identity.Name)
            ? User.Identity.Name
            : "web";

    protected void Success(string message) => TempData[SuccessKey] = message;

    protected void Error(string message) => TempData[ErrorKey] = message;

    /// <summary>Copies business-rule failures into ModelState so they show next to the right fields.</summary>
    protected void AddErrors(StockFlowException exception, string? prefix = null)
    {
        if (exception is BusinessValidationException validation)
        {
            foreach (var (field, messages) in validation.Errors)
            {
                var key = string.IsNullOrEmpty(field) || string.IsNullOrEmpty(prefix) ? field : $"{prefix}.{field}";
                foreach (var message in messages)
                {
                    ModelState.AddModelError(key, message);
                }
            }

            return;
        }

        ModelState.AddModelError(string.Empty, exception.Message);
    }
}
