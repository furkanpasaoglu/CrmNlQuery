using System.Xml.Linq;
using CrmNlQuery.Application.CrmViews.Models;

namespace CrmNlQuery.Application.CrmViews.Services;

/// <summary>
/// Builds the column layout XML of a personal CRM view.
/// </summary>
public static class ViewLayout
{
    public const int DefaultCellWidth = 150;

    /// <summary>
    /// Builds the layout XML from the view's columns with cell names and widths.
    /// </summary>
    public static string ToLayoutXml(ViewDefinition view, int objectTypeCode)
    {
        var row = new XElement("row", new XAttribute("name", "result"), new XAttribute("id", view.PrimaryIdAttribute));
        foreach (var column in view.Columns)
        {
            row.Add(new XElement("cell", new XAttribute("name", column), new XAttribute("width", DefaultCellWidth)));
        }
        var grid = new XElement("grid",
            new XAttribute("name", "resultset"),
            new XAttribute("object", objectTypeCode),
            new XAttribute("jump", view.PrimaryNameAttribute),
            new XAttribute("select", "1"),
            new XAttribute("icon", "1"),
            new XAttribute("preview", "1"),
            row);
        return grid.ToString(SaveOptions.DisableFormatting);
    }
}
