using System;
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Xml;

namespace ExchangeAuditTool
{
    internal sealed class XlsxWriteResult
    {
        public bool Ok;
        public string Error = "";
        public int DataRows;
        public int Columns;
        public bool Truncated;
        public long Bytes;
    }

    // Minimal dependency-free .xlsx writer: converts the ';'-delimited audit
    // CSV into a single-worksheet workbook (bold header, frozen top row,
    // autofilter, sized columns) using only the in-box WindowsBase packaging
    // API - no Excel install, no NuGet packages.
    internal static class XlsxWriter
    {
        internal const int MaxDataRows = 1048575; // Excel row limit incl. the header row
        internal const int MaxColumns = 16384;    // Excel column limit (XFD)
        private const int MinColWidth = 12;
        private const int MaxColWidth = 60;

        private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public static XlsxWriteResult WriteFromCsv(string csvPath, string xlsxPath, string sheetName)
        {
            var result = new XlsxWriteResult();
            try
            {
                if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
                {
                    result.Error = "CSV file not found: " + csvPath;
                    return result;
                }
                sheetName = SanitizeSheetName(sheetName);

                string[] headers;
                int cols;
                int[] widths;
                using (var headerReader = new StreamReader(csvPath, Encoding.UTF8, true))
                {
                    string headerLine = headerReader.ReadLine();
                    if (headerLine == null)
                    {
                        result.Error = "CSV file is empty.";
                        return result;
                    }
                    headers = MainForm.ParseCsvLine(headerLine);
                    cols = Math.Min(headers.Length, MaxColumns);
                    if (cols == 0)
                    {
                        result.Error = "CSV header has no columns.";
                        return result;
                    }
                    result.Columns = cols;
                    widths = new int[cols];
                    for (int c = 0; c < cols; c++) widths[c] = TextWidth(headers[c]);
                }

                using (Package package = Package.Open(xlsxPath, FileMode.Create))
                {
                    WriteContentTypes(package);
                    WriteRootRels(package);
                    WriteDocProps(package);
                    WriteWorkbook(package, sheetName);
                    WriteStyles(package);
                    WriteSheet(package, csvPath, headers, cols, widths, result);
                }

                result.Ok = true;
                try { result.Bytes = new FileInfo(xlsxPath).Length; }
                catch { result.Bytes = 0; }
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex.Message;
            }
            return result;
        }

