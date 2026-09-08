using System.Drawing;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class ThemePaletteTests
    {
        [Fact]
        public void SetLight_SwitchesContentTokens_KeepsChrome()
        {
            try
            {
                UiTheme.SetLight(false);
                Color darkWindow = UiTheme.Window;
                Color darkSurface = UiTheme.Surface;
                Color sidebar = UiTheme.Sidebar;
                Color navText = UiTheme.NavText;
                Color titleBar = UiTheme.TitleBar;
                Color blue = UiTheme.Blue;

                UiTheme.SetLight(true);
                Assert.True(UiTheme.IsLight);
                Assert.NotEqual(darkWindow, UiTheme.Window);
                Assert.NotEqual(darkSurface, UiTheme.Surface);
                Assert.Equal(sidebar, UiTheme.Sidebar);
                Assert.Equal(navText, UiTheme.NavText);
                Assert.Equal(titleBar, UiTheme.TitleBar);
                Assert.Equal(blue, UiTheme.Blue);
            }
            finally
            {
                UiTheme.SetLight(false);
            }
        }

        [Fact]
        public void ThemeMap_CoversChangedTokensOnly()
        {
            try
            {
                UiTheme.SetLight(false);
                var toLight = UiTheme.ThemeMap(true);
                Assert.True(toLight.Count > 0);
                Assert.True(toLight.ContainsKey(Color.FromArgb(7, 16, 28)));
                Assert.False(toLight.ContainsKey(Color.FromArgb(9, 20, 34)));
                Assert.False(toLight.ContainsKey(Color.FromArgb(47, 111, 235)));

                UiTheme.SetLight(true);
                var toDark = UiTheme.ThemeMap(false);
                Assert.True(toDark.ContainsKey(Color.FromArgb(241, 244, 249)));
            }
            finally
            {
                UiTheme.SetLight(false);
            }
        }
    }
}
