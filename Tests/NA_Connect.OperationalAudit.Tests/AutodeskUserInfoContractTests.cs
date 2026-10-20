using System.IO;
using System.Linq;
using System.Text.Json;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Identidade do usuário: o endpoint legado /userprofile/v1/users/@me foi removido pela Autodesk (410 Gone) e não
    /// pode voltar ao código; o login 3-legged pede user-profile:read (UserInfo) sem alterar o scope do token 2-legged.
    /// </summary>
    public class AutodeskUserInfoContractTests
    {
        [Fact]
        public void NenhumFonteUsaOEndpointLegadoUserProfileMe()
        {
            var raiz = Path.GetDirectoryName(RepoFileLocator.CaminhoAbsoluto("Program.cs"))!;
            var fontes = new[] { "Services", "Controllers", "Areas" }
                .Where(d => Directory.Exists(Path.Combine(raiz, d)))
                .SelectMany(d => Directory.EnumerateFiles(Path.Combine(raiz, d), "*.cs", SearchOption.AllDirectories))
                .Where(f => File.ReadAllText(f).Contains("userprofile/v1/users/@me"))
                .Select(f => Path.GetRelativePath(raiz, f))
                .ToList();
            Assert.True(fontes.Count == 0, "Endpoint legado (410) ainda usado em: " + string.Join(", ", fontes));
        }

        [Fact]
        public void LoginPedeUserProfileRead_ETokenDoisPernasNaoMuda()
        {
            var cfg = JsonDocument.Parse(RepoFileLocator.LerArquivoDaRaiz("appsettings.json")).RootElement.GetProperty("APS");
            Assert.Equal("user-profile:read", cfg.GetProperty("UserScope").GetString());
            Assert.DoesNotContain("user-profile:read", cfg.GetProperty("Scope").GetString());

            var oauth = RepoFileLocator.LerArquivoDaRaiz("Areas", "Authentication", "Controllers", "OAuthController.cs");
            Assert.Contains("_config[\"APS:UserScope\"]", oauth);
            var tokenManager = RepoFileLocator.LerArquivoDaRaiz("Services", "TokenManager.cs");
            Assert.DoesNotContain("UserScope", tokenManager);
        }
    }
}
