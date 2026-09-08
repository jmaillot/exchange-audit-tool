using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ExchangeAuditTool.Tests
{
    public sealed class ConnectionProfileTests
    {
        private static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(), "EAT-" + Guid.NewGuid().ToString("N") + ".json");
        }

        [Fact]
        public void SaveLoad_RoundTrip_PreservesAllFields()
        {
            string path = TempPath();
            try
            {
                var profiles = new List<ConnectionProfile>();
                profiles.Add(new ConnectionProfile
                {
                    Name = "Prod \"EXO\" \\ test",
                    Mode = ConnectionMode.ExchangeOnlineApp,
                    Upn = "admin@contoso.com",
                    DisableWam = false,
                    AppId = "11111111-2222-3333-4444-555555555555",
                    Organization = "contoso.onmicrosoft.com",
                    CertThumbprint = "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
                    RemoteServer = "exch01.contoso.com",
                    RemoteUser = "CONTOSO\\svc-exch",
                    RemoteAuth = RemoteAuthMode.Basic,
                    RemoteUseHttps = true
                });
                profiles.Add(new ConnectionProfile
                {
                    Name = "Café\nnewline",
                    Mode = ConnectionMode.OnPremisesRemote,
                    RemoteAuth = RemoteAuthMode.Kerberos
                });
                ConnectionProfileStore.SaveTo(path, profiles, "Prod \"EXO\" \\ test");

                List<ConnectionProfile> loaded;
                string last;
                ConnectionProfileStore.LoadFrom(path, out loaded, out last);

                Assert.Equal("Prod \"EXO\" \\ test", last);
                Assert.Equal(2, loaded.Count);
                ConnectionProfile a = loaded[0];
                Assert.Equal("Prod \"EXO\" \\ test", a.Name);
                Assert.Equal(ConnectionMode.ExchangeOnlineApp, a.Mode);
                Assert.Equal("admin@contoso.com", a.Upn);
                Assert.False(a.DisableWam);
                Assert.Equal("11111111-2222-3333-4444-555555555555", a.AppId);
                Assert.Equal("contoso.onmicrosoft.com", a.Organization);
                Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", a.CertThumbprint);
                Assert.Equal("exch01.contoso.com", a.RemoteServer);
                Assert.Equal("CONTOSO\\svc-exch", a.RemoteUser);
                Assert.Equal(RemoteAuthMode.Basic, a.RemoteAuth);
                Assert.True(a.RemoteUseHttps);
                Assert.Equal("Café\nnewline", loaded[1].Name);
                Assert.Equal(ConnectionMode.OnPremisesRemote, loaded[1].Mode);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void Load_MissingOrMalformedFile_ReturnsEmpty()
        {
            List<ConnectionProfile> profiles;
            string last;
            ConnectionProfileStore.LoadFrom(Path.Combine(Path.GetTempPath(), "EAT-" + Guid.NewGuid().ToString("N") + ".json"), out profiles, out last);
            Assert.Empty(profiles);
            Assert.Equal("", last);

            string bad = TempPath();
            try
            {
                File.WriteAllText(bad, "{not json");
                ConnectionProfileStore.LoadFrom(bad, out profiles, out last);
                Assert.Empty(profiles);
            }
            finally
            {
                try { File.Delete(bad); } catch { }
            }
        }

        [Fact]
        public void MiniJson_EscapesAndArrays()
        {
            var sb = new System.Text.StringBuilder();
            MiniJson.AppendString(sb, "a\"b\\c\ndéf");
            Dictionary<string, string> map = MiniJson.ParseObject("{\"k\":" + sb.ToString() + ",\"b\":true,\"arr\":[{\"x\":\"1\"},{\"x\":\"2}\"}]}");
            Assert.Equal("a\"b\\c\ndéf", map["k"]);
            Assert.Equal("true", map["b"]);
            List<string> items = MiniJson.SplitArray(map["arr"]);
            Assert.Equal(2, items.Count);
        }

        [Fact]
        public void MiniJson_Malformed_Throws()
        {
            Assert.Throws<FormatException>(delegate { MiniJson.ParseObject("[1]"); });
            Assert.Throws<FormatException>(delegate { MiniJson.ParseObject("{\"a\"}"); });
        }

        [Theory]
        [InlineData("Interactive", ConnectionMode.ExchangeOnlineInteractive)]
        [InlineData("AppOnly", ConnectionMode.ExchangeOnlineApp)]
        [InlineData("Local", ConnectionMode.OnPremisesLocal)]
        [InlineData("Remote", ConnectionMode.OnPremisesRemote)]
        [InlineData("Bogus", ConnectionMode.ExchangeOnlineInteractive)]
        public void ParseMode_Cases(string input, ConnectionMode expected)
        {
            Assert.Equal(expected, ConnectionProfileStore.ParseMode(input, ConnectionMode.ExchangeOnlineInteractive));
        }
    }
}
