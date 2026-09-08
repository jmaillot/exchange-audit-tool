using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class ValidationTests
    {
        [Theory]
        [InlineData("user@contoso.com", true)]
        [InlineData("user@contoso.onmicrosoft.com", true)]
        [InlineData("", false)]
        [InlineData("plainaddress", false)]
        [InlineData("user@contoso", false)]
        [InlineData("user@@contoso.com", false)]
        [InlineData("user @contoso.com", false)]
        public void IsValidUpn_Cases(string upn, bool expected)
        {
            Assert.Equal(expected, MainForm.IsValidUpn(upn));
        }

        [Theory]
        [InlineData("0123456789ABCDEF0123456789ABCDEF01234567", true)]
        [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
        [InlineData("", false)]
        [InlineData("ABCDEF", false)]
        [InlineData("0123456789ABCDEF0123456789ABCDEF0123456G", false)]
        [InlineData("0123456789ABCDEF0123456789ABCDEF012345678", false)]
        public void IsValidThumbprint_Cases(string thumb, bool expected)
        {
            Assert.Equal(expected, MainForm.IsValidThumbprint(thumb));
        }

        [Theory]
        [InlineData("exch01.contoso.com", true)]
        [InlineData("EXCH01.Contoso.COM", true)]
        [InlineData("", false)]
        [InlineData("ab", false)]
        [InlineData("localhost", false)]
        [InlineData("exch 01.contoso.com", false)]
        [InlineData("exch01.contoso.com/PowerShell", false)]
        public void IsValidHostname_Cases(string host, bool expected)
        {
            Assert.Equal(expected, MainForm.IsValidHostname(host));
        }

        [Theory]
        [InlineData("C:\\Exports\\audit.csv", true)]
        [InlineData("audit.CSV", true)]
        [InlineData("", false)]
        [InlineData("C:\\Exports\\audit.txt", false)]
        [InlineData("C:\\Exports\\", false)]
        public void IsValidCsvPath_Cases(string csv, bool expected)
        {
            Assert.Equal(expected, MainForm.IsValidCsvPath(csv));
        }
    }
}
