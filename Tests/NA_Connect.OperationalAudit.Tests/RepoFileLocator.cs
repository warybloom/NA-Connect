using System;
using System.IO;
using System.Linq;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Localiza a raiz do repositório a partir do diretório de saída do teste (bin\Debug\net9.0\...),
    /// subindo diretórios até encontrar "AccIntegration.WebApp.csproj". Usado pelos testes de CONTRATO
    /// (AuditRepositorySqlContractTests, OperationalAuditSchemaContractTests) que leem o texto-fonte real
    /// (.cs/.sql) em vez de depender só de reflexão sobre tipos compilados.
    /// </summary>
    internal static class RepoFileLocator
    {
        public static string CaminhoAbsoluto(params string[] partesRelativasARaiz)
        {
            string raiz = LocalizarRaizDoRepositorio();
            return Path.Combine(new[] { raiz }.Concat(partesRelativasARaiz).ToArray());
        }

        public static string LerArquivoDaRaiz(params string[] partesRelativasARaiz) =>
            File.ReadAllText(CaminhoAbsoluto(partesRelativasARaiz));

        private static string LocalizarRaizDoRepositorio()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AccIntegration.WebApp.csproj")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new InvalidOperationException(
                $"Não foi possível localizar a raiz do repositório (AccIntegration.WebApp.csproj) a partir de '{AppContext.BaseDirectory}'.");
        }
    }
}