        internal static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Audit";
            char[] bad = new char[] { '\\', '/', '*', '?', ':', '[', ']' };
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool isBad = false;
                foreach (char b in bad) if (c == b) { isBad = true; break; }
                sb.Append(isBad ? '-' : c);
            }
            string clean = sb.ToString().Trim();
            if (clean.Length == 0) clean = "Audit";
            if (clean.Length > 31) clean = clean.Substring(0, 31);
            return clean;
        }

        private static int TextWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return s.Length;
        }

        private static void WriteSheet(Package package, string csvPath, string[] headers, int cols, int[] widths, XlsxWriteResult result)
        {
            // Pass 1: row count + column widths (streaming, no row buffering).
            int totalRows = 0;
            using (var counter = new StreamReader(csvPath, Encoding.UTF8, true))
            {
                counter.ReadLine(); // header
                string line;
                while ((line = counter.ReadLine()) != null)
                {
                    if (totalRows >= MaxDataRows)
                    {
                        result.Truncated = true;
                        break;
                    }
                    totalRows++;
                    string[] cells = MainForm.ParseCsvLine(line);
                    for (int c = 0; c < cols && c < cells.Length; c++)
                    {
                        string v = cells[c];
                        if (v != null && v.Length > widths[c]) widths[c] = v.Length;
                    }
                }
            }
            result.DataRows = totalRows;

            string lastCol = ColName(cols - 1);
            string lastCell = lastCol + (totalRows + 1).ToString();
            Uri uri = new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative);
            PackagePart part = package.CreatePart(uri, "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", CompressionOption.Normal);
            using (Stream stream = part.GetStream(FileMode.Create, FileAccess.Write))
            {
                var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false };
                using (XmlWriter w = XmlWriter.Create(stream, settings))
                {
                    w.WriteStartDocument();
                    w.WriteStartElement("worksheet", MainNs);
                    w.WriteStartElement("dimension");
                    w.WriteAttributeString("ref", "A1:" + lastCell);
                    w.WriteEndElement();
                    w.WriteStartElement("sheetViews");
                    w.WriteStartElement("sheetView");
                    w.WriteAttributeString("workbookViewId", "0");
                    w.WriteStartElement("pane");
                    w.WriteAttributeString("ySplit", "1");
                    w.WriteAttributeString("topLeftCell", "A2");
                    w.WriteAttributeString("activePane", "bottomLeft");
                    w.WriteAttributeString("state", "frozen");
                    w.WriteEndElement();
                    w.WriteStartElement("selection");
                    w.WriteAttributeString("pane", "bottomLeft");
                    w.WriteAttributeString("activeCell", "A2");
                    w.WriteAttributeString("sqref", "A2");
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteEndElement();
                    w.WriteStartElement("sheetFormat");
                    w.WriteAttributeString("defaultRowHeight", "15");
                    w.WriteEndElement();

                    w.WriteStartElement("sheetData");
                    // Header row (bold).
                    w.WriteStartElement("row");
                    w.WriteAttributeString("r", "1");
                    for (int c = 0; c < cols; c++) WriteCell(w, c, 1, c < headers.Length ? headers[c] : "", true);
                    w.WriteEndElement();

                    // Pass 2: rows.
                    using (var reader = new StreamReader(csvPath, Encoding.UTF8, true))
                    {
                        reader.ReadLine(); // header
                        int row = 1;
                        string line;
                        while (row <= totalRows && (line = reader.ReadLine()) != null)
                        {
                            row++;
                            string[] cells = MainForm.ParseCsvLine(line);
                            w.WriteStartElement("row");
                            w.WriteAttributeString("r", row.ToString());
                            for (int c = 0; c < cols; c++)
                                WriteCell(w, c, row, c < cells.Length ? cells[c] : "", false);
                            w.WriteEndElement();
                        }
                    }
                    w.WriteEndElement(); // sheetData

                    w.WriteStartElement("cols");
                    for (int c = 0; c < cols; c++)
                    {
                        int width = widths[c] + 2;
                        if (width < MinColWidth) width = MinColWidth;
                        if (width > MaxColWidth) width = MaxColWidth;
                        w.WriteStartElement("col");
                        w.WriteAttributeString("min", (c + 1).ToString());
                        w.WriteAttributeString("max", (c + 1).ToString());
                        w.WriteAttributeString("width", width.ToString());
                        w.WriteAttributeString("customWidth", "1");
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();

                    w.WriteStartElement("autoFilter");
                    w.WriteAttributeString("ref", "A1:" + lastCol + "1");
                    w.WriteEndElement();
                    w.WriteEndElement(); // worksheet
                    w.WriteEndDocument();
                }
            }
        }

        private static void WriteCell(XmlWriter w, int col, int row, string value, bool bold)
        {
            w.WriteStartElement("c");
            w.WriteAttributeString("r", ColName(col) + row.ToString());
            if (value == null) value = "";
            if (value.Length == 0)
            {
                w.WriteEndElement();
                return;
            }
            w.WriteAttributeString("t", "inlineStr");
            if (bold) w.WriteAttributeString("s", "1");
            w.WriteStartElement("is");
            w.WriteStartElement("t");
            w.WriteString(value);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        internal static string ColName(int index)
        {
            string s = "";
            int i = index + 1;
            while (i > 0)
            {
                int m = (i - 1) % 26;
                s = (char)('A' + m) + s;
                i = (i - 1) / 26;
            }
            return s;
        }

        private static void WritePart(Package package, string path, string contentType, Action<XmlWriter> body)
        {
            PackagePart part = package.CreatePart(new Uri(path, UriKind.Relative), contentType, CompressionOption.Normal);
            using (Stream stream = part.GetStream(FileMode.Create, FileAccess.Write))
            {
                var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false };
                using (XmlWriter w = XmlWriter.Create(stream, settings))
                {
                    w.WriteStartDocument();
                    body(w);
                    w.WriteEndDocument();
                }
            }
        }

        private static void WriteContentTypes(Package package)
        {
            WritePart(package, "/[Content_Types].xml", "application/vnd.openxmlformats-package.content-types+xml", delegate (XmlWriter w)
            {
                w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                w.WriteStartElement("Default");
                w.WriteAttributeString("Extension", "rels");
                w.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
                w.WriteEndElement();
                w.WriteStartElement("Default");
                w.WriteAttributeString("Extension", "xml");
                w.WriteAttributeString("ContentType", "application/xml");
                w.WriteEndElement();
                Override(w, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
                Override(w, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml");
                Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Override(w, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                w.WriteEndElement();
            });
        }

        private static void Override(XmlWriter w, string partName, string contentType)
        {
            w.WriteStartElement("Override");
            w.WriteAttributeString("PartName", partName);
            w.WriteAttributeString("ContentType", contentType);
            w.WriteEndElement();
        }

        private static void WriteRootRels(Package package)
        {
            WritePart(package, "/_rels/.rels", "application/vnd.openxmlformats-package.relationships+xml", delegate (XmlWriter w)
            {
                w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                Rel(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
                Rel(w, "rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml");
                Rel(w, "rId3", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/extended-properties", "docProps/app.xml");
                w.WriteEndElement();
            });
        }

        private static void Rel(XmlWriter w, string id, string type, string target)
        {
            w.WriteStartElement("Relationship");
            w.WriteAttributeString("Id", id);
            w.WriteAttributeString("Type", type);
            w.WriteAttributeString("Target", target);
            w.WriteEndElement();
        }

        private static void WriteDocProps(Package package)
        {
            const string dc = "http://purl.org/dc/elements/1.1/";
            const string dcterms = "http://purl.org/dc/terms/";
            WritePart(package, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", delegate (XmlWriter w)
            {
                w.WriteStartElement("coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
                w.WriteStartElement("title", dc);
                w.WriteString("Exchange Audit Export");
                w.WriteEndElement();
                w.WriteStartElement("creator", dc);
                w.WriteString("Exchange Audit Tool");
                w.WriteEndElement();
                w.WriteStartElement("created", dcterms);
                w.WriteString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                w.WriteEndElement();
                w.WriteEndElement();
            });
            WritePart(package, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", delegate (XmlWriter w)
            {
                w.WriteStartElement("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
                w.WriteElementString("Application", "Exchange Audit Tool");
                w.WriteEndElement();
            });
        }

        private static void WriteWorkbook(Package package, string sheetName)
        {
            WritePart(package, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", delegate (XmlWriter w)
            {
                w.WriteStartElement("workbook", MainNs);
                w.WriteAttributeString("xmlns", "r", null, RelNs);
                w.WriteStartElement("sheets");
                w.WriteStartElement("sheet");
                w.WriteAttributeString("name", sheetName);
                w.WriteAttributeString("sheetId", "1");
                w.WriteAttributeString("r", "id", RelNs, "rId1");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
            });
            WritePart(package, "/xl/_rels/workbook.xml.rels", "application/vnd.openxmlformats-package.relationships+xml", delegate (XmlWriter w)
            {
                w.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                Rel(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml");
                Rel(w, "rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
                w.WriteEndElement();
            });
        }

        private static void WriteStyles(Package package)
        {
            WritePart(package, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", delegate (XmlWriter w)
            {
                w.WriteStartElement("styleSheet", MainNs);
                w.WriteStartElement("fonts");
                w.WriteAttributeString("count", "2");
                w.WriteStartElement("font");
                w.WriteStartElement("sz");
                w.WriteAttributeString("val", "11");
                w.WriteEndElement();
                w.WriteStartElement("name");
                w.WriteAttributeString("val", "Calibri");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteStartElement("font");
                w.WriteStartElement("b");
                w.WriteEndElement();
                w.WriteStartElement("sz");
                w.WriteAttributeString("val", "11");
                w.WriteEndElement();
                w.WriteStartElement("name");
                w.WriteAttributeString("val", "Calibri");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteStartElement("fills");
                w.WriteAttributeString("count", "2");
                w.WriteStartElement("fill");
                w.WriteStartElement("patternFill");
                w.WriteAttributeString("patternType", "none");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteStartElement("fill");
                w.WriteStartElement("patternFill");
                w.WriteAttributeString("patternType", "gray125");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteStartElement("borders");
                w.WriteAttributeString("count", "1");
                w.WriteStartElement("border");
                w.WriteStartElement("left");
                w.WriteEndElement();
                w.WriteStartElement("right");
                w.WriteEndElement();
                w.WriteStartElement("top");
                w.WriteEndElement();
                w.WriteStartElement("bottom");
                w.WriteEndElement();
                w.WriteStartElement("diagonal");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteStartElement("cellStyleXfs");
                w.WriteAttributeString("count", "1");
                w.WriteStartElement("xf");
                w.WriteAttributeString("numFmtId", "0");
                w.WriteAttributeString("fontId", "0");
                w.WriteAttributeString("fillId", "0");
                w.WriteAttributeString("borderId", "0");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteStartElement("cellXfs");
                w.WriteAttributeString("count", "2");
                w.WriteStartElement("xf");
                w.WriteAttributeString("numFmtId", "0");
                w.WriteAttributeString("fontId", "0");
                w.WriteAttributeString("fillId", "0");
                w.WriteAttributeString("borderId", "0");
                w.WriteAttributeString("xfId", "0");
                w.WriteEndElement();
                w.WriteStartElement("xf");
                w.WriteAttributeString("numFmtId", "0");
                w.WriteAttributeString("fontId", "1");
                w.WriteAttributeString("fillId", "0");
                w.WriteAttributeString("borderId", "0");
                w.WriteAttributeString("xfId", "0");
                w.WriteAttributeString("applyFont", "1");
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
            });
        }
    }
}
