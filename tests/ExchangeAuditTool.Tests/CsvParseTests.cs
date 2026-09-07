using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class CsvParseTests
    {
        [Fact]
        public void ParseCsvLine_SimpleRow_SplitsOnSemicolon()
        {
            Assert.Equal(
                new[] { "a", "b", "c" },
                MainForm.ParseCsvLine("a;b;c"));
        }

        [Fact]
        public void ParseCsvLine_QuotedField_KeepsInnerSemicolon()
        {
            Assert.Equal(
                new[] { "a", "b;c", "d" },
                MainForm.ParseCsvLine("a;\"b;c\";d"));
        }

        [Fact]
        public void ParseCsvLine_EscapedQuotes_Unescaped()
        {
            Assert.Equal(
                new[] { "a\"b", "c" },
                MainForm.ParseCsvLine("\"a\"\"b\";c"));
        }
    }
}
