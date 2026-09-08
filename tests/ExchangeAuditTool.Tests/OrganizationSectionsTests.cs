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
            Assert.Equal(26, AuditRegistry.Sections.Count);
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

        [Fact]
        public void OrgConfigBatch_BuildsQueries()
        {
            var ctx = new ScriptContext("C:\\Exports\\Org.csv");
            AuditSection journal = Find("journal-rules");
            Assert.Equal("Organization", journal.Category);
            Assert.Equal(AuditScope.Both, journal.Scope);
            Assert.Contains("Get-JournalRule", journal.BuildScript(new AuditSelection(), ctx));

            AuditSection certs = Find("certificates");
            Assert.Equal("Organization", certs.Category);
            Assert.Equal(AuditScope.OnPremises, certs.Scope);
            Assert.Contains("Get-ExchangeCertificate", certs.BuildScript(new AuditSelection(), ctx));

            AuditSection retention = Find("retention-policies");
            var sel = new AuditSelection();
            sel.Set("object", new List<string>(new string[] { "policies" }));
            Assert.Contains("Get-RetentionPolicy", retention.BuildScript(sel, ctx));
            sel.Set("object", new List<string>(new string[] { "tags" }));
            Assert.Contains("Get-RetentionPolicyTag", retention.BuildScript(sel, ctx));

            AuditSection books = Find("address-books");
            Assert.Equal(AuditScope.OnPremises, books.Scope);
            sel.Set("object", new List<string>(new string[] { "oab" }));
            Assert.Contains("Get-OfflineAddressBook", books.BuildScript(sel, ctx));
            sel.Set("object", new List<string>(new string[] { "lists" }));
            Assert.Contains("Get-AddressList", books.BuildScript(sel, ctx));

            AuditSection owa = Find("owa-policy");
            Assert.Contains("Get-OwaMailboxPolicy", owa.BuildScript(new AuditSelection(), ctx));

            AuditSection roles = Find("role-policies");
            sel.Set("object", new List<string>(new string[] { "policies" }));
            Assert.Contains("Get-RoleAssignmentPolicy", roles.BuildScript(sel, ctx));
            sel.Set("object", new List<string>(new string[] { "assignments" }));
            Assert.Contains("Get-ManagementRoleAssignment", roles.BuildScript(sel, ctx));
        }

        [Fact]
        public void MailContacts_RoutingGroup_ExportsExternalEmail()
        {
            AuditRegistry.BuildAll();
            AuditSection section = Find("mail-contacts");
            bool hasRouting = false;
            foreach (AuditOptionGroup g in section.Groups)
            {
                if (g.Key != "routing") continue;
                hasRouting = true;
                bool hasExternal = false;
                foreach (AuditOption o in g.Options)
                    if (o.Value == "ExternalEmailAddress") hasExternal = true;
                Assert.True(hasExternal);
            }
            Assert.True(hasRouting);
        }
    }
}
