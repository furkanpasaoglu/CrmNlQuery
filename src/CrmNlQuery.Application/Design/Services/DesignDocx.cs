using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using CrmNlQuery.Application.Design.Models;

namespace CrmNlQuery.Application.Design.Services;

/// <summary>
/// Builds a minimal valid Word document from a design with no dependencies.
/// </summary>
public static class DesignDocx
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Builds the .docx bytes with headings, paragraphs, bullets and tables from the design result.
    /// </summary>
    public static byte[] Build(DesignResult result)
    {
        var t = DesignRenderer.For(result.Language);
        var body = new List<XElement>();

        body.Add(Para("Title", t.Title));
        body.Add(Para("Normal", t.Proposal, italic: true));

        body.Add(Para("Heading1", "1. " + t.Summary));
        body.Add(Para("Normal", string.IsNullOrWhiteSpace(result.Document.Summary) ? t.None : result.Document.Summary));

        body.Add(Para("Heading1", "2. " + t.SchemaCheck));
        var ignored = new List<string>();
        if (result.Matches.Count == 0)
        {
            body.Add(Para("Normal", t.NoMatches));
        }
        foreach (var m in result.Matches)
        {
            var decision = result.Decisions.FirstOrDefault(d => string.Equals(d.Entity, m.Entity, StringComparison.OrdinalIgnoreCase));
            if (decision is not null && decision.Action == "ignore")
            {
                ignored.Add($"{m.Label} ({m.Entity})");
                continue;
            }
            body.Add(Para("Normal", $"{m.Label} ({m.Entity}, {(m.IsCustom ? t.Custom : t.Standard)}, {m.AttributeCount} {t.FieldCount})", bold: true));
            if (m.Fields.Count > 0)
            {
                body.Add(Bullet($"{t.Matched}: " + string.Join(", ", m.Fields.Select(f => $"{f.Label} ({f.Logical})"))));
            }
            if (decision is not null)
            {
                body.Add(Bullet($"{t.Decision}: {decision.Action}" + (string.IsNullOrWhiteSpace(decision.Reason) ? "" : " — " + decision.Reason)));
            }
            if (result.ClosedTables.Contains(m.Entity, StringComparer.OrdinalIgnoreCase))
            {
                body.Add(Bullet(t.Closed));
            }
        }
        if (ignored.Count > 0)
        {
            body.Add(Bullet($"{t.Unrelated}: {string.Join(", ", ignored)}"));
        }

        body.Add(Para("Heading1", "3. " + t.Tables));
        foreach (var table in result.Document.Tables)
        {
            var extends = !string.IsNullOrWhiteSpace(table.Extends);
            body.Add(Para("Heading2", $"{table.DisplayName} ({table.LogicalName}) — {(extends ? t.Extends : t.New)}"));
            if (!extends)
            {
                body.Add(Bullet($"{t.Ownership}: {table.Ownership}"));
            }
            if (!string.IsNullOrWhiteSpace(table.Description))
            {
                body.Add(Bullet($"{t.Description}: {table.Description}"));
            }
            var rows = new List<string[]> { new[] { t.Column, t.Display, t.Type, t.Required, t.Detail } };
            if (!extends && !string.IsNullOrWhiteSpace(table.PrimaryNameLogical))
            {
                rows.Add(new[] { table.PrimaryNameLogical, table.PrimaryNameDisplay, "string", t.Yes, "primary name" });
            }
            rows.AddRange(table.Fields.Select(f => new[] { f.LogicalName, f.DisplayName, f.Type, f.Required ? t.Yes : t.No, DesignRenderer.FieldDetail(f) }));
            body.Add(Table(rows));
            body.Add(Para("Normal", ""));
        }

        body.Add(Para("Heading1", "4. " + t.Relationships));
        if (result.Document.Relationships.Count == 0)
        {
            body.Add(Para("Normal", t.None));
        }
        else
        {
            var rows = new List<string[]> { new[] { t.Kind, t.From, t.To, t.Lookup, t.OnDelete, t.Description } };
            rows.AddRange(result.Document.Relationships.Select(r => new[] { r.Kind, r.From, r.To, r.LookupField, r.Kind == "many-to-many" ? "" : r.DeleteBehavior, r.Description }));
            body.Add(Table(rows));
        }

        body.Add(Para("Heading1", "5. " + t.Views));
        if (result.Document.Views.Count == 0)
        {
            body.Add(Para("Normal", t.None));
        }
        foreach (var v in result.Document.Views)
        {
            body.Add(Bullet($"{v.Name} ({v.Table}): {string.Join(", ", v.Columns)}"));
        }

        body.Add(Para("Heading1", "6. " + t.Security));
        var sensitive = DesignRenderer.SensitiveFields(result.Document);
        foreach (var note in result.Document.SecurityNotes)
        {
            body.Add(Bullet(note));
        }
        if (sensitive.Count > 0)
        {
            body.Add(Bullet($"{t.Sensitive}: {string.Join(", ", sensitive)}"));
        }
        if (result.Document.SecurityNotes.Count == 0 && sensitive.Count == 0)
        {
            body.Add(Para("Normal", t.None));
        }

        body.Add(Para("Heading1", "7. " + t.Open));
        if (result.Document.OpenQuestions.Count == 0)
        {
            body.Add(Para("Normal", t.None));
        }
        foreach (var q in result.Document.OpenQuestions)
        {
            body.Add(Bullet(q));
        }

        if (result.Problems.Count > 0)
        {
            body.Add(Para("Heading1", t.NeedsReview));
            foreach (var p in result.Problems)
            {
                body.Add(Bullet(p));
            }
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W),
                new XElement(W + "body", body,
                    new XElement(W + "sectPr",
                        new XElement(W + "pgSz", new XAttribute(W + "w", 11906), new XAttribute(W + "h", 16838)),
                        new XElement(W + "pgMar", new XAttribute(W + "top", 1134), new XAttribute(W + "right", 1134), new XAttribute(W + "bottom", 1134), new XAttribute(W + "left", 1134))))));

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", RootRels);
            Add(zip, "word/_rels/document.xml.rels", DocumentRels);
            Add(zip, "word/styles.xml", Styles);
            Add(zip, "word/document.xml", document.Declaration + Environment.NewLine + document.Root);
        }
        return stream.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Clean(string? text)
    {
        return new string((text ?? "").Where(ch => ch == '\t' || ch == '\n' || ch == '\r' || (ch >= ' ' && ch != '￾' && ch != '￿' && !char.IsSurrogate(ch))).ToArray());
    }

    private static XElement Run(string text, bool bold = false, bool italic = false)
    {
        var props = new XElement(W + "rPr");
        if (bold) { props.Add(new XElement(W + "b")); }
        if (italic) { props.Add(new XElement(W + "i")); }
        var run = new XElement(W + "r");
        if (props.HasElements) { run.Add(props); }
        run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(text)));
        return run;
    }

    private static XElement Para(string style, string text, bool bold = false, bool italic = false)
    {
        return new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", style))), Run(text, bold, italic));
    }

    private static XElement Bullet(string text)
    {
        return new XElement(W + "p",
            new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", "ListBullet"))),
            Run("• " + text));
    }

    private static XElement Table(List<string[]> rows)
    {
        var columns = rows[0].Length;
        var table = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblStyle", new XAttribute(W + "val", "TableGrid")),
                new XElement(W + "tblW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto"))));
        table.Add(new XElement(W + "tblGrid", Enumerable.Range(0, columns).Select(_ => new XElement(W + "gridCol", new XAttribute(W + "w", 9000 / columns)))));
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(W + "tr");
            foreach (var cell in rows[r])
            {
                var props = new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", 9000 / columns), new XAttribute(W + "type", "dxa")));
                if (r == 0)
                {
                    props.Add(new XElement(W + "shd", new XAttribute(W + "val", "clear"), new XAttribute(W + "color", "auto"), new XAttribute(W + "fill", "E7ECF2")));
                }
                row.Add(new XElement(W + "tc", props, new XElement(W + "p", Run(cell ?? "", bold: r == 0))));
            }
            table.Add(row);
        }
        return table;
    }

    private const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "</Types>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private const string DocumentRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/><w:sz w:val=\"21\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"100\" w:line=\"264\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"200\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"40\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"280\" w:after=\"120\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"1F3A5F\"/><w:sz w:val=\"30\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"200\" w:after=\"80\"/><w:outlineLvl w:val=\"1\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2F5597\"/><w:sz w:val=\"26\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"ListBullet\"><w:name w:val=\"List Bullet\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"60\"/><w:ind w:left=\"360\" w:hanging=\"240\"/></w:pPr></w:style>" +
        "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
        "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"TableNormal\"/><w:tblPr><w:tblBorders>" +
        "<w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/>" +
        "<w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/>" +
        "<w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/>" +
        "</w:tblBorders></w:tblPr></w:style>" +
        "</w:styles>";
}
