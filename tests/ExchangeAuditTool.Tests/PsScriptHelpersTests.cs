using System.Collections.Generic;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class PsScriptHelpersTests
    {
        [Fact]
        public void Collect_DedupesAcrossGroups()
        {
            var sel = new AuditSelection();
            sel.Set("g1", new List<string> { "a", "b" });
            sel.Set("g2", new List<string> { "b", "c" });

            List<string> chosen = PsScriptHelpers.Collect(sel, "g1", "g2");

            Assert.Equal(new List<string> { "a", "b", "c" }, chosen);
        }

        [Fact]
        public void Collect_MissingGroup_ReturnsEmpty()
        {
            var sel = new AuditSelection();

            Assert.Empty(PsScriptHelpers.Collect(sel, "nope"));
        }

        [Fact]
        public void AddSizeGroup_HasThreeOptionsWithUnlimitedDefault()
        {
            var section = new AuditSection("t", "n", "t", "s", "mailbox", AuditScope.Both);

            PsScriptHelpers.AddSizeGroup(section);

            Assert.Equal(1, section.Groups.Count);
            AuditOptionGroup size = section.Groups[0];
            Assert.Equal("size", size.Key);
            Assert.Equal(3, size.Options.Count);
            Assert.Equal("Unlimited", size.Options[0].Value);
            Assert.True(size.Options[0].DefaultChecked);
        }
    }
}
