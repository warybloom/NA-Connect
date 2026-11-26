using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NA_Connect.Services.Ldp
{
    /// <summary>
    /// Armazenamento TEMPORÁRIO de arquivos base (XLSX Espelho) do Novo Motor de LDP — decisão de
    /// infraestrutura DEFINITIVA (ACC / blob storage / storage do app) ainda PENDENTE, ver
    /// AUDITORIA_FORGESYNC_NA/17-*.md §9/§20. Caminho configurável via
    /// appsettings.json:LdpConfig:ArmazenamentoBasePath (relativo ao content root da aplicação) —
    /// nunca um caminho absoluto hardcoded, para não amarrar o sistema a uma pasta local fixa.
    /// </summary>
    public class LdpArquivoStorageService
    {
        private readonly string _basePath;

        public LdpArquivoStorageService(IConfiguration configuration, IHostEnvironment env)
        {
            var relativo = configuration["LdpConfig:ArmazenamentoBasePath"] ?? "App_Data/LdpConfigArquivos";
            _basePath = Path.Combine(env.ContentRootPath, relativo);
            Directory.CreateDirectory(_basePath);
        }

        /// <summary>Salva os bytes sob um nome de arquivo único e devolve o token (nome de arquivo) a persistir em Arquivo_Referencia.</summary>
        public string Salvar(byte[] conteudo, string nomeOriginal)
        {
            var extensao = Path.GetExtension(nomeOriginal);
            var token = $"{Guid.NewGuid():N}{extensao}";
            File.WriteAllBytes(Path.Combine(_basePath, token), conteudo);
            return token;
        }

        public byte[] Ler(string token)
        {
            var caminho = Path.Combine(_basePath, token);
            if (!File.Exists(caminho))
                throw new FileNotFoundException($"Arquivo de referência '{token}' não encontrado no armazenamento temporário do Novo Motor de LDP.");
            return File.ReadAllBytes(caminho);
        }

        public bool Existe(string token) => File.Exists(Path.Combine(_basePath, token));
    }
}
