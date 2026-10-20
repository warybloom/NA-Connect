using System;

namespace NA_Connect.OperationalAudit.Tests
{
    /// <summary>
    /// Correção 16/09/2026 (Etapa 07 — recursão para 06 ENGINEERING/08 FEDERATED MODEL/09 ENGINEERING
    /// DOCUMENTS/12 PRODUCT): a arquitetura anterior de ClassificarViaPastaPrincipalAlternativaAsync fazia
    /// DOIS passes — 1) listagem direta (não recursiva) de cada disciplina configurada; 2) uma varredura
    /// recursiva "aditiva" que EXCLUÍA qualquer pasta já reconhecida como disciplina (HashSet
    /// pastasDisciplinaJaCobertas). Resultado: um arquivo profundo dentro de uma disciplina reconhecida
    /// (ex.: "09 - ENGINEERING DOCUMENTS/09.06 - PIPING/Related Files/Subpasta/arquivo.pdf") NUNCA era
    /// visitado. A correção substituiu os dois passes por UMA única varredura recursiva completa a partir
    /// da pasta principal. Como AutodeskApiService é uma classe concreta sem métodos virtuais (não há
    /// costura de teste para simular chamadas HTTP reais sem introduzir uma abstração nova, fora do escopo
    /// desta correção), este arquivo prova as garantias estruturais contra o TEXTO-FONTE REAL de
    /// Services\LdpUpdateService.cs (mesmo padrão de Tipo2RoutingContractTests) — a prova comportamental
    /// completa (arquivo profundo realmente encontrado; nenhum FolderId percorrido duas vezes contra a API
    /// real do ACC) só é alcançável na execução real (validação CHS001).
    /// </summary>
    public class Etapa07RecursaoUnicaContractTests
    {
        private static string LerLdpUpdateService() =>
            RepoFileLocator.LerArquivoDaRaiz("Services", "LdpUpdateService.cs");

        private static string ExtrairCorpoDoMetodo(string codigoFonte, string assinaturaMetodo)
        {
            int inicio = codigoFonte.IndexOf(assinaturaMetodo, StringComparison.Ordinal);
            Assert.True(inicio >= 0, $"Assinatura '{assinaturaMetodo}' não encontrada — o método pode ter sido renomeado ou movido.");

            int chaveAbre = codigoFonte.IndexOf('{', inicio);
            Assert.True(chaveAbre >= 0);

            int profundidade = 0;
            int i = chaveAbre;
            for (; i < codigoFonte.Length; i++)
            {
                if (codigoFonte[i] == '{') profundidade++;
                else if (codigoFonte[i] == '}')
                {
                    profundidade--;
                    if (profundidade == 0) break;
                }
            }
            Assert.True(profundidade == 0, "Chave de fechamento do método não encontrada (contagem desbalanceada).");
            return codigoFonte.Substring(chaveAbre, i - chaveAbre + 1);
        }

        [Fact]
        public void NuncaMaisExisteExclusaoDeDisciplinaJaCoberta()
        {
            // pastasDisciplinaJaCobertas era o HashSet que causava o bug relatado — sua simples reintrodução
            // em qualquer forma (mesmo renomeada, se o padrão de exclusão voltar) já seria capturada pelas
            // outras asserções deste arquivo; esta prova o nome literal do bug original não retornou.
            Assert.DoesNotContain("pastasDisciplinaJaCobertas", LerLdpUpdateService(), StringComparison.Ordinal);
        }

        [Fact]
        public void ClassificarViaPastaPrincipalAlternativa_ChamaAColetaRecursivaExatamenteUmaVezComAPastaPrincipal()
        {
            string corpo = ExtrairCorpoDoMetodo(
                LerLdpUpdateService(),
                "private async Task ClassificarViaPastaPrincipalAlternativaAsync(");

            int ocorrencias = 0;
            int pos = 0;
            while ((pos = corpo.IndexOf("ColetarArquivosDaPastaRecursivoAsync(", pos, StringComparison.Ordinal)) >= 0)
            {
                ocorrencias++;
                pos += 1;
            }
            Assert.True(ocorrencias == 1,
                $"Esperado exatamente 1 chamada a ColetarArquivosDaPastaRecursivoAsync dentro de " +
                $"ClassificarViaPastaPrincipalAlternativaAsync (uma única varredura completa) — encontradas {ocorrencias}. " +
                "Mais de uma chamada reintroduziria o risco de reescanear a mesma árvore (fluxo duplicado).");

            int chamada = corpo.IndexOf("ColetarArquivosDaPastaRecursivoAsync(", StringComparison.Ordinal);
            int fimDaChamada = corpo.IndexOf(';', chamada);
            string argumentos = corpo.Substring(chamada, fimDaChamada - chamada);
            Assert.Contains("pastaPrincipal.Id", argumentos, StringComparison.Ordinal);

            // Nenhum foreach por disciplina/pasta-restante deve envolver essa chamada — provaria que a
            // arquitetura de dois passes (direto + aditivo com exclusão) voltou.
            Assert.DoesNotContain("pastaRestante", corpo, StringComparison.Ordinal);
            Assert.DoesNotContain("ResolverPastaDisciplina(", corpo, StringComparison.Ordinal);
        }

        [Fact]
        public void ColetaRecursiva_SoIgnoraPastaPorPalavraInvalida_NuncaPorJaTerSidoVisitada()
        {
            string corpo = ExtrairCorpoDoMetodo(
                LerLdpUpdateService(),
                "private async Task ColetarArquivosDaPastaRecursivoAsync(");

            // A única guarda que pode impedir a descida numa subpasta é o filtro de palavra inválida
            // (OLD/BACKUP/ARCHIVE/...) — nunca uma checagem de "já visitada/já coberta".
            Assert.Contains("ContemPalavraInvalida(NormalizarNomePasta(item.Name))", corpo, StringComparison.Ordinal);
            Assert.DoesNotContain("jaCobertas", corpo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("jaVisitada", corpo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("jaProcessada", corpo, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ColetaRecursiva_ChamaRecursaoComOIdDoItemAtual_NuncaComOIdDaPastaRaizOuPrincipal()
        {
            string corpo = ExtrairCorpoDoMetodo(
                LerLdpUpdateService(),
                "private async Task ColetarArquivosDaPastaRecursivoAsync(");

            // Bug suspeitado pelo usuário: recursão chamada com o Id da pasta-pai/raiz em vez do Id do item
            // atual do foreach, o que reescanearia a árvore inteira uma vez por subpasta remanescente.
            Assert.Contains(
                "ColetarArquivosDaPastaRecursivoAsync(projectId, item.Id, chaveConfiguracao, grupo,",
                corpo, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ColetarArquivosDaPastaRecursivoAsync(projectId, folderId,",
                corpo, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "ColetarArquivosDaPastaRecursivoAsync(projectId, pastaPrincipal.Id,",
                corpo, StringComparison.Ordinal);
        }
    }
}
