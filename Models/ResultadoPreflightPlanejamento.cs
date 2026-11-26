using System.Collections.Generic;
using System.Data;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado estruturado do preflight de uma etapa de Planning Control (seção 20 do pedido). O Worker
    /// nunca deve registrar CONCLUIDO só porque o método retornou uma string — deve olhar
    /// <see cref="Sucesso"/>. <see cref="Tabela"/> só vem preenchida quando Sucesso é true; nenhuma conexão
    /// SQL é aberta antes disso (preflight antes de apagar — seção 19).
    /// </summary>
    public sealed class ResultadoPreflightPlanejamento
    {
        // Metadados de domínio: não mapear ao SQL dos CAP/HOLD/SCOPE sem suporte de schema.
        public string? ProjectIdAcc { get; set; }
        public string TipoRegistro { get; set; } = "DADO";
        public string StatusImportacao { get; set; } = "OK";
        public string? DiagnosticoImportacao { get; set; }
        public static ResultadoPreflightPlanejamento SemExcel(string pasta) => new()
        {
            TipoRegistro = "SEM_EXCEL", StatusImportacao = "SEM_EXCEL", CodigoResultado = "SEM_EXCEL",
            PastaEncontrada = pasta, DiagnosticoImportacao = "NÃO EXISTE EXCEL PARA O PROJETO NA PASTA OFICIAL.",
            Mensagem = "SEM_EXCEL: diagnóstico preparado no domínio; persistência depende das seis colunas técnicas no SQL."
        };
        public bool Sucesso { get; set; }
        public string CodigoResultado { get; set; }
        public string Mensagem { get; set; }

        public string PastaEncontrada { get; set; }
        public string ArquivoSelecionado { get; set; }
        public string AbaSelecionada { get; set; }

        public int HeaderRow { get; set; }
        public List<string> ColunasEncontradas { get; set; } = new List<string>();
        public List<string> ColunasAusentes { get; set; } = new List<string>();

        public int LinhasFisicas { get; set; }
        public int LinhasAnalisadas { get; set; }
        public Dictionary<int, List<string>> DiagnosticosLinhas { get; set; } = new();

        public int RowsWithErrors { get; set; }
        public int LinhasValidas { get; set; }
        public List<string> LinhasIgnoradas { get; set; } = new List<string>();

        public DataTable Tabela { get; set; }

        public override string ToString() =>
            $"Sucesso={Sucesso} | CodigoResultado={CodigoResultado} | Pasta={PastaEncontrada} | Arquivo={ArquivoSelecionado} | " +
            $"Aba={AbaSelecionada} | HeaderRow={HeaderRow} | ColunasEncontradas=[{string.Join(", ", ColunasEncontradas)}] | " +
            $"ColunasAusentes=[{string.Join(", ", ColunasAusentes)}] | LinhasFisicas={LinhasFisicas} | LinhasAnalisadas={LinhasAnalisadas} | " +
            $"LinhasValidas={LinhasValidas} | LinhasIgnoradas={LinhasIgnoradas.Count} | Mensagem={Mensagem}";
    }
}
