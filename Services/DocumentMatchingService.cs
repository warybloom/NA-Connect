using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NA_Connect.Services
{
    public class DocumentMatchingService
    {
        // =====================================================================================
        // Correspondência por PREFIXO DE TOKENS (fronteira real, nunca Contains/substring/StartsWith
        // textual cru). Único ponto de verdade para essa lógica no projeto: LdpUpdateService.
        // NomeArquivoCorrespondeAoDocumento (usada pelo Workflow) delega para
        // NomeCorrespondePorPrefixoDeTokens abaixo — o comportamento do Workflow não muda, só passa a
        // reaproveitar esta implementação em vez de manter uma cópia própria.
        //
        // Regra: o valor é tokenizado (após remover SOMENTE uma extensão real conhecida — nunca
        // Path.GetFileNameWithoutExtension cru, que cortaria "P-0602.1-..." no ponto incidental de
        // "0602.1") e separado por espaço, ponto, hífen ou underline. Os tokens do código precisam ser
        // EXATAMENTE um prefixo dos tokens do arquivo, token a token — isso aceita qualquer sufixo final
        // ("-01", "-02", "-REV01", "-R01", "-A", "_01", " 01" ...) e rejeita tanto sufixo colado sem
        // fronteira ("...-00550") quanto prefixo colado ("X-...-01").
        // =====================================================================================

        private static readonly HashSet<string> ExtensoesDeArquivoConhecidas = new HashSet<string>(
            new[]
            {
                ".pdf", ".dwg", ".dxf", ".doc", ".docx", ".xls", ".xlsx", ".xlsm",
                ".rvt", ".rfa", ".nwd", ".nwc", ".ifc", ".zip", ".txt", ".csv", ".xml"
            },
            StringComparer.OrdinalIgnoreCase);

        private static string RemoverAcentos(string valor)
        {
            string normalizado = valor.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalizado)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Remove a extensão do final de <paramref name="valor"/> SOMENTE quando ela for uma extensão de
        /// arquivo real e conhecida (ver ExtensoesDeArquivoConhecidas) — nunca usa
        /// Path.GetFileNameWithoutExtension diretamente, porque esse método corta tudo depois do ÚLTIMO
        /// ponto do texto, mesmo quando esse ponto não é uma extensão (ex.: "P-0602.1-930-PX-DR-0003"
        /// viraria "P-0602" incorretamente).
        /// </summary>
        private static string RemoverSomenteExtensaoConhecida(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

            string texto = valor.Trim();
            string extensao = Path.GetExtension(texto);

            if (!string.IsNullOrWhiteSpace(extensao) && ExtensoesDeArquivoConhecidas.Contains(extensao))
            {
                return texto.Substring(0, texto.Length - extensao.Length);
            }

            return texto;
        }

        /// <summary>
        /// Divide um código de documento (ou nome de arquivo) em tokens, após remover SOMENTE uma
        /// extensão real conhecida, remover acentos e separar por espaço, ponto, hífen ou underline.
        /// Ex.: "P-0602.1-310-PX-DR-0055" vira ["P","0602","1","310","PX","DR","0055"];
        /// "P-0602.1-310-PX-DR-0055-01.dwg" vira ["P","0602","1","310","PX","DR","0055","01"].
        /// </summary>
        internal static string[] TokenizarCodigoDocumento(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor)) return Array.Empty<string>();

            string semExtensaoReal = RemoverSomenteExtensaoConhecida(valor);
            string normalizado = RemoverAcentos(semExtensaoReal.Trim());

            return Regex.Split(normalizado, @"[\s\.\-_]+")
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
        }

        /// <summary>
        /// Um nome de arquivo (ou nome-base sem extensão) "corresponde" a um código de documento quando
        /// os tokens do código são EXATAMENTE um prefixo dos tokens do arquivo, token a token — nunca por
        /// substring/Contains/StartsWith textual cru. Sufixo final do arquivo ("-01", "-REV02", "-A" etc.)
        /// fica indiferente automaticamente: "P-0602.1-310-PX-DR-0055" corresponde a
        /// "P-0602.1-310-PX-DR-0055-01" mas NÃO corresponde a "P-0602.1-310-PX-DR-00550" (o token "0055"
        /// do código nunca é igual ao token "00550" do arquivo) nem a "X-P-0602.1-310-PX-DR-0055-01" (o
        /// 1º token "X" não bate).
        /// </summary>
        public static bool NomeCorrespondePorPrefixoDeTokens(string nomeArquivoOuBase, string codigoDocumento)
        {
            string[] tokensArquivo = TokenizarCodigoDocumento(nomeArquivoOuBase);
            string[] tokensCodigo = TokenizarCodigoDocumento(codigoDocumento);

            if (tokensCodigo.Length == 0 || tokensArquivo.Length < tokensCodigo.Length)
            {
                return false;
            }

            for (int i = 0; i < tokensCodigo.Length; i++)
            {
                if (!string.Equals(tokensArquivo[i], tokensCodigo[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Usada EXCLUSIVAMENTE pela Etapa 7 (LdpUpdateService.SincronizarAtributosLdpPorPastaAsync).
        /// Devolve TODOS os nomes-base do ACC (chaves de indicePorNome, já sem extensão) que correspondem
        /// ao código por prefixo de tokens — nunca só o primeiro. Isso é necessário porque podem existir
        /// vários arquivos físicos com sufixos diferentes para o mesmo documento lógico da LDP (ex.:
        /// "...-0055-01.dwg" e "...-0055-02.dwg" na mesma pasta) e todos precisam receber os atributos,
        /// não apenas um escolhido arbitrariamente. Não decide prioridade Cliente/N&A nem fallback — isso
        /// é responsabilidade do chamador (Etapa 7 já resolve a prioridade do NamingStandard antes de
        /// chamar este método, uma vez por código).
        /// </summary>
        public IReadOnlyList<string> EncontrarDocumentosNoAccPorPrefixoDeTokens(string codigoDocumento, IEnumerable<string> nomesBaseNoAcc)
        {
            if (string.IsNullOrWhiteSpace(codigoDocumento) || nomesBaseNoAcc == null)
            {
                return Array.Empty<string>();
            }

            return nomesBaseNoAcc
                .Where(nomeBase => NomeCorrespondePorPrefixoDeTokens(nomeBase, codigoDocumento))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // =====================================================================================
        // Correspondência LEGADA por StartsWith — mantida EXATAMENTE como estava, sem nenhuma mudança de
        // comportamento. Usada por dois pontos não relacionados à Etapa 7: AutodeskApiService (relatório
        // de comparação LDP x ACC) e LdpUpdateService (relatório de "arquivos do ACC sem registro na
        // LDP"). Nenhum dos dois foi pedido para correção — mexer aqui alteraria resultado desses dois
        // fluxos sem necessidade.
        // =====================================================================================
        public string EncontrarDocumentoNoAcc(string numeroCliente, string numeroNA, IEnumerable<string> arquivosNoAcc)
        {
            if (arquivosNoAcc == null || !arquivosNoAcc.Any())
                return null;

            string documentoEncontrado = null;

            // 1. TENTA PELO NÚMERO DO CLIENTE (Prioridade Máxima)
            if (!string.IsNullOrWhiteSpace(numeroCliente))
            {
                // Busca o primeiro arquivo no ACC que COMECE com o número do cliente (ignorando maiúsculas/minúsculas)
                // Ex: "DOC-CLI-100" vai dar match em "DOC-CLI-100-R01.pdf"
                documentoEncontrado = arquivosNoAcc.FirstOrDefault(accFile =>
                    accFile.StartsWith(numeroCliente, StringComparison.OrdinalIgnoreCase));
            }

            // 2. SE ACHOU, RETORNA E PARA AQUI.
            if (!string.IsNullOrEmpty(documentoEncontrado))
            {
                return documentoEncontrado;
            }

            // 3. SE NÃO ACHOU (Ou se não tinha número de cliente), TENTA PELO NÚMERO DA N&A
            if (!string.IsNullOrWhiteSpace(numeroNA))
            {
                // Busca o primeiro arquivo no ACC que COMECE com o número N&A
                // Ex: "P-0602.1-000-PM-LI-0001" vai dar match em "P-0602.1-000-PM-LI-0001_Rev0A.xlsx"
                documentoEncontrado = arquivosNoAcc.FirstOrDefault(accFile =>
                    accFile.StartsWith(numeroNA, StringComparison.OrdinalIgnoreCase));
            }

            return documentoEncontrado; // Retorna o que achou (ou null se nenhum bater)
        }
    }
}
