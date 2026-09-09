using System.Collections.Generic;
using System.Text;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class SmartModeTests
    {
        private static AuditSection Find(string id)
        {
            AuditRegistry.BuildAll();
            foreach (AuditSection s in AuditRegistry.Sections)
                if (s.Id == id) return s;
            return null;
        }

        [Fact]
        public void EverySection_HasSmartModeGroup()
        {
            AuditRegistry.BuildAll();
            Assert.True(AuditRegistry.Sections.Count >= 27);
            foreach (AuditSection s in AuditRegistry.Sections)
            {
                bool hasAuto = false;
                foreach (AuditOptionGroup g in s.Groups)
                    if (g.Key == "auto") hasAuto = true;
                Assert.True(hasAuto, "No Smart mode group: " + s.Id);
            }
        }

        [Fact]
        public void SmartMode_Selected_EmitsFilter()
        {
            AuditSection section = Find("accepted-domains");
            var ctx = new ScriptContext("C:\\Exports\\AcceptedDomains.csv");
            var sel = new AuditSelection();
            sel.Set("auto", new List<string>(new string[] { "autodetect" }));
            string script = section.BuildScript(sel, ctx);
            Assert.Contains("function Remove-EmptyColumns", script);
            Assert.Contains("$rows = Remove-EmptyColumns $rows", script);
        }

        [Fact]
        public void SmartMode_NotSelected_NoFilter()
        {
            AuditSection section = Find("accepted-domains");
            var ctx = new ScriptContext("C:\\Exports\\AcceptedDomains.csv");
            string script = section.BuildScript(new AuditSelection(), ctx);
            Assert.DoesNotContain("Remove-EmptyColumns", script);
        }

        [Fact]
        public void SmartMode_ComplexSection_EmitsFilter()
        {
            AuditSection section = Find("mailbox-list");
            var ctx = new ScriptContext("C:\\Exports\\UserMailboxes.csv");
            var sel = new AuditSelection();
            sel.Set("auto", new List<string>(new string[] { "autodetect" }));
            string script = section.BuildScript(sel, ctx);
            Assert.Contains("function Remove-EmptyColumns", script);
            Assert.Contains("$rows = Remove-EmptyColumns $rows", script);
        }

        [Fact]
        public void EmitRemoveEmptyColumns_AlwaysKeepsIdentity()
        {
            var sb = new StringBuilder();
            PsScriptHelpers.EmitRemoveEmptyColumns(sb);
            string emitted = sb.ToString();
            Assert.Contains("function Remove-EmptyColumns", emitted);
            Assert.Contains("DisplayName", emitted);
            Assert.Contains("PrimarySmtpAddress", emitted);
        }
    }
}
