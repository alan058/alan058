using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using StockFlow.Core.Models;
using StockFlow.Core.Services;

namespace StockFlow.Web.Infrastructure;

public static class HtmlExtensions
{
    public static IHtmlContent StatusBadge(this IHtmlHelper html, OrderStatus status)
    {
        var css = status switch
        {
            OrderStatus.Open => "text-bg-primary",
            OrderStatus.Allocated => "text-bg-warning",
            OrderStatus.Dispatched => "text-bg-success",
            OrderStatus.Cancelled => "text-bg-secondary",
            _ => "text-bg-light",
        };

        var tag = new TagBuilder("span");
        tag.AddCssClass("badge");
        tag.AddCssClass(css);
        tag.InnerHtml.Append(OrderStatusRules.Describe(status));
        return tag;
    }

    public static string Money(decimal value) => value.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
}
