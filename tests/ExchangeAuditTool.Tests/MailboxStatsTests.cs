using System.Collections.Generic;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class MailboxStatsTests
    {
        private static AuditSection Find(string id)
        {
            AuditRegistry.BuildAll();
            foreach (AuditSection s in AuditRegistry.Sections)
                if (s.Id == id) return s;
            return null;
        }

        private static AuditSelection With(params string[] pairs)
        {
            var sel = new AuditSelection();
            var grouped = new Dictionary<string, List<string>>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                List<string> list;
                if (!grouped.TryGetValue(pairs[i], out list))
                {
                    list = new List<string>();
                    grouped[pairs[i]] = list;
                }
                list.Add(pairs[i + 1]);
            }
            foreach (KeyValuePair<string, List<string>> kv in grouped)
                sel.Set(kv.Key, kv.Value);
            return sel;
        }

        [Fact]
        public void MobileDevices_BuildsPerMailboxQuery()
        {
            AuditSection section = Find("mobile-devices");
            Assert.NotNull(section);
            Assert.Equal("Mailboxes", section.Category);
            Assert.Equal(AuditScope.Both, section.Scope);
            var ctx = new ScriptContext("C:\\Exports\\MobileDevices.csv");
            string script = section.BuildScript(new AuditSelection(), ctx);
            Assert.Contains("Get-MobileDevice", script);
            Assert.Contains("MailboxDisplayName", script);
        }

        [Fact]
        public void MailboxCountsArchive_EmittedWhenSelected()
        {
            AuditSection section = Find("mailbox-list");
            var ctx = new ScriptContext("C:\\Exports\\UserMailboxes.csv");
            var sel = new AuditSelection();
            sel.Set("complementary", new List<string>(new string[] { "mailboxitemcount", "archivesize" }));
            string script = section.BuildScript(sel, ctx);
            Assert.Contains("Get-ItemCount", script);
            Assert.Contains("Get-ArchiveMB", script);
            Assert.Contains("MailboxItemCount", script);
            Assert.Contains("ArchiveSizeMB", script);
            Assert.DoesNotContain("Get-ArchiveCount", script);
            Assert.DoesNotContain("ArchiveItemCount", script);
        }

        [Fact]
        public void MailboxCountsArchive_SkippedWhenNotSelected()
        {
            AuditSection section = Find("mailbox-list");
            var ctx = new ScriptContext("C:\\Exports\\UserMailboxes.csv");
            string script = section.BuildScript(new AuditSelection(), ctx);
            Assert.DoesNotContain("Get-ItemCount", script);
            Assert.DoesNotContain("Get-ArchiveMB", script);
        }

        [Fact]
        public void SharedMailboxCountsArchive_EmittedWhenSelected()
        {
            AuditSection section = Find("shared-mailboxes");
            var ctx = new ScriptContext("C:\\Exports\\SharedMailboxes.csv");
            string script = section.BuildScript(With("complementary", "mailboxitemcount", "complementary", "archivesize"), ctx);
            Assert.Contains("Get-ItemCount", script);
            Assert.Contains("Get-ArchiveMB", script);
            Assert.DoesNotContain("Get-ArchiveCount", script);
        }

        [Fact]
        public void FolderPermissions_EmittedForUserAndShared()
        {
            var ctx = new ScriptContext("C:\\Exports\\x.csv");

            AuditSection users = Find("mailbox-list");
            var sel = new AuditSelection();
            sel.Set("folderperms", new List<string>(new string[] { "Inbox" }));
            string userScript = users.BuildScript(sel, ctx);
            Assert.Contains("Get-MailboxFolderPermission", userScript);
            Assert.Contains("FolderPerm_Inbox", userScript);

            AuditSection shared = Find("shared-mailboxes");
            bool hasSentItems = false;
            bool hasContacts = false;
            bool hasTasks = false;
            foreach (AuditOptionGroup g in shared.Groups)
            {
                if (g.Key != "folderperms") continue;
                foreach (AuditOption o in g.Options)
                {
                    if (o.Value == "SentItems") hasSentItems = true;
                    if (o.Value == "Contacts") hasContacts = true;
                    if (o.Value == "Tasks") hasTasks = true;
                }
            }
            Assert.True(hasSentItems && hasContacts && hasTasks);
            var sel2 = new AuditSelection();
            sel2.Set("folderperms", new List<string>(new string[] { "SentItems" }));
            Assert.Contains("FolderPerm_SentItems", shared.BuildScript(sel2, ctx));
        }

        [Fact]
        public void SendConnector_ContainsTlsCertificateName()
        {
            ConnectionMode saved = ConnectionSettings.Mode;
            try
            {
                ConnectionSettings.Mode = ConnectionMode.OnPremisesLocal;
                AuditSection section = Find("connectors");
                var ctx = new ScriptContext("C:\\Exports\\Connectors.csv");
                var sel = new AuditSelection();
                sel.Set("direction", new List<string>(new string[] { "outbound" }));
                sel.Set("opout", new List<string>(new string[] { "TlsCertificateName" }));
                string script = section.BuildScript(sel, ctx);
                Assert.Contains("Get-SendConnector", script);
                Assert.Contains("TlsCertificateName", script);
            }
            finally
            {
                ConnectionSettings.Mode = saved;
            }
        }
    }
}
