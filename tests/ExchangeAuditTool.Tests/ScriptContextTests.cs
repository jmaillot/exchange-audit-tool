using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class ScriptContextTests
    {
        [Fact]
        public void PsLiteral_Null_ReturnsEmptyQuotes()
        {
            Assert.Equal("''", ScriptContext.PsLiteral(null));
        }

        [Fact]
        public void PsLiteral_EscapesSingleQuotes()
        {
            Assert.Equal("'O''Brien'", ScriptContext.PsLiteral("O'Brien"));
        }

        [Fact]
        public void PsLiteral_WrapsPlainValue()
        {
            Assert.Equal("'C:\\Exports\\a.csv'", ScriptContext.PsLiteral("C:\\Exports\\a.csv"));
        }

        [Fact]
        public void ExportCsv_UsesSemicolonDelimiterAndLiteralPath()
        {
            var ctx = new ScriptContext("C:\\Exports\\O'Brien.csv");
            string script = ctx.ExportCsv("$rows");

            Assert.Contains("Export-Csv", script);
            Assert.Contains("-Delimiter ';'", script);
            Assert.Contains("-LiteralPath 'C:\\Exports\\O''Brien.csv'", script);
            Assert.Contains("-NoTypeInformation", script);
        }
    }
}
