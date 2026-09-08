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
