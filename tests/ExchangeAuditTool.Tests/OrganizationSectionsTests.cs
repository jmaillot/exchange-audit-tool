using System.Collections.Generic;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class OrganizationSectionsTests
    {
        private static AuditSection Find(string id)
        {
            AuditRegistry.BuildAll();
            foreach (AuditSection s in AuditRegistry.Sections)
                if (s.Id == id) return s;
            return null;
        }

        [Fact]
        public void Registry_ContainsNewSections()
        {
            AuditRegistry.BuildAll();
            Assert.Equal(19, AuditRegistry.Sections.Count);
            var ids = new HashSet<string>();
            foreach (AuditSection s in AuditRegistry.Sections)
            {
                Assert.False(string.IsNullOrEmpty(s.Id));
                Assert.True(ids.Add(s.Id), "Duplicate section id: " + s.Id);
                Assert.NotNull(s.BuildScript);
                Assert.True(s.Groups.Count > 0, "No groups: " + s.Id);
            }
            Assert.NotNull(Find("org-sharing"));
            Assert.NotNull(Find("address-policies"));
        }

        [Fact]
        public void OrgSharing_BuildsPerObjectQuery()
        {
            AuditSection section = Find("org-sharing");
            Assert.Equal("Organization", section.Category);
            Assert.Equal(AuditScope.Both, section.Scope);
            var ctx = new ScriptContext("C:\\Exports\\OrgSharing.csv");
            foreach (KeyValuePair<string, string> kv in new Dictionary<string, string>
            {
                { "sharing", "Get-SharingPolicy" },
                { "relationships", "Get-OrganizationRelationship" },
                { "federation", "Get-FederationTrust" },
                { "fedorgid", "Get-FederatedOrganizationIdentifier" },
                { "orgconfig", "Get-OrganizationConfig" }
            })
            {
                var sel = new AuditSelection();
                sel.Set("object", new List<string>(new string[] { kv.Key }));
                string script = section.BuildScript(sel, ctx);
                Assert.Contains(kv.Value, script);
            }
        }

        [Fact]
        public void AddressPolicies_BuildsQuery()
        {
            AuditSection section = Find("address-policies");
            Assert.Equal("Domains / Routing", section.Category);
            Assert.Equal(AuditScope.Both, section.Scope);
            var ctx = new ScriptContext("C:\\Exports\\AddressPolicies.csv");
            string script = section.BuildScript(new AuditSelection(), ctx);
            Assert.Contains("Get-EmailAddressPolicy", script);
        }
    }
}
