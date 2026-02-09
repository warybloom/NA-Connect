using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AccIntegration.WebApp.Data;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext()
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<TbComercial> TbComercials { get; set; }

    public virtual DbSet<TbContasAnalitica> TbContasAnaliticas { get; set; }

    public virtual DbSet<TbContasApagar> TbContasApagars { get; set; }

    public virtual DbSet<TbContasAreceber> TbContasArecebers { get; set; }

    public virtual DbSet<TbControleContrato> TbControleContratos { get; set; }

    public virtual DbSet<TbControleCusto> TbControleCustos { get; set; }

    public virtual DbSet<TbFluxoCaixaReal> TbFluxoCaixaReals { get; set; }

    public virtual DbSet<TbHorasPlanejadasDocumentoT> TbHorasPlanejadasDocumentoTs { get; set; }

    public virtual DbSet<TbHorasPlanejadasT> TbHorasPlanejadasTs { get; set; }

    public virtual DbSet<TbInfo> TbInfos { get; set; }

    public virtual DbSet<TbParcelasAfaturar> TbParcelasAfaturars { get; set; }

    public virtual DbSet<TbPatrimonio> TbPatrimonios { get; set; }

    public virtual DbSet<TbTimeSheet> TbTimeSheets { get; set; }

    public virtual DbSet<TbTimeSheetRealizadoDocumento> TbTimeSheetRealizadoDocumentos { get; set; }

    public virtual DbSet<TbgestaoNac007> TbgestaoNac007s { get; set; }

    public virtual DbSet<TbmovEquipamento> TbmovEquipamentos { get; set; }

    /*protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=sql.naconsult.com.br,5040;Database=db_na_bi;User Id=bi_na;Password=biNA@1202@m;TrustServerCertificate=True");*/

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TbComercial>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("TbComercial");

            entity.Property(e => e.Cliente)
                .HasMaxLength(256)
                .IsUnicode(false);
            entity.Property(e => e.Empreendimento)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Empresa)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Escopo)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Estado)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Gestor)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Inicio).HasColumnType("datetime");
            entity.Property(e => e.Investimento)
                .HasColumnType("numeric(18, 2)")
                .HasColumnName("investimento");
            entity.Property(e => e.Pais)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.SegmentoMercado)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.SegmentoNegocio)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Tipo)
                .HasMaxLength(24)
                .IsUnicode(false);
        });

        modelBuilder.Entity<TbContasAnalitica>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbContasAnaliticas");

            entity.Property(e => e.ClassificacaoImposto)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.Competencia).HasColumnType("datetime");
            entity.Property(e => e.ContratoId).HasColumnName("Contrato.ID");
            entity.Property(e => e.DataRealizacao).HasColumnType("datetime");
            entity.Property(e => e.DescricaoManual)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.EapCentroResultadoCodigo)
                .HasMaxLength(8)
                .IsUnicode(false)
                .HasColumnName("EAP.CentroResultado.Codigo");
            entity.Property(e => e.EapCentroResultadoDescricao)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("EAP.CentroResultado.Descricao");
            entity.Property(e => e.EapCentroResultadoNatureza).HasColumnName("EAP.CentroResultado.Natureza");
            entity.Property(e => e.EapitemPlanoContaCodigo)
                .HasMaxLength(32)
                .IsUnicode(false)
                .HasColumnName("EAPItem.PlanoConta.Codigo");
            entity.Property(e => e.EapitemPlanoContaDescricao)
                .HasMaxLength(512)
                .IsUnicode(false)
                .HasColumnName("EAPItem.PlanoConta.Descricao");
            entity.Property(e => e.EapitemPlanoContaNatureza).HasColumnName("EAPItem.PlanoConta.Natureza");
            entity.Property(e => e.EmpresaId).HasColumnName("Empresa.ID");
            entity.Property(e => e.EntidadeNome)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Entidade.Nome");
            entity.Property(e => e.Observacao)
                .HasMaxLength(1024)
                .IsUnicode(false);
            entity.Property(e => e.SegmentoNegocioDescricao)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("SegmentoNegocio.Descricao");
            entity.Property(e => e.SegmentoNegocioId).HasColumnName("SegmentoNegocio.ID");
            entity.Property(e => e.ValorRealizado).HasColumnType("numeric(18, 2)");
        });

        modelBuilder.Entity<TbContasApagar>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbContasAPagar");

            entity.Property(e => e.Agencia)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.Banco)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.CentroResultado)
                .HasMaxLength(8)
                .IsUnicode(false);
            entity.Property(e => e.CentroResultadoDescricao)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.Competencia).HasColumnType("datetime");
            entity.Property(e => e.ContaCorrete)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(8)
                .IsUnicode(false);
            entity.Property(e => e.EapId).HasColumnName("EAP.Id");
            entity.Property(e => e.EntidadeNome)
                .HasMaxLength(128)
                .IsUnicode(false)
                .HasColumnName("Entidade.Nome");
            entity.Property(e => e.FormaPagamento)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.GestorNome)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Identificador)
                .HasMaxLength(32)
                .IsUnicode(false);
            entity.Property(e => e.Observacao)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.Parcela)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Previsao).HasColumnType("datetime");
            entity.Property(e => e.ResgatarPrevisao)
                .HasMaxLength(8)
                .IsUnicode(false);
            entity.Property(e => e.Valor).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorLiquido).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorTitulo).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.Vencimento).HasColumnType("datetime");
        });

        modelBuilder.Entity<TbContasAreceber>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbContasAReceber");

            entity.Property(e => e.Competencia).HasColumnType("datetime");
            entity.Property(e => e.ContratoId).HasColumnName("Contrato.ID");
            entity.Property(e => e.DataEmissao).HasColumnType("datetime");
            entity.Property(e => e.EapCentroResultadoCodigo)
                .HasMaxLength(8)
                .IsUnicode(false)
                .HasColumnName("EAP.CentroResultado.Codigo");
            entity.Property(e => e.EapCentroResultadoDescricao)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("EAP.CentroResultado.Descricao");
            entity.Property(e => e.EapId).HasColumnName("EAP.ID");
            entity.Property(e => e.Emissao).HasColumnType("datetime");
            entity.Property(e => e.EmpresaId).HasColumnName("Empresa.ID");
            entity.Property(e => e.EmpresaNome)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("Empresa.Nome");
            entity.Property(e => e.EntidadeCodigo).HasColumnName("Entidade.Codigo");
            entity.Property(e => e.EntidadeId).HasColumnName("Entidade.ID");
            entity.Property(e => e.EntidadeNome)
                .HasMaxLength(128)
                .IsUnicode(false)
                .HasColumnName("Entidade.Nome");
            entity.Property(e => e.GroupBy)
                .HasMaxLength(1)
                .IsUnicode(false);
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.MoedaSigla)
                .HasMaxLength(6)
                .IsUnicode(false)
                .HasColumnName("Moeda.Sigla");
            entity.Property(e => e.NotaFiscal)
                .HasMaxLength(32)
                .IsUnicode(false);
            entity.Property(e => e.Observacao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.ObservacaoParcela)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.Parcela)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.PedidoId).HasColumnName("Pedido.ID");
            entity.Property(e => e.Previsao).HasColumnType("datetime");
            entity.Property(e => e.ResgatarPrevisao)
                .HasMaxLength(1)
                .IsUnicode(false)
                .IsFixedLength();
            entity.Property(e => e.TipoDocumentoCodigo)
                .HasMaxLength(8)
                .IsUnicode(false)
                .HasColumnName("TipoDocumento.Codigo");
            entity.Property(e => e.TipoDocumentoDescricao)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("TipoDocumento.Descricao");
            entity.Property(e => e.TotalBrutoEmpresa).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.TotalLiquidoEmpresa).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.Valor).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorLiquido).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.Vencimento).HasColumnType("datetime");
        });

        modelBuilder.Entity<TbControleContrato>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbControleContrato");

            entity.Property(e => e.CadastroData)
                .HasColumnType("datetime")
                .HasColumnName("Cadastro.Data");
            entity.Property(e => e.EapCentroResultadoCodigo)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("EAP.CentroResultado.Codigo");
            entity.Property(e => e.EapCentroResultadoDescricao)
                .HasMaxLength(128)
                .IsUnicode(false)
                .HasColumnName("EAP.CentroResultado.Descricao");
            entity.Property(e => e.EapIp).HasColumnName("EAP.IP");
            entity.Property(e => e.EapitemPlanoContaCodigo)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("EAPItem.PlanoConta.Codigo");
            entity.Property(e => e.EapitemPlanoContaDescricao)
                .HasMaxLength(256)
                .IsUnicode(false)
                .HasColumnName("EAPItem.PlanoConta.Descricao");
            entity.Property(e => e.EntidadeCodigo).HasColumnName("Entidade.Codigo");
            entity.Property(e => e.EntidadeNome)
                .HasMaxLength(128)
                .IsUnicode(false)
                .HasColumnName("Entidade.Nome");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Origem)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.PlanoContaAcessoRestrito).HasColumnName("PlanoConta.AcessoRestrito");
            entity.Property(e => e.SaldoMovimentos).HasColumnType("numeric(18, 4)");
            entity.Property(e => e.ValorCompromisso).HasColumnType("numeric(18, 4)");
            entity.Property(e => e.ValorFaturar).HasColumnType("numeric(18, 4)");
            entity.Property(e => e.ValorLiquidado).HasColumnType("numeric(18, 4)");
        });

        modelBuilder.Entity<TbControleCusto>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbControleCusto");

            entity.Property(e => e.CentroResultado)
                .HasMaxLength(16)
                .IsUnicode(false);
            entity.Property(e => e.NivelPlanoContaOrdem).HasColumnName("NivelPlanoConta.Ordem");
            entity.Property(e => e.PlanejadoData).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.PlanoContaAnalitico).HasColumnName("PlanoConta.Analitico");
            entity.Property(e => e.PlanoContaCodigo)
                .HasMaxLength(32)
                .IsUnicode(false)
                .HasColumnName("PlanoConta.Codigo");
            entity.Property(e => e.PlanoContaDescricao)
                .HasMaxLength(512)
                .IsUnicode(false)
                .HasColumnName("PlanoConta.Descricao");
            entity.Property(e => e.PlanoContaId).HasColumnName("PlanoConta.ID");
            entity.Property(e => e.PlanoContaNatureza).HasColumnName("PlanoConta.Natureza");
            entity.Property(e => e.PlanoContaNivel).HasColumnName("PlanoConta.Nivel");
            entity.Property(e => e.ValorCompromissado).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorIncorrido).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorOrcado).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorPlanejado).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorRealizado).HasColumnType("numeric(18, 2)");
        });

        modelBuilder.Entity<TbFluxoCaixaReal>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("TbFluxoCaixaReal");

            entity.Property(e => e.Apresentacao)
                .HasMaxLength(256)
                .IsUnicode(false);
            entity.Property(e => e.Cr)
                .HasMaxLength(64)
                .IsUnicode(false)
                .HasColumnName("CR");
            entity.Property(e => e.Data)
                .HasMaxLength(24)
                .IsUnicode(false);
            entity.Property(e => e.DataCalc).HasColumnType("datetime");
            entity.Property(e => e.DataInicioDiario).HasColumnType("datetime");
            entity.Property(e => e.DataInicioMensal).HasColumnType("datetime");
            entity.Property(e => e.DataInicioSemanal).HasColumnType("datetime");
            entity.Property(e => e.TipoApresentacao).HasColumnName("tipoApresentacao");
            entity.Property(e => e.ValorCredito).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.ValorDebito).HasColumnType("numeric(18, 2)");
        });

        modelBuilder.Entity<TbHorasPlanejadasDocumentoT>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbHorasPlanejadasDocumentoTS");

            entity.Property(e => e.CodDisciplina).HasColumnName("codDisciplina");
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Documento)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoDescricao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Empresa).HasMaxLength(256);
            entity.Property(e => e.Planejada).HasColumnType("numeric(38, 15)");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
        });

        modelBuilder.Entity<TbHorasPlanejadasT>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbHorasPlanejadasTS");

            entity.Property(e => e.CodDisciplina).HasColumnName("codDisciplina");
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Empresa).HasMaxLength(256);
            entity.Property(e => e.Planejada).HasColumnType("numeric(38, 15)");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
        });

        modelBuilder.Entity<TbInfo>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbInfo");

            entity.Property(e => e.Email).HasMaxLength(128);
            entity.Property(e => e.Patrimonio).HasMaxLength(24);
            entity.Property(e => e.Usuario).HasMaxLength(128);
        });

        modelBuilder.Entity<TbParcelasAfaturar>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbParcelasAFaturar");

            entity.Property(e => e.CentroResultado)
                .HasMaxLength(8)
                .IsUnicode(false);
            entity.Property(e => e.EapId).HasColumnName("EAP.ID");
            entity.Property(e => e.EntidadeCodigo).HasColumnName("Entidade.Codigo");
            entity.Property(e => e.EntidadeNome)
                .HasMaxLength(164)
                .IsUnicode(false)
                .HasColumnName("Entidade.Nome");
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Observacao)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.Periodo)
                .HasMaxLength(24)
                .IsUnicode(false);
            entity.Property(e => e.RazaoSocial)
                .HasMaxLength(124)
                .IsUnicode(false);
            entity.Property(e => e.Responsavel)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Valor).HasColumnType("numeric(18, 2)");
        });

        modelBuilder.Entity<TbPatrimonio>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbPatrimonio");

            entity.Property(e => e.Alugado).HasMaxLength(6);
            entity.Property(e => e.Crreferencia)
                .HasMaxLength(16)
                .HasColumnName("CRReferencia");
            entity.Property(e => e.Data)
                .HasColumnType("datetime")
                .HasColumnName("data");
            entity.Property(e => e.Descricao).HasMaxLength(256);
            entity.Property(e => e.Disponivel).HasMaxLength(20);
            entity.Property(e => e.Faixa).HasMaxLength(128);
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.NotaFiscal).HasMaxLength(24);
            entity.Property(e => e.Responsavel).HasMaxLength(256);
            entity.Property(e => e.Tipo).HasMaxLength(64);
            entity.Property(e => e.Valor).HasColumnType("numeric(18, 2)");
        });

        modelBuilder.Entity<TbTimeSheet>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbTimeSheet");

            entity.Property(e => e.Afe)
                .HasMaxLength(24)
                .HasColumnName("AFE");
            entity.Property(e => e.Data)
                .HasColumnType("datetime")
                .HasColumnName("data");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Entrada).HasColumnType("datetime");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Intervalo).HasColumnType("numeric(38, 15)");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.Saida).HasColumnType("datetime");
            entity.Property(e => e.SemanaDesk).HasMaxLength(32);
            entity.Property(e => e.Trabalhadas).HasColumnType("numeric(38, 15)");
        });

        modelBuilder.Entity<TbTimeSheetRealizadoDocumento>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("tbTimeSheetRealizadoDocumento");

            entity.Property(e => e.Data)
                .HasColumnType("datetime")
                .HasColumnName("data");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.Planejadas).HasColumnType("numeric(38, 15)");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.Trabalhadas).HasColumnType("numeric(38, 15)");
        });

        modelBuilder.Entity<TbgestaoNac007>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("TBGestaoNac007");

            entity.Property(e => e.Conta).HasMaxLength(64);
            entity.Property(e => e.ContaDescricao).HasMaxLength(128);
            entity.Property(e => e.Cr)
                .HasMaxLength(20)
                .HasColumnName("CR");
            entity.Property(e => e.Data).HasColumnType("datetime");
            entity.Property(e => e.Entidade).HasMaxLength(256);
            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Natureza).HasColumnName("natureza");
            entity.Property(e => e.Obs)
                .HasMaxLength(512)
                .HasColumnName("OBS");
            entity.Property(e => e.Parcela).HasMaxLength(20);
            entity.Property(e => e.Rateio).HasColumnType("numeric(18, 2)");
            entity.Property(e => e.Restrito).HasColumnName("restrito");
            entity.Property(e => e.ValorContrato).HasColumnType("numeric(18, 2)");
        });

        modelBuilder.Entity<TbmovEquipamento>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("TBMovEquipamento");

            entity.Property(e => e.Data)
                .HasColumnType("datetime")
                .HasColumnName("data");
            entity.Property(e => e.Host)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.Rede)
                .HasMaxLength(512)
                .IsUnicode(false)
                .HasColumnName("rede");
            entity.Property(e => e.Ukey)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Usuario)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Usuarios)
                .HasMaxLength(512)
                .IsUnicode(false);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
