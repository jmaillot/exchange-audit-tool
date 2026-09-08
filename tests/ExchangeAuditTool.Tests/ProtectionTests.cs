using System.Collections.Generic;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class ProtectionTests
    {
        private static AuditSection Find(string id)
        {
            AuditRegistry.BuildAll();
            foreach (AuditSection s in AuditRegistry.Sections)
                if (s.Id == id) return s;
            return null;
        }

        [Fact]
        public void Protection_BuildsPerObjectQuery()
        {
            AuditSection section = Find("protection-policies");
            Assert.NotNull(section);
            Assert.Equal("Protection", section.Category);
            Assert.Equal(AuditScope.ExchangeOnline, section.Scope);
            var ctx = new ScriptContext("C:\\Exports\\ProtectionPolicies.csv");
            foreach (KeyValuePair<string, string> kv in new Dictionary<string, string>
            {
                { "antispamin", "Get-HostedContentFilterPolicy" },
                { "antispamout", "Get-HostedOutboundSpamFilterPolicy" },
                { "antimalware", "Get-MalwareFilterPolicy" },
                { "antiphish", "Get-AntiPhishPolicy" },
                { "safelinks", "Get-SafeLinksPolicy" },
                { "safeattachments", "Get-SafeAttachmentPolicy" },
                { "dlp", "Get-DlpPolicy" },
                { "ruleantispamin", "Get-HostedContentFilterRule" },
                { "ruleantispamout", "Get-HostedOutboundSpamFilterRule" },
                { "ruleantimalware", "Get-MalwareFilterRule" },
                { "ruleantiphish", "Get-AntiPhishRule" },
                { "rulesafelinks", "Get-SafeLinksRule" },
                { "rulesafeattachments", "Get-SafeAttachmentRule" }
            })
            {
                var sel = new AuditSelection();
                sel.Set("object", new List<string>(new string[] { kv.Key }));
                Assert.Contains(kv.Value, section.BuildScript(sel, ctx));
            }
        }

        [Fact]
        public void Protection_RuleLinksPolicyAutomatically()
        {
            AuditSection section = Find("protection-policies");
            var ctx = new ScriptContext("C:\\Exports\\ProtectionPolicies.csv");
            var sel = new AuditSelection();
            sel.Set("object", new List<string>(new string[] { "ruleantiphish" }));
            string script = section.BuildScript(sel, ctx);
            Assert.Contains("AntiPhishPolicy", script);

            var sel2 = new AuditSelection();
            sel2.Set("object", new List<string>(new string[] { "rulesafelinks" }));
            Assert.Contains("SafeLinksPolicy", section.BuildScript(sel2, ctx));
        }
    }
}
