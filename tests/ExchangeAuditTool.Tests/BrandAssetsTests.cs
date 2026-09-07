using System.Drawing;
using System.IO;
using System.Reflection;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class BrandAssetsTests
    {
        [Fact]
        public void AppIcon_EmbeddedResource_Exists()
        {
            Assembly asm = typeof(ScriptContext).Assembly;
            using (Stream s = asm.GetManifestResourceStream("ExchangeAuditTool.app.ico"))
            {
                Assert.NotNull(s);
            }
        }

        [Fact]
        public void AppIcon_ReturnsUsableIcon()
        {
            Icon icon = BrandAssets.AppIcon(32);

            Assert.NotNull(icon);
            Assert.True(icon.Width > 0 && icon.Height > 0);
        }
    }
}
