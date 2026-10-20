using System.Collections.Generic;
using System.Linq;
using OfficeOpenXml;

namespace NA_Connect.Services.Ldp
{
    public sealed class LdpEspelhoAbaGeracao
    {
        public string NomeAbaFisica { get; set; }
        public int LinhaCabecalho { get; set; } = 1;
        public int ColunaInicial { get; set; } = 1;
        /// <summary>Nomes dos campos, na ordem em que devem ser escritos a partir de ColunaInicial.</summary>
        public List<string> CamposOrdem { get; set; } = new();
        /// <summary>Cada linha é um dicionário Campo -&gt; valor (mesmas chaves de CamposOrdem).</summary>
        public List<Dictionary<string, string>> LinhasDados { get; set; } = new();
    }

    /// <summary>
    /// Gera um XLSX de TESTE ("LDP Espelho") a partir de um XLSX base já enviado (preserva logo/
    /// carimbo/estilos — nunca recria o arquivo do zero) — mesma biblioteca (EPPlus) e mesmo padrão
    /// (abrir pacote, escrever em ws.Cells[linha,coluna], devolver bytes) já usados em
    /// GrdToLdpSyncService/LdpUpdateService/CronogramaTipo2LdpPlanilhaAtualizador. Nunca sobrescreve o
    /// arquivo base original: abre a partir dos bytes em memória e devolve um NOVO array de bytes.
    ///
    /// EPPlus.LicenseContext.NonCommercial é setado globalmente em Program.cs (mesma configuração usada
    /// por toda a base de código) — ver aviso de licenciamento na entrega desta tarefa.
    /// </summary>
    public class LdpEspelhoGeradorService
    {
        public byte[] Gerar(byte[] xlsxBase, List<LdpEspelhoAbaGeracao> abas)
        {
            using var package = new ExcelPackage(new System.IO.MemoryStream(xlsxBase));

            foreach (var aba in abas)
            {
                var ws = package.Workbook.Worksheets[aba.NomeAbaFisica];
                if (ws == null) continue; // aba física não existe mais no arquivo base — pula, não derruba a geração das outras

                int linhaEscrita = aba.LinhaCabecalho + 1;
                foreach (var linhaDados in aba.LinhasDados)
                {
                    int coluna = aba.ColunaInicial;
                    foreach (var campo in aba.CamposOrdem)
                    {
                        ws.Cells[linhaEscrita, coluna].Value = linhaDados.TryGetValue(campo, out var valor) ? valor : "";
                        coluna++;
                    }
                    linhaEscrita++;
                }
            }

            return package.GetAsByteArray();
        }
    }
}
