using System;
using System.Collections.Generic;

namespace NA_Connect.Services.Ldp
{
    // POCOs do Novo Motor de LDP (fundação) — espelham 1:1 o schema ldp.* proposto em
    // Migrations/ConfiguracaoLdp/*.sql (não executado). Ver AUDITORIA_FORGESYNC_NA/17-*.md.

    public sealed class LdpTemplateDto
    {
        public int TemplateId { get; set; }
        public string NomeTemplate { get; set; }
        public string Descricao { get; set; }
        public string OrigemCriacao { get; set; } = "MANUAL";
        public bool Ativo { get; set; } = true;
        public List<LdpTemplateAbaDto> Abas { get; set; } = new();
    }

    public sealed class LdpTemplateAbaDto
    {
        public int TemplateAbaId { get; set; }
        public string NomeAba { get; set; }
        public int Ordem { get; set; }
        public bool Ativo { get; set; } = true;
        public List<LdpTemplateCampoDto> Campos { get; set; } = new();
    }

    public sealed class LdpTemplateCampoDto
    {
        public int TemplateCampoId { get; set; }
        public string NomeExibido { get; set; }
        public string ChaveCampo { get; set; }
        public string TipoDado { get; set; } = "TEXTO";
        public bool Obrigatorio { get; set; }
        public int Ordem { get; set; }
        public string Origem { get; set; } = "MANUAL";
        public bool Ativo { get; set; } = true;
    }

    public sealed class LdpCronogramaMapeamentoDto
    {
        public int CronogramaMapeamentoId { get; set; }
        public int TemplateId { get; set; }
        public string NomeMapeamento { get; set; }
        public bool Ativo { get; set; } = true;
        public List<LdpCronogramaMapeamentoCampoDto> Campos { get; set; } = new();
    }

    public sealed class LdpCronogramaMapeamentoCampoDto
    {
        public int CronogramaMapeamentoCampoId { get; set; }
        public int? TemplateCampoId { get; set; }
        public string ColunaCronogramaOrigem { get; set; }
        public int Ordem { get; set; }
    }

    public sealed class LdpExcelEspelhoDto
    {
        public int ExcelEspelhoId { get; set; }
        public int TemplateId { get; set; }
        public string NomeModelo { get; set; }
        public string ArquivoReferencia { get; set; }
        public bool Ativo { get; set; } = true;
        public List<LdpExcelEspelhoAbaDto> Abas { get; set; } = new();
    }

    public sealed class LdpExcelEspelhoAbaDto
    {
        public int ExcelEspelhoAbaId { get; set; }
        public int TemplateAbaId { get; set; }
        public string NomeAbaFisica { get; set; }
        public int LinhaCabecalho { get; set; } = 1;
        public int ColunaInicial { get; set; } = 1;
        public int Ordem { get; set; }
        public List<LdpExcelEspelhoCampoDto> Campos { get; set; } = new();
    }

    public sealed class LdpExcelEspelhoCampoDto
    {
        public int ExcelEspelhoCampoId { get; set; }
        public int TemplateCampoId { get; set; }
        public int Ordem { get; set; }
    }

    public sealed class LdpExcelControleDto
    {
        public int ExcelControleId { get; set; }
        public int TemplateId { get; set; }
        public string NomeConfiguracao { get; set; }
        public bool Ativo { get; set; } = true;
        public List<LdpExcelControleAbaDto> Abas { get; set; } = new();
    }

    public sealed class LdpExcelControleAbaDto
    {
        public int ExcelControleAbaId { get; set; }
        public int TemplateAbaId { get; set; }
        public string NomeAbaExibido { get; set; }
        public int Ordem { get; set; }
        public List<LdpExcelControleCampoDto> Campos { get; set; } = new();
    }

    public sealed class LdpExcelControleCampoDto
    {
        public int ExcelControleCampoId { get; set; }
        public int TemplateCampoId { get; set; }
        public string NomeExibidoOverride { get; set; }
        public int Ordem { get; set; }
    }

    // Estrutura do Projeto (Projeto Único x Projeto por Fase) — rodada 3. Isolado de dbo.Projetos e
    // de config.ProjetoConfiguracao (legado, schema config).
    public sealed class LdpProjetoConfiguracaoDto
    {
        public string ProjectIdAcc { get; set; }
        public string ModoEstrutura { get; set; } = "PROJETO_UNICO";
        public string ModoGeracaoFase { get; set; }
        public int? TemplateId { get; set; }
        public int? CronogramaMapeamentoId { get; set; }
        public int? ExcelEspelhoId { get; set; }
        public int? ExcelControleId { get; set; }
        public List<LdpProjetoFaseDto> Fases { get; set; } = new();
    }

    public sealed class LdpProjetoFaseDto
    {
        public int ProjetoFaseId { get; set; }
        public string Codigo { get; set; }
        public string Nome { get; set; }
        public int Ordem { get; set; }
        public bool Ativa { get; set; } = true;
        public string OperadorLogico { get; set; } = "ALL";
        public int? TemplateIdOverride { get; set; }
        public int? CronogramaMapeamentoIdOverride { get; set; }
        public int? ExcelEspelhoIdOverride { get; set; }
        public int? ExcelControleIdOverride { get; set; }
        public List<LdpProjetoFaseRegraDto> Regras { get; set; } = new();
    }

    public sealed class LdpProjetoFaseRegraDto
    {
        public int ProjetoFaseRegraId { get; set; }
        public string CampoOrigem { get; set; }
        public string Operador { get; set; }
        public string ValorComparacao { get; set; }
        public int Ordem { get; set; }
        public bool Ativa { get; set; } = true;
    }
}
