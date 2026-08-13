using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AccIntegration.WebApp.Models.NAConnect;

public partial class NAConnectDbContext : DbContext
{
    public NAConnectDbContext()
    {
    }

    public NAConnectDbContext(DbContextOptions<NAConnectDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<CronogramaProjeto> CronogramaProjetos { get; set; }

    public virtual DbSet<HubsContum> HubsConta { get; set; }

    public virtual DbSet<LdpUnificadaDado> LdpUnificadaDados { get; set; }

    public virtual DbSet<LdpUnificadum> LdpUnificada { get; set; }

    public virtual DbSet<MetadadosProjeto> MetadadosProjetos { get; set; }

    public virtual DbSet<MetadadosProjeto1> MetadadosProjetos1 { get; set; }

    public virtual DbSet<Projeto> Projetos { get; set; }

    public virtual DbSet<TbCustoProfissional> TbCustoProfissionals { get; set; }

    public virtual DbSet<TbCustoProfissionalAlias> TbCustoProfissionalAliases { get; set; }

    public virtual DbSet<TbOrcamentoDisciplina> TbOrcamentoDisciplinas { get; set; }

    public virtual DbSet<TbTimeSheetCorrecaoManual> TbTimeSheetCorrecaoManuals { get; set; }

    public virtual DbSet<VwAlgumaCoisa> VwAlgumaCoisas { get; set; }

    public virtual DbSet<VwCronogramaProjetosUltimaVersao> VwCronogramaProjetosUltimaVersaos { get; set; }

    public virtual DbSet<VwCustoDiarioDocumento> VwCustoDiarioDocumentos { get; set; }

    public virtual DbSet<VwCustoDiarioDocumentoPessoa> VwCustoDiarioDocumentoPessoas { get; set; }

    public virtual DbSet<VwCustoRealizadoDiarioDocumento> VwCustoRealizadoDiarioDocumentos { get; set; }

    public virtual DbSet<VwCustoRealizadoDiarioDocumentoBml011013> VwCustoRealizadoDiarioDocumentoBml011013s { get; set; }

    public virtual DbSet<VwCustoRealizadoDiarioDocumentoPessoa> VwCustoRealizadoDiarioDocumentoPessoas { get; set; }

    public virtual DbSet<VwCustoRealizadoDiarioDocumentoPessoaBml011013> VwCustoRealizadoDiarioDocumentoPessoaBml011013s { get; set; }

    public virtual DbSet<VwCustoRealizadoSemanalDocumento> VwCustoRealizadoSemanalDocumentos { get; set; }

    public virtual DbSet<VwCustoRealizadoSemanalDocumentoBml011013> VwCustoRealizadoSemanalDocumentoBml011013s { get; set; }

    public virtual DbSet<VwCustoRealizadoSemanalDocumentoPessoa> VwCustoRealizadoSemanalDocumentoPessoas { get; set; }

    public virtual DbSet<VwCustoRealizadoSemanalDocumentoPessoaBml011013> VwCustoRealizadoSemanalDocumentoPessoaBml011013s { get; set; }

    public virtual DbSet<VwCustoRealizadoTimesheetBaseBml011013> VwCustoRealizadoTimesheetBaseBml011013s { get; set; }

    public virtual DbSet<VwCustoSemanalDocumento> VwCustoSemanalDocumentos { get; set; }

    public virtual DbSet<VwCustoSemanalDocumentoPessoa> VwCustoSemanalDocumentoPessoas { get; set; }

    public virtual DbSet<VwHhDistribuidoNoTempo> VwHhDistribuidoNoTempos { get; set; }

    public virtual DbSet<VwLdpDocumentosAtivo> VwLdpDocumentosAtivos { get; set; }

    public virtual DbSet<VwLdpHhPlanejadoVsRealizado> VwLdpHhPlanejadoVsRealizados { get; set; }

    public virtual DbSet<VwLdpPrevistoRealizadoDiarioTeste> VwLdpPrevistoRealizadoDiarioTestes { get; set; }

    public virtual DbSet<VwLdpUnificadaNova> VwLdpUnificadaNovas { get; set; }

    public virtual DbSet<VwLdpUnificadaTeste> VwLdpUnificadaTestes { get; set; }

    public virtual DbSet<VwLdpUnificadaUltimaVersao> VwLdpUnificadaUltimaVersaos { get; set; }

    public virtual DbSet<VwLdpUnificadaUltimaVersaoTeste> VwLdpUnificadaUltimaVersaoTestes { get; set; }

    public virtual DbSet<VwNAConnectWeeklyControl> VwNAConnectWeeklyControls { get; set; }

    public virtual DbSet<VwResumoHhDefinitiva> VwResumoHhDefinitivas { get; set; }

    public virtual DbSet<VwResumoHhLdpCompletum> VwResumoHhLdpCompleta { get; set; }

    public virtual DbSet<VwResumoHhPorDocumento> VwResumoHhPorDocumentos { get; set; }

    public virtual DbSet<VwResumoHhRateioTempo> VwResumoHhRateioTempos { get; set; }

    public virtual DbSet<VwTimeSheetNa> VwTimeSheetNas { get; set; }

    public virtual DbSet<VwTimeSheetNaCorrigidum> VwTimeSheetNaCorrigida { get; set; }

    public virtual DbSet<VwTimeSheetRealizadoPorPessoa> VwTimeSheetRealizadoPorPessoas { get; set; }

    public virtual DbSet<VwTimesheetSemLdp> VwTimesheetSemLdps { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseSqlServer("Server=100.76.16.82,14333;Database=DB_NA_CONNECT;User Id=usuario_bi;Password=SenhaBI@2026;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("SQL_Latin1_General_CP1_CI_AS");

        modelBuilder.Entity<CronogramaProjeto>(entity =>
        {
            entity.HasKey(e => e.IdCronograma).HasName("PK__Cronogra__7269D6B02F7AC24D");

            entity.ToTable("Cronograma_Projetos");

            entity.Property(e => e.IdCronograma).HasColumnName("ID_Cronograma");
            entity.Property(e => e.CrOs)
                .HasMaxLength(50)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.EdtWbs)
                .HasMaxLength(100)
                .HasColumnName("EDT_WBS");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_DOCUMENT_NUMBER");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("PERCENT_PHYSICAL_COMPLETED");
            entity.Property(e => e.PercentPredictedPhysical)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("PERCENT_PREDICTED_PHYSICAL");
            entity.Property(e => e.PlannedDataBaseline)
                .HasColumnType("datetime")
                .HasColumnName("PLANNED_DATA_BASELINE");
            entity.Property(e => e.PlannedDateStart)
                .HasColumnType("datetime")
                .HasColumnName("PLANNED_DATE_START");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("REPLANNED_DATE");
            entity.Property(e => e.TaskName).HasColumnName("TASK_NAME");
            entity.Property(e => e.UidTarefa)
                .HasMaxLength(100)
                .HasColumnName("UID_Tarefa");
            entity.Property(e => e.WbsPercent)
                .HasMaxLength(50)
                .HasColumnName("WBS_PERCENT");
        });

        modelBuilder.Entity<HubsContum>(entity =>
        {
            entity.HasKey(e => e.HubContaId).HasName("PK__Hubs_Con__30B4FEFD2F34F8F4");

            entity.ToTable("Hubs_Conta");

            entity.Property(e => e.HubContaId).HasColumnName("HubContaID");
            entity.Property(e => e.AccountIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Account_ID_ACC");
            entity.Property(e => e.NomeHub)
                .HasMaxLength(100)
                .HasColumnName("Nome_Hub");
        });

        modelBuilder.Entity<LdpUnificadaDado>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__LDP_Unif__3214EC27A86E459B");

            entity.ToTable("LDP_Unificada_Dados");

            entity.HasIndex(e => e.ProjectIdAcc, "IX_LDP_Unificada_ProjectID");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DateCommentsReceived)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle)
                .IsUnicode(false)
                .HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.Format)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.InclusionDate)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.NomeProjeto)
                .HasMaxLength(500)
                .IsUnicode(false)
                .HasColumnName("Nome_Projeto");
            entity.Property(e => e.NumPages).HasColumnName("Num_Pages");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Rev)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.Seq)
                .HasMaxLength(50)
                .IsUnicode(false);
            entity.Property(e => e.Situation)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.StatusFlow)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Status_Flow");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<LdpUnificadum>(entity =>
        {
            entity.HasKey(e => e.IdRegistro).HasName("PK__Ldp_Unif__8EC639F26885858A");

            entity.ToTable("Ldp_Unificada");

            entity.Property(e => e.IdRegistro).HasColumnName("ID_Registro");
            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.ClientDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("Client_Document_Number");
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(100)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DateCommentsReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(100)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.Format).HasMaxLength(50);
            entity.Property(e => e.InclusionDate)
                .HasColumnType("datetime")
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.Item).HasMaxLength(50);
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.NoPages).HasColumnName("No_Pages");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Revision).HasMaxLength(50);
            entity.Property(e => e.ScheduleId)
                .HasMaxLength(100)
                .HasColumnName("Schedule_ID");
            entity.Property(e => e.Seq).HasMaxLength(50);
            entity.Property(e => e.Situation).HasMaxLength(100);
            entity.Property(e => e.StatusInFlow)
                .HasMaxLength(100)
                .HasColumnName("Status_In_Flow");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<MetadadosProjeto>(entity =>
        {
            entity.HasKey(e => e.MetadataId).HasName("PK__Metadado__66106FF9E987584D");

            entity.ToTable("Metadados_Projeto");

            entity.Property(e => e.MetadataId).HasColumnName("MetadataID");
            entity.Property(e => e.HubProjetoId)
                .HasMaxLength(100)
                .HasColumnName("Hub_Projeto_ID");
            entity.Property(e => e.NumeroNaEn)
                .HasMaxLength(100)
                .HasColumnName("Numero_NA_EN");
            entity.Property(e => e.NumeroNaPt)
                .HasMaxLength(100)
                .HasColumnName("Numero_NA_PT");
            entity.Property(e => e.ProjetoId).HasColumnName("ProjetoID");

            entity.HasOne(d => d.Projeto).WithMany(p => p.MetadadosProjetos)
                .HasForeignKey(d => d.ProjetoId)
                .HasConstraintName("FK__Metadados__Proje__2A4B4B5E");
        });

        modelBuilder.Entity<MetadadosProjeto1>(entity =>
        {
            entity.HasKey(e => e.MetadadoId).HasName("PK__Metadado__9733920D37EB188D");

            entity.ToTable("Metadados_Projetos");

            entity.Property(e => e.MetadadoId).HasColumnName("MetadadoID");
            entity.Property(e => e.AccountIdAcc)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("Account_ID_ACC");
            entity.Property(e => e.ColunaChave)
                .HasMaxLength(100)
                .IsUnicode(false)
                .HasColumnName("Coluna_Chave");
            entity.Property(e => e.DataUltimaAtualizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Ultima_Atualizacao");
            entity.Property(e => e.DataUpdate)
                .HasColumnType("datetime")
                .HasColumnName("Data_Update");
            entity.Property(e => e.DescricaoCorporativa).HasColumnName("Descricao_Corporativa");
            entity.Property(e => e.Idioma)
                .HasMaxLength(10)
                .IsUnicode(false);
            entity.Property(e => e.LdpFileId)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("LDP_File_ID");
            entity.Property(e => e.NomeLdp)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("Nome_LDP");
            entity.Property(e => e.NumeroNa)
                .HasMaxLength(50)
                .HasColumnName("Numero_NA");
            entity.Property(e => e.PadraoNomenclatura)
                .HasMaxLength(20)
                .IsUnicode(false)
                .HasColumnName("Padrao_Nomenclatura");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(255)
                .HasColumnName("Project_ID_ACC");

            entity.HasOne(d => d.ProjectIdAccNavigation).WithMany(p => p.MetadadosProjeto1s)
                .HasPrincipalKey(p => p.ProjectIdAcc)
                .HasForeignKey(d => d.ProjectIdAcc)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Metadados_Projetos_ACC");
        });

        modelBuilder.Entity<Projeto>(entity =>
        {
            entity.HasKey(e => e.ProjetoId).HasName("PK__Projetos__F7594DFD8137F6DB");

            entity.HasIndex(e => e.ProjectIdAcc, "UQ_Project_ID_ACC").IsUnique();

            entity.Property(e => e.ProjetoId).HasColumnName("ProjetoID");
            entity.Property(e => e.FileIdLdpAcc)
                .HasMaxLength(255)
                .HasColumnName("File_ID_LDP_ACC");
            entity.Property(e => e.HubContaId).HasColumnName("HubContaID");
            entity.Property(e => e.IsAtivo).HasColumnName("Is_Ativo");
            entity.Property(e => e.NamingStandard)
                .HasMaxLength(20)
                .HasColumnName("Naming_Standard");
            entity.Property(e => e.NomeProjeto)
                .HasMaxLength(200)
                .HasColumnName("Nome_Projeto");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(255)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.ProjectLanguage)
                .HasMaxLength(10)
                .HasColumnName("Project_Language");

            entity.HasOne(d => d.HubConta).WithMany(p => p.Projetos)
                .HasForeignKey(d => d.HubContaId)
                .HasConstraintName("FK__Projetos__HubCon__276EDEB3");
        });

        modelBuilder.Entity<TbCustoProfissional>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__tbCustoP__3214EC27057BE658");

            entity.ToTable("tbCustoProfissional");

            entity.HasIndex(e => new { e.ProjetoCodigo, e.NomeProfissional, e.Ativo }, "IX_tbCustoProfissional_Projeto_Nome");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Computadores).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.CustoHora).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.CustoTotalMensal).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.DisciplinaCusto).HasMaxLength(200);
            entity.Property(e => e.Encargos).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.FuncaoCusto).HasMaxLength(200);
            entity.Property(e => e.NomeProfissional).HasMaxLength(200);
            entity.Property(e => e.Origem).HasMaxLength(200);
            entity.Property(e => e.ProjetoCodigo).HasMaxLength(50);
            entity.Property(e => e.Remuneracao).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.Softwares).HasColumnType("decimal(18, 6)");
        });

        modelBuilder.Entity<TbCustoProfissionalAlias>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__tbCustoP__3214EC2746BF465D");

            entity.ToTable("tbCustoProfissionalAlias");

            entity.HasIndex(e => new { e.ProjetoCodigo, e.NomeAlias, e.Ativo }, "IX_tbCustoProfissionalAlias_Projeto_Alias");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.NomeAlias).HasMaxLength(200);
            entity.Property(e => e.NomeProfissional).HasMaxLength(200);
            entity.Property(e => e.Observacao).HasMaxLength(500);
            entity.Property(e => e.ProjetoCodigo).HasMaxLength(50);
        });

        modelBuilder.Entity<TbOrcamentoDisciplina>(entity =>
        {
            entity.HasKey(e => new { e.ProjectIdAcc, e.Discipline }).HasName("PK_Orcamento_Disciplina");

            entity.ToTable("TB_Orcamento_Disciplina");

            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(255)
                .IsUnicode(false)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.Discipline)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.TotalHhOrcado)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Total_HH_Orcado");
        });

        modelBuilder.Entity<TbTimeSheetCorrecaoManual>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__tbTimeSh__3214EC272664FED2");

            entity.ToTable("tbTimeSheetCorrecaoManual");

            entity.Property(e => e.Id).HasColumnName("ID");
            entity.Property(e => e.Activity).IsUnicode(false);
            entity.Property(e => e.Afe)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("AFE");
            entity.Property(e => e.DataCadastro).HasColumnType("datetime");
            entity.Property(e => e.Discipline)
                .HasMaxLength(100)
                .IsUnicode(false);
            entity.Property(e => e.DocCode)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.DocDescription)
                .HasMaxLength(500)
                .IsUnicode(false);
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .IsUnicode(false);
            entity.Property(e => e.Wh)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("WH");
        });

        modelBuilder.Entity<VwAlgumaCoisa>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_AlgumaCoisa");

            entity.Property(e => e.DescricaoCorporativa).HasColumnName("Descricao_Corporativa");
            entity.Property(e => e.MetadadoId)
                .ValueGeneratedOnAdd()
                .HasColumnName("MetadadoID");
            entity.Property(e => e.NumeroNa)
                .HasMaxLength(50)
                .HasColumnName("Numero_NA");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(255)
                .HasColumnName("Project_ID_ACC");
        });

        modelBuilder.Entity<VwCronogramaProjetosUltimaVersao>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Cronograma_Projetos_Ultima_Versao");

            entity.Property(e => e.CrOs)
                .HasMaxLength(50)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.EdtWbs)
                .HasMaxLength(100)
                .HasColumnName("EDT_WBS");
            entity.Property(e => e.IdCronograma).HasColumnName("ID_Cronograma");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_DOCUMENT_NUMBER");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("PERCENT_PHYSICAL_COMPLETED");
            entity.Property(e => e.PercentPredictedPhysical)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("PERCENT_PREDICTED_PHYSICAL");
            entity.Property(e => e.PlannedDataBaseline)
                .HasColumnType("datetime")
                .HasColumnName("PLANNED_DATA_BASELINE");
            entity.Property(e => e.PlannedDateStart)
                .HasColumnType("datetime")
                .HasColumnName("PLANNED_DATE_START");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("REPLANNED_DATE");
            entity.Property(e => e.TaskName).HasColumnName("TASK_NAME");
            entity.Property(e => e.UidTarefa)
                .HasMaxLength(100)
                .HasColumnName("UID_Tarefa");
            entity.Property(e => e.WbsPercent)
                .HasMaxLength(50)
                .HasColumnName("WBS_PERCENT");
        });

        modelBuilder.Entity<VwCustoDiarioDocumento>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Diario_Documento");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(1015)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.CustoPrevistoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Previsto_Diario");
            entity.Property(e => e.CustoRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.Doc1)
                .HasMaxLength(200)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(200)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(200)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(200)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(200)
                .HasColumnName("doc5");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.HhPrevistoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Previsto_Diario");
            entity.Property(e => e.HhRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(512)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.OrigemDataPrevisto)
                .HasMaxLength(16)
                .IsUnicode(false)
                .HasColumnName("Origem_Data_Previsto");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
        });

        modelBuilder.Entity<VwCustoDiarioDocumentoPessoa>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Diario_Documento_Pessoa");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.ChaveDiariaDocPessoa)
                .HasMaxLength(290)
                .HasColumnName("Chave_Diaria_Doc_Pessoa");
            entity.Property(e => e.CustoPrevistoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Previsto_Diario");
            entity.Property(e => e.CustoPrevistoDiarioPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Previsto_Diario_Pessoa");
            entity.Property(e => e.CustoRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario");
            entity.Property(e => e.CustoRealizadoDiarioPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario_Pessoa");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhPlanejadoTimeSheetPessoa)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("HH_Planejado_TimeSheet_Pessoa");
            entity.Property(e => e.HhPrevistoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Previsto_Diario");
            entity.Property(e => e.HhRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario");
            entity.Property(e => e.HhRealizadoDiarioPessoa)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("HH_Realizado_Diario_Pessoa");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(512)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.OrigemDataPrevisto)
                .HasMaxLength(16)
                .IsUnicode(false)
                .HasColumnName("Origem_Data_Previsto");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwCustoRealizadoDiarioDocumento>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Diario_Documento");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.CustoRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.HhRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.ProjetoCodigoCusto).HasMaxLength(50);
            entity.Property(e => e.TemPessoaSemCadastroCusto).HasColumnName("Tem_Pessoa_Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoDiarioDocumentoBml011013>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Diario_Documento_BML011_013");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.CustoRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.HhRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.TemPessoaSemCadastroCusto).HasColumnName("Tem_Pessoa_Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoDiarioDocumentoPessoa>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Diario_Documento_Pessoa");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.ChaveDiariaDocPessoa)
                .HasMaxLength(290)
                .HasColumnName("Chave_Diaria_Doc_Pessoa");
            entity.Property(e => e.CustoHora).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.CustoPrevistoDiarioDoc)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Previsto_Diario_Doc");
            entity.Property(e => e.CustoPrevistoDiarioPessoaBase)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Previsto_Diario_Pessoa_Base");
            entity.Property(e => e.CustoRealizadoDiarioPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario_Pessoa");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhPlanejadasTimeSheetDoc)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Planejadas_TimeSheet_Doc");
            entity.Property(e => e.HhPrevistoDiarioDoc)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Previsto_Diario_Doc");
            entity.Property(e => e.HhPrevistoDiarioPessoaBase)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Previsto_Diario_Pessoa_Base");
            entity.Property(e => e.HhRealizadoDiarioDoc)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario_Doc");
            entity.Property(e => e.HhRealizadoDiarioPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario_Pessoa");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.NomeProfissionalCusto).HasMaxLength(200);
            entity.Property(e => e.OrigemDataPrevisto)
                .HasMaxLength(16)
                .IsUnicode(false)
                .HasColumnName("Origem_Data_Previsto");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.ProjetoCodigoCusto).HasMaxLength(50);
            entity.Property(e => e.SemCadastroCusto).HasColumnName("Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoDiarioDocumentoPessoaBml011013>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Diario_Documento_Pessoa_BML011_013");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.ChaveDiariaDocPessoa)
                .HasMaxLength(290)
                .HasColumnName("Chave_Diaria_Doc_Pessoa");
            entity.Property(e => e.CustoHora).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.CustoRealizadoDiarioPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario_Pessoa");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhRealizadoDiarioPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario_Pessoa");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.NomeProfissionalCusto).HasMaxLength(200);
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.SemCadastroCusto).HasColumnName("Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoSemanalDocumento>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Semanal_Documento");

            entity.Property(e => e.ChaveSemanalDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Semanal_Doc");
            entity.Property(e => e.CustoRealizadoSemanal)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Semanal");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.HhRealizadoSemanal)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Semanal");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.ProjetoCodigoCusto).HasMaxLength(50);
            entity.Property(e => e.TemPessoaSemCadastroCusto).HasColumnName("Tem_Pessoa_Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoSemanalDocumentoBml011013>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Semanal_Documento_BML011_013");

            entity.Property(e => e.ChaveSemanalDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Semanal_Doc");
            entity.Property(e => e.CustoRealizadoSemanal)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Semanal");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.HhRealizadoSemanal)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Semanal");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.TemPessoaSemCadastroCusto).HasColumnName("Tem_Pessoa_Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoSemanalDocumentoPessoa>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Semanal_Documento_Pessoa");

            entity.Property(e => e.ChaveSemanalDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Semanal_Doc");
            entity.Property(e => e.ChaveSemanalDocPessoa)
                .HasMaxLength(290)
                .HasColumnName("Chave_Semanal_Doc_Pessoa");
            entity.Property(e => e.CustoRealizadoSemanalPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Semanal_Pessoa");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhRealizadoSemanalPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Semanal_Pessoa");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.NomeProfissionalCusto).HasMaxLength(200);
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.ProjetoCodigoCusto).HasMaxLength(50);
            entity.Property(e => e.SemCadastroCusto).HasColumnName("Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoSemanalDocumentoPessoaBml011013>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Semanal_Documento_Pessoa_BML011_013");

            entity.Property(e => e.ChaveSemanalDocPessoa)
                .HasMaxLength(290)
                .HasColumnName("Chave_Semanal_Doc_Pessoa");
            entity.Property(e => e.CustoRealizadoSemanalPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Semanal_Pessoa");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhRealizadoSemanalPessoa)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Semanal_Pessoa");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.NomeProfissionalCusto).HasMaxLength(200);
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.SemCadastroCusto).HasColumnName("Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoRealizadoTimesheetBaseBml011013>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Realizado_Timesheet_Base_BML011_013");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.ChaveDiariaDocPessoa)
                .HasMaxLength(290)
                .HasColumnName("Chave_Diaria_Doc_Pessoa");
            entity.Property(e => e.CustoHora).HasColumnType("decimal(18, 6)");
            entity.Property(e => e.CustoRealizadoDiarioPessoa)
                .HasColumnType("decimal(18, 6)")
                .HasColumnName("Custo_Realizado_Diario_Pessoa");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhRealizadoDiarioPessoa)
                .HasColumnType("decimal(38, 6)")
                .HasColumnName("HH_Realizado_Diario_Pessoa");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.NomeProfissionalMapeado).HasMaxLength(200);
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.SemCadastroCusto).HasColumnName("Sem_Cadastro_Custo");
        });

        modelBuilder.Entity<VwCustoSemanalDocumento>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Semanal_Documento");

            entity.Property(e => e.ChaveSemanalDoc)
                .HasMaxLength(1015)
                .HasColumnName("Chave_Semanal_Doc");
            entity.Property(e => e.CustoPrevistoSemanal)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("Custo_Previsto_Semanal");
            entity.Property(e => e.CustoRealizadoSemanal)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("Custo_Realizado_Semanal");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.Doc1)
                .HasMaxLength(200)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(200)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(200)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(200)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(200)
                .HasColumnName("doc5");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.HhPrevistoSemanal)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("HH_Previsto_Semanal");
            entity.Property(e => e.HhRealizadoSemanal)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("HH_Realizado_Semanal");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(512)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
        });

        modelBuilder.Entity<VwCustoSemanalDocumentoPessoa>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Custo_Semanal_Documento_Pessoa");

            entity.Property(e => e.ChaveSemanalDocPessoa)
                .HasMaxLength(290)
                .HasColumnName("Chave_Semanal_Doc_Pessoa");
            entity.Property(e => e.CustoPrevistoSemanalPessoa)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("Custo_Previsto_Semanal_Pessoa");
            entity.Property(e => e.CustoRealizadoSemanalPessoa)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("Custo_Realizado_Semanal_Pessoa");
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhPlanejadoSemanalPessoa)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("HH_Planejado_Semanal_Pessoa");
            entity.Property(e => e.HhRealizadoSemanalPessoa)
                .HasColumnType("decimal(38, 4)")
                .HasColumnName("HH_Realizado_Semanal_Pessoa");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(512)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwHhDistribuidoNoTempo>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_HH_Distribuido_No_Tempo");

            entity.Property(e => e.DataApontamento)
                .HasColumnType("datetime")
                .HasColumnName("Data_Apontamento");
            entity.Property(e => e.DataPlanejadaBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Data_Planejada_Baseline");
            entity.Property(e => e.DescricoesAgrupadas)
                .IsUnicode(false)
                .HasColumnName("Descricoes_Agrupadas");
            entity.Property(e => e.Disciplina).HasMaxLength(100);
            entity.Property(e => e.Doc1)
                .HasMaxLength(100)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(100)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(100)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(100)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(50)
                .HasColumnName("doc5");
            entity.Property(e => e.DocumentoCodigo).HasMaxLength(100);
            entity.Property(e => e.HhPrevistoProporcionalDia)
                .HasColumnType("numeric(38, 6)")
                .HasColumnName("HH_Previsto_Proporcional_Dia");
            entity.Property(e => e.HhPrevistoTotalEstatico)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("HH_Previsto_Total_Estatico");
            entity.Property(e => e.HhRealizadoDia)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Realizado_Dia");
            entity.Property(e => e.NomeProjeto)
                .HasMaxLength(200)
                .HasColumnName("Nome_Projeto");
            entity.Property(e => e.Projeto).HasMaxLength(100);
        });

        modelBuilder.Entity<VwLdpDocumentosAtivo>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_LDP_DOCUMENTOS_ATIVOS");

            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.ClientDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("Client_Document_Number");
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(100)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DateCommentsReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.Doc1)
                .HasMaxLength(100)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(100)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(100)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(100)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(50)
                .HasColumnName("doc5");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(100)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.Format).HasMaxLength(50);
            entity.Property(e => e.IdRegistro).HasColumnName("ID_Registro");
            entity.Property(e => e.InclusionDate)
                .HasColumnType("datetime")
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.Item).HasMaxLength(50);
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.NoPages).HasColumnName("No_Pages");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Revision).HasMaxLength(50);
            entity.Property(e => e.Rn).HasColumnName("rn");
            entity.Property(e => e.ScheduleId)
                .HasMaxLength(100)
                .HasColumnName("Schedule_ID");
            entity.Property(e => e.Seq).HasMaxLength(50);
            entity.Property(e => e.Situation).HasMaxLength(100);
            entity.Property(e => e.StatusInFlow)
                .HasMaxLength(100)
                .HasColumnName("Status_In_Flow");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<VwLdpHhPlanejadoVsRealizado>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_LDP_HH_Planejado_vs_Realizado");

            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.Doc1)
                .HasMaxLength(100)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(100)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(100)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(100)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(50)
                .HasColumnName("doc5");
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.HhPrevistoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Previsto_Diario");
            entity.Property(e => e.HhRealizadoDiario)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Realizado_Diario");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(512)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
        });

        modelBuilder.Entity<VwLdpPrevistoRealizadoDiarioTeste>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Ldp_Previsto_Realizado_Diario_Teste");

            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(1015)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.ClientDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("Client_Document_Number");
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(100)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.CustoPrevistoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Previsto_Diario");
            entity.Property(e => e.CustoRealizadoDiarioEstimado)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Custo_Realizado_Diario_Estimado");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DateCommentsReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.Doc1)
                .HasMaxLength(200)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(200)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(200)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(200)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(200)
                .HasColumnName("doc5");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(100)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Format).HasMaxLength(50);
            entity.Property(e => e.HhPlanejadasTimeSheet)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Planejadas_TimeSheet");
            entity.Property(e => e.HhPrevistoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Previsto_Diario");
            entity.Property(e => e.HhRealizadoDiario)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado_Diario");
            entity.Property(e => e.IdRegistro).HasColumnName("ID_Registro");
            entity.Property(e => e.InclusionDate)
                .HasColumnType("datetime")
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.Item).HasMaxLength(50);
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(512)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.NoPages).HasColumnName("No_Pages");
            entity.Property(e => e.OrigemDataPrevisto)
                .HasMaxLength(16)
                .IsUnicode(false)
                .HasColumnName("Origem_Data_Previsto");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Revision).HasMaxLength(50);
            entity.Property(e => e.ScheduleId)
                .HasMaxLength(100)
                .HasColumnName("Schedule_ID");
            entity.Property(e => e.Seq).HasMaxLength(50);
            entity.Property(e => e.Situation).HasMaxLength(100);
            entity.Property(e => e.StatusInFlow)
                .HasMaxLength(100)
                .HasColumnName("Status_In_Flow");
            entity.Property(e => e.TemPrevisto).HasColumnName("Tem_Previsto");
            entity.Property(e => e.TemRealizado).HasColumnName("Tem_Realizado");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<VwLdpUnificadaNova>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Ldp_Unificada_Nova");

            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.ClientDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("Client_Document_Number");
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(100)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataDateConsertada).HasColumnName("Data_Date_Consertada");
            entity.Property(e => e.DataFimSemana).HasColumnName("Data_Fim_Semana");
            entity.Property(e => e.DataInicioSemana).HasColumnName("Data_Inicio_Semana");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DateCommentsReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(100)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.Format).HasMaxLength(50);
            entity.Property(e => e.HhTimeSheet)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_TimeSheet");
            entity.Property(e => e.IdRegistro).HasColumnName("ID_Registro");
            entity.Property(e => e.InclusionDate)
                .HasColumnType("datetime")
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.Item).HasMaxLength(50);
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.NoPages).HasColumnName("No_Pages");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Revision).HasMaxLength(50);
            entity.Property(e => e.ScheduleId)
                .HasMaxLength(100)
                .HasColumnName("Schedule_ID");
            entity.Property(e => e.Seq).HasMaxLength(50);
            entity.Property(e => e.Situation).HasMaxLength(100);
            entity.Property(e => e.StatusInFlow)
                .HasMaxLength(100)
                .HasColumnName("Status_In_Flow");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<VwLdpUnificadaTeste>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Ldp_Unificada_Teste");

            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.ClientDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("Client_Document_Number");
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(100)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DateCommentsReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(100)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.Format).HasMaxLength(50);
            entity.Property(e => e.HhTimeSheet)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_TimeSheet");
            entity.Property(e => e.IdRegistro).HasColumnName("ID_Registro");
            entity.Property(e => e.InclusionDate)
                .HasColumnType("datetime")
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.Item).HasMaxLength(50);
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.NoPages).HasColumnName("No_Pages");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Revision).HasMaxLength(50);
            entity.Property(e => e.ScheduleId)
                .HasMaxLength(100)
                .HasColumnName("Schedule_ID");
            entity.Property(e => e.Seq).HasMaxLength(50);
            entity.Property(e => e.Situation).HasMaxLength(100);
            entity.Property(e => e.StatusInFlow)
                .HasMaxLength(100)
                .HasColumnName("Status_In_Flow");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<VwLdpUnificadaUltimaVersao>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Ldp_Unificada_Ultima_Versao");

            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.ClientDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("Client_Document_Number");
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(100)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DateCommentsReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(100)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Format).HasMaxLength(50);
            entity.Property(e => e.HhTimeSheet)
                .HasColumnType("numeric(38, 2)")
                .HasColumnName("HH_TimeSheet");
            entity.Property(e => e.IdRegistro).HasColumnName("ID_Registro");
            entity.Property(e => e.InclusionDate)
                .HasColumnType("datetime")
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.Item).HasMaxLength(50);
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.NoPages).HasColumnName("No_Pages");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Revision).HasMaxLength(50);
            entity.Property(e => e.ScheduleId)
                .HasMaxLength(100)
                .HasColumnName("Schedule_ID");
            entity.Property(e => e.Seq).HasMaxLength(50);
            entity.Property(e => e.Situation).HasMaxLength(100);
            entity.Property(e => e.StatusInFlow)
                .HasMaxLength(100)
                .HasColumnName("Status_In_Flow");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<VwLdpUnificadaUltimaVersaoTeste>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Ldp_Unificada_Ultima_Versao_TESTE");

            entity.Property(e => e.ActualCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_Cost");
            entity.Property(e => e.ActualHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Actual_HH");
            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.ClientDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("Client_Document_Number");
            entity.Property(e => e.CommentStatus)
                .HasMaxLength(100)
                .HasColumnName("Comment_Status");
            entity.Property(e => e.Cr)
                .HasMaxLength(100)
                .HasColumnName("CR");
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DataTimesheetLançamentos).HasColumnName("DATA_TIMESHEET_LANÇAMENTOS");
            entity.Property(e => e.DateCommentsReceived)
                .HasColumnType("datetime")
                .HasColumnName("Date_Comments_Received");
            entity.Property(e => e.Discipline).HasMaxLength(100);
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.DocTypeCode)
                .HasMaxLength(100)
                .HasColumnName("Doc_Type_Code");
            entity.Property(e => e.DocumentStatus)
                .HasMaxLength(100)
                .HasColumnName("Document_Status");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.EmissionDate)
                .HasColumnType("datetime")
                .HasColumnName("Emission_Date");
            entity.Property(e => e.FimDaSemana).HasColumnName("Fim_da_Semana");
            entity.Property(e => e.FimDoMes).HasColumnName("Fim_do_Mes");
            entity.Property(e => e.Format).HasMaxLength(50);
            entity.Property(e => e.HhTimeSheet)
                .HasColumnType("numeric(38, 2)")
                .HasColumnName("HH_TimeSheet");
            entity.Property(e => e.IdRegistro).HasColumnName("ID_Registro");
            entity.Property(e => e.InclusionDate)
                .HasColumnType("datetime")
                .HasColumnName("Inclusion_Date");
            entity.Property(e => e.InicioDaSemana).HasColumnName("Inicio_da_Semana");
            entity.Property(e => e.InicioDoMes).HasColumnName("Inicio_do_Mes");
            entity.Property(e => e.IssuedPurpose)
                .HasMaxLength(100)
                .HasColumnName("Issued_Purpose");
            entity.Property(e => e.Item).HasMaxLength(50);
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.NoPages).HasColumnName("No_Pages");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.PlannedCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_Cost");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.PlannedHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Planned_HH");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.RemainingCost)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_Cost");
            entity.Property(e => e.RemainingHh)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("Remaining_HH");
            entity.Property(e => e.ReplannedDate)
                .HasColumnType("datetime")
                .HasColumnName("Replanned_Date");
            entity.Property(e => e.Revision).HasMaxLength(50);
            entity.Property(e => e.ScheduleId)
                .HasMaxLength(100)
                .HasColumnName("Schedule_ID");
            entity.Property(e => e.Seq).HasMaxLength(50);
            entity.Property(e => e.Situation).HasMaxLength(100);
            entity.Property(e => e.StatusInFlow)
                .HasMaxLength(100)
                .HasColumnName("Status_In_Flow");
            entity.Property(e => e.TransmittalNumber)
                .HasMaxLength(100)
                .HasColumnName("Transmittal_Number");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<VwNAConnectWeeklyControl>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_N_A_Connect_Weekly_Control");

            entity.Property(e => e.Area).HasMaxLength(100);
            entity.Property(e => e.CrOs)
                .HasMaxLength(100)
                .HasColumnName("CR_OS");
            entity.Property(e => e.DataFimSemana).HasColumnName("Data_Fim_Semana");
            entity.Property(e => e.DataInicioSemana).HasColumnName("Data_Inicio_Semana");
            entity.Property(e => e.DataSincronizacao)
                .HasColumnType("datetime")
                .HasColumnName("Data_Sincronizacao");
            entity.Property(e => e.DisciplineActivity)
                .HasMaxLength(100)
                .HasColumnName("Discipline_Activity");
            entity.Property(e => e.DocumentTitle).HasColumnName("Document_Title");
            entity.Property(e => e.IdRegistro)
                .ValueGeneratedOnAdd()
                .HasColumnName("ID_Registro");
            entity.Property(e => e.NaDocumentNumber)
                .HasMaxLength(100)
                .HasColumnName("NA_Document_Number");
            entity.Property(e => e.PercentPhysicalCompleted)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Physical_Completed");
            entity.Property(e => e.PercentPlannedPhysical)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("Percent_Planned_Physical");
            entity.Property(e => e.PeriodoSemanalLabel)
                .HasMaxLength(4000)
                .HasColumnName("Periodo_Semanal_Label");
            entity.Property(e => e.PlannedDateBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Planned_Date_Baseline");
            entity.Property(e => e.ProjectIdAcc)
                .HasMaxLength(100)
                .HasColumnName("Project_ID_ACC");
            entity.Property(e => e.WbsPercent)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("WBS_Percent");
        });

        modelBuilder.Entity<VwResumoHhDefinitiva>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Resumo_HH_Definitiva");

            entity.Property(e => e.DataApontamento)
                .HasColumnType("datetime")
                .HasColumnName("Data_Apontamento");
            entity.Property(e => e.DataPlanejadaBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Data_Planejada_Baseline");
            entity.Property(e => e.DescricoesAgrupadas)
                .IsUnicode(false)
                .HasColumnName("Descricoes_Agrupadas");
            entity.Property(e => e.Disciplina).HasMaxLength(100);
            entity.Property(e => e.Doc1)
                .HasMaxLength(100)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(100)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(100)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(100)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(50)
                .HasColumnName("doc5");
            entity.Property(e => e.DocumentoCodigo).HasMaxLength(100);
            entity.Property(e => e.HhPrevistoProporcionalDia)
                .HasColumnType("numeric(38, 6)")
                .HasColumnName("HH_Previsto_Proporcional_Dia");
            entity.Property(e => e.HhPrevistoTotalEstatico)
                .HasColumnType("decimal(38, 6)")
                .HasColumnName("HH_Previsto_Total_Estatico");
            entity.Property(e => e.HhRealizadoDia)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Realizado_Dia");
            entity.Property(e => e.NomeProjeto)
                .HasMaxLength(200)
                .HasColumnName("Nome_Projeto");
            entity.Property(e => e.Projeto).HasMaxLength(100);
        });

        modelBuilder.Entity<VwResumoHhLdpCompletum>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Resumo_HH_LDP_Completa");

            entity.Property(e => e.DescricoesAgrupadas)
                .IsUnicode(false)
                .HasColumnName("Descricoes_Agrupadas");
            entity.Property(e => e.Disciplina).HasMaxLength(100);
            entity.Property(e => e.Doc1)
                .HasMaxLength(100)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(100)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(100)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(100)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(50)
                .HasColumnName("doc5");
            entity.Property(e => e.DocumentoCodigo).HasMaxLength(100);
            entity.Property(e => e.HhPrevisto)
                .HasColumnType("decimal(18, 2)")
                .HasColumnName("HH_Previsto");
            entity.Property(e => e.HhRealizadoTotal)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Realizado_Total");
            entity.Property(e => e.NomeProjeto)
                .HasMaxLength(200)
                .HasColumnName("Nome_Projeto");
            entity.Property(e => e.Projeto).HasMaxLength(100);
        });

        modelBuilder.Entity<VwResumoHhPorDocumento>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Resumo_HH_Por_Documento");

            entity.Property(e => e.DescricoesAgrupadas)
                .IsUnicode(false)
                .HasColumnName("Descricoes_Agrupadas");
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.HhPrevisto)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Previsto");
            entity.Property(e => e.HhRealizadoTotal)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Realizado_Total");
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwResumoHhRateioTempo>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_Resumo_HH_Rateio_Tempo");

            entity.Property(e => e.DataApontamento)
                .HasColumnType("datetime")
                .HasColumnName("Data_Apontamento");
            entity.Property(e => e.DataPlanejadaBaseline)
                .HasColumnType("datetime")
                .HasColumnName("Data_Planejada_Baseline");
            entity.Property(e => e.Disciplina).HasMaxLength(100);
            entity.Property(e => e.Doc1)
                .HasMaxLength(100)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(100)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(100)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(100)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(50)
                .HasColumnName("doc5");
            entity.Property(e => e.DocumentoCodigo).HasMaxLength(100);
            entity.Property(e => e.HhPrevistoProporcionalDia)
                .HasColumnType("decimal(38, 6)")
                .HasColumnName("HH_Previsto_Proporcional_Dia");
            entity.Property(e => e.HhPrevistoTotalEstatico)
                .HasColumnType("decimal(38, 6)")
                .HasColumnName("HH_Previsto_Total_Estatico");
            entity.Property(e => e.HhRealizadoDia)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Realizado_Dia");
            entity.Property(e => e.NomeProjeto)
                .HasMaxLength(200)
                .HasColumnName("Nome_Projeto");
            entity.Property(e => e.Projeto).HasMaxLength(100);
        });

        modelBuilder.Entity<VwTimeSheetNa>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_TimeSheet_NA");

            entity.Property(e => e.Data)
                .HasColumnType("datetime")
                .HasColumnName("data");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
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

        modelBuilder.Entity<VwTimeSheetNaCorrigidum>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_TimeSheet_NA_Corrigida");

            entity.Property(e => e.Data)
                .HasColumnType("datetime")
                .HasColumnName("data");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhPlanejadoOriginalDuplicado)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Planejado_Original_Duplicado");
            entity.Property(e => e.HhPrevistoCorrigido)
                .HasColumnType("numeric(38, 15)")
                .HasColumnName("HH_Previsto_Corrigido");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
            entity.Property(e => e.Trabalhadas).HasColumnType("numeric(38, 15)");
        });

        modelBuilder.Entity<VwTimeSheetRealizadoPorPessoa>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_TimeSheet_Realizado_Por_Pessoa");

            entity.Property(e => e.ChaveDiariaDoc)
                .HasMaxLength(161)
                .IsUnicode(false)
                .HasColumnName("Chave_Diaria_Doc");
            entity.Property(e => e.DataDate).HasColumnName("Data_Date");
            entity.Property(e => e.Descricao)
                .HasMaxLength(8000)
                .IsUnicode(false);
            entity.Property(e => e.Disciplina)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.Doc1)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("doc1");
            entity.Property(e => e.Doc2)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc2");
            entity.Property(e => e.Doc3)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc3");
            entity.Property(e => e.Doc4)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc4");
            entity.Property(e => e.Doc5)
                .HasMaxLength(24)
                .IsUnicode(false)
                .HasColumnName("doc5");
            entity.Property(e => e.Documento)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.Funcao)
                .HasMaxLength(128)
                .IsUnicode(false);
            entity.Property(e => e.HhPlanejadoTimeSheet)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Planejado_TimeSheet");
            entity.Property(e => e.HhRealizado)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("HH_Realizado");
            entity.Property(e => e.Nome).HasMaxLength(128);
            entity.Property(e => e.Projeto)
                .HasMaxLength(64)
                .IsUnicode(false);
        });

        modelBuilder.Entity<VwTimesheetSemLdp>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("VW_TIMESHEET_SEM_LDP");

            entity.Property(e => e.DocumentoCodigo)
                .HasMaxLength(512)
                .IsUnicode(false);
            entity.Property(e => e.Horas).HasColumnType("numeric(38, 15)");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
