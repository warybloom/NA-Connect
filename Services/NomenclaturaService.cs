using Microsoft.Extensions.Configuration;

using System;

using System.Collections.Generic;
using System.Text.RegularExpressions; // ADICIONADO AQUI

namespace APS.WebApp.Services
{
    /// <summary>
    /// Modelo para carregar as seções de mapeamento do appsettings.json.
    /// </summary>
    public class NomenclaturaMappings
    {
        public Dictionary<string, string> Disciplinas { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> TiposDeDocumento { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Objeto que armazena as partes de um código de documento N&A após a decodificação.
    /// </summary>
    public class DecodedDocumentInfo
    {
        public string CR { get; set; } = string.Empty;
        public string AreaAplicacao { get; set; } = string.Empty;
        public string DisciplinaCodigo { get; set; } = string.Empty;
        public string TipoDocumentoCodigo { get; set; } = string.Empty;
        public string Sequencial { get; set; } = string.Empty;
        public string Revisao { get; set; } = string.Empty;
    }

    /// <summary>
    /// Serviço responsável por toda a lógica de negócio relacionada à nomenclatura de documentos.
    /// </summary>
    public class NomenclaturaService
    {
        private readonly NomenclaturaMappings _mappings;

        public NomenclaturaService(IConfiguration configuration)
        {
            // 1. Carrega os mapeamentos do appsettings.json normalmente.
            var loadedMappings = configuration.GetSection("NomenclaturaMappings").Get<NomenclaturaMappings>()
                                ?? new NomenclaturaMappings();

            // 2. Cria NOVOS dicionários, forçando a comparação a ser IGNORANDO MAIÚSCULAS/MINÚSCULAS.
            //    ESTA É A CORREÇÃO MÁGICA que resolve o problema de caracteres invisíveis.
            _mappings = new NomenclaturaMappings
            {
                Disciplinas = new Dictionary<string, string>(loadedMappings.Disciplinas, StringComparer.OrdinalIgnoreCase),
                TiposDeDocumento = new Dictionary<string, string>(loadedMappings.TiposDeDocumento, StringComparer.OrdinalIgnoreCase)
            };

            // O log de diagnóstico continua útil.
            Console.WriteLine("\n--- [LOG] SERVIÇO DE NOMENCLATURA INICIADO ---");
            Console.WriteLine($"[LOG] Mapeamentos de Disciplinas carregados: {_mappings.Disciplinas.Count}");
            Console.WriteLine($"[LOG] Mapeamentos de Tipos de Documento carregados: {_mappings.TiposDeDocumento.Count}");
            Console.WriteLine("--------------------------------------------------\n");
        }

        /// <summary>
        /// Decodifica um nome de documento completo no padrão N&A (ex: "BYR005-000-PC1-MD-001").
        /// Esta versão é robusta e limpa caracteres invisíveis ou inválidos.
        /// </summary>
        /// <param name="nomeDocumentoNA">A string completa do nome do documento.</param>
        /// <returns>Um objeto DecodedDocumentInfo com as partes, ou null se o formato for inválido.</returns>
        public DecodedDocumentInfo? DecodeNomeDocumento(string nomeDocumentoNA)
        {
            if (string.IsNullOrWhiteSpace(nomeDocumentoNA))
            {
                return null;
            }

            // --- ETAPA DE LIMPEZA (A SOLUÇÃO) ---
            // Remove qualquer caractere que NÃO seja letra, número ou hífen.
            var nomeLimpo = Regex.Replace(nomeDocumentoNA, @"[^\w-]", "");

            var parts = nomeLimpo.Split('-');
            if (parts.Length < 5)
            {
                Console.WriteLine($"[DEBUG] Falha na decodificação. Original: '{nomeDocumentoNA}', Após Limpeza: '{nomeLimpo}'");
                return null;
            }

            var disciplinaCodigo = parts[2].ToUpper();
            var tipoDocumentoCodigo = parts[3].ToUpper();

            return new DecodedDocumentInfo
            {
                CR = parts[0],
                AreaAplicacao = parts[1],
                DisciplinaCodigo = disciplinaCodigo,
                TipoDocumentoCodigo = tipoDocumentoCodigo,
                Sequencial = parts[4],
                Revisao = parts.Length > 5 ? parts[5] : "0"
            };
        }

        /// <summary>
        /// Procura o nome da pasta de destino no appsettings com base no código da disciplina.
        /// </summary>
        /// <param name="disciplinaCodigoCompleto">O código completo da disciplina (ex: "PC1").</param>
        /// <returns>O nome da pasta correspondente ou null se não for encontrado.</returns>
        public string? GetNomePastaDestino(string disciplinaCodigoCompleto)
        {
            if (_mappings.Disciplinas.TryGetValue(disciplinaCodigoCompleto, out var nomePasta))
            {
                return nomePasta;
            }
            return null;
        }

        /// <summary>
        /// Procura o nome do ficheiro de template no appsettings com base no código do tipo de documento.
        /// </summary>
        /// <param name="tipoDocumentoCodigo">O código do tipo de documento (ex: "MD").</param>
        /// <returns>O nome do ficheiro de template ou um fallback padrão se não for encontrado.</returns>
        public string GetTemplateFileName(string tipoDocumentoCodigo)
        {
            if (_mappings.TiposDeDocumento.TryGetValue(tipoDocumentoCodigo, out var templateName))
            {
                return templateName;
            }
            
            return "template-geral.docx";
        }
        
        /// <summary>
        /// RETORNA TODAS AS CHAVES DE DISCIPLINA CARREGADAS. (MÉTODO DE DIAGNÓSTICO)
        /// </summary>
        public ICollection<string> GetDisciplinaKeys()
        {
            return _mappings.Disciplinas.Keys;
        }
    }
}