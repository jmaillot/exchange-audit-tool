using System;
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Xml;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class XlsxWriterTests
    {
        private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        [Fact]
        public void WriteFromCsv_SmallCsv_CreatesWorkbookWithRows()
        {
            string dir = Path.Combine(Path.GetTempPath(), "EAT-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string csv = Path.Combine(dir, "audit.csv");
                File.WriteAllText(csv,
                    "Name;Address;Note\r\n" +
                    "Café;\"a;b\";plain\r\n" +
                    "Bob;;\"quoted \"\"x\"\"\"\r\n" +
                    "Eve;eve@contoso.com;last\r\n",
                    new UTF8Encoding(false));

                string xlsx = Path.Combine(dir, "audit.xlsx");
                XlsxWriteResult r = XlsxWriter.WriteFromCsv(csv, xlsx, "User mailboxes");

                Assert.True(r.Ok, r.Error);
                Assert.Equal(3, r.DataRows);
                Assert.Equal(3, r.Columns);
                Assert.False(r.Truncated);
                Assert.True(new FileInfo(xlsx).Length > 0);

                using (Package package = Package.Open(xlsx, FileMode.Open, FileAccess.Read))
                {
                    Assert.True(package.PartExists(new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative)));
                    PackagePart sheet = package.GetPart(new Uri("/xl/worksheets/sheet1.xml", UriKind.Relative));
                    var doc = new XmlDocument();
                    using (Stream s = sheet.GetStream(FileMode.Open, FileAccess.Read))
                        doc.Load(s);
                    var mgr = new XmlNamespaceManager(doc.NameTable);
                    mgr.AddNamespace("m", MainNs);
                    Assert.Equal(4, doc.SelectNodes("//m:row", mgr).Count);
                    XmlNode headerCell = doc.SelectSingleNode("//m:row[@r='1']/m:c[@r='A1']", mgr);
                    Assert.NotNull(headerCell);
                    Assert.Equal("1", headerCell.Attributes["s"].Value);
                    Assert.Equal("Name", headerCell.SelectSingleNode("m:is/m:t", mgr).InnerText);
                    Assert.Equal("a;b", doc.SelectSingleNode("//m:row[@r='2']/m:c[@r='B2']/m:is/m:t", mgr).InnerText);
                    Assert.Equal("Café", doc.SelectSingleNode("//m:row[@r='2']/m:c[@r='A2']/m:is/m:t", mgr).InnerText);

                    PackagePart book = package.GetPart(new Uri("/xl/workbook.xml", UriKind.Relative));
                    string bookXml;
                    using (Stream s = book.GetStream(FileMode.Open, FileAccess.Read))
                    using (var reader = new StreamReader(s, Encoding.UTF8, true))
                        bookXml = reader.ReadToEnd();
                    Assert.Contains("User mailboxes", bookXml);
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void WriteFromCsv_MissingCsv_ReturnsNotOk()
        {
            string missing = Path.Combine(Path.GetTempPath(), "EAT-" + Guid.NewGuid().ToString("N") + ".csv");
            XlsxWriteResult r = XlsxWriter.WriteFromCsv(missing, missing + ".xlsx", "Audit");
            Assert.False(r.Ok);
        }

        [Theory]
        [InlineData("Plain", "Plain")]
        [InlineData("A/B:C*D?E[F]G", "A-B-C-D-E-F-G")]
        [InlineData("", "Audit")]
        public void SanitizeSheetName_Cases(string input, string expected)
        {
            Assert.Equal(expected, XlsxWriter.SanitizeSheetName(input));
        }

        [Theory]
        [InlineData(0, "A")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        [InlineData(27, "AB")]
        [InlineData(701, "ZZ")]
        [InlineData(702, "AAA")]
        [InlineData(16383, "XFD")]
        public void ColName_Cases(int index, string expected)
        {
            Assert.Equal(expected, XlsxWriter.ColName(index));
        }
    }
}
