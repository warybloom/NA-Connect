-- Explicit user confirmation in PROMPT 08. This seed is exclusively ETS001 / BML011.016.
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
IF DB_NAME()<>N'DB_NA_CONNECT' THROW 51000,'Wrong database.',1;
BEGIN TRAN;
DECLARE @project nvarchar(255)=N'b.9907135f-ff67-43bb-887b-20c3a7f75807', @package int=2, @id bigint;
DECLARE @author nvarchar(255)=N'christian.santana@naconsult.com.br (confirmacao explicita PROMPT 08)';
EXEC sys.sp_set_session_context @key=N'MeasuredHistoricalAuthor',@value=@author;
IF (SELECT COUNT(*) FROM dbo.Projetos WHERE Project_ID_ACC=@project AND Is_Ativo=1 AND CR_OS LIKE '%BML011.016%')<>1
    THROW 51002,'Project identity inconsistent.',1;
IF NOT EXISTS(SELECT 1 FROM costs.Package WITH(UPDLOCK,HOLDLOCK)
    WHERE Id=@package AND Project_ID_ACC=@project AND NomeReferencia=N'ETS001' AND AliasTimesheet=N'Everton Eustaquio'
      AND ModeloCadastro='DISCIPLINA' AND Ativo=1 AND HHBase=710 AND CustoBase=71000)
    THROW 51003,'ETS001 identity or contract inconsistent.',1;
IF EXISTS(SELECT 1 FROM costs.PackageAddendum WHERE PackageId=@package AND Ativo=1)
    THROW 51004,'Unexpected addendum: revalidate contract.',1;
DECLARE @expected TABLE(Discipline nvarchar(200) PRIMARY KEY,Hours decimal(18,6),Cost decimal(18,6));
INSERT @expected VALUES(N'CIVIL',190,19000),(N'PIPING',246,24600),(N'PROCESS',144,14400),(N'STEEL STRUCTURE',130,13000);
IF (SELECT COUNT(*) FROM costs.PackageDiscipline WHERE PackageId=@package AND Ativo=1)<>4
 OR EXISTS(SELECT 1 FROM @expected e WHERE NOT EXISTS(
    SELECT 1 FROM costs.PackageDiscipline d WHERE d.PackageId=@package AND d.Ativo=1 AND d.Disciplina=e.Discipline AND d.HHPrevisto=e.Hours AND d.CustoPrevisto=e.Cost))
    THROW 51005,'Discipline contracts inconsistent.',1;
-- Re-read the current evidence before confirming; never import hours from BML011.013.
SELECT NomeProfissional,COUNT(*) Entries,SUM(HorasTrabalhadas) CurrentHours,SUM(CustoCalculado) CurrentCost
FROM gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial
WHERE Project_ID_ACC=@project AND NomeProfissional=N'Everton Eustaquio' GROUP BY NomeProfissional;
SELECT @id=Id FROM costs.PackageMeasuredHistorical WITH(UPDLOCK,HOLDLOCK) WHERE PackageId=@package AND Status='CONFIRMADA';
IF @id IS NULL
BEGIN
    IF EXISTS(SELECT 1 FROM costs.PackageMeasuredHistorical WHERE PackageId=@package)
        THROW 51006,'Previous confirmation revoked: explicit new confirmation required.',1;
    INSERT costs.PackageMeasuredHistorical(Project_ID_ACC,PackageId,ProfessionalName,ConfirmedHours,ConfirmedCost,ConfirmedAtUtc,ConfirmedBy,Status,Origin)
    VALUES(@project,@package,N'Everton Eustaquio',710,71000,SYSUTCDATETIME(),@author,'CONFIRMADA',
        N'PROMPT 08 de 13/10/2026: usuario confirmou 710 HH ja medidas/executadas; Timesheet incompleto. Medicao por disciplina conforme contrato. Complemento derivado do snapshot corrigido atual, nunca congelado.');
    SET @id=SCOPE_IDENTITY();
    INSERT costs.PackageMeasuredHistoricalDiscipline(ConfirmationId,Discipline,ConfirmedHours,ConfirmedCost)
    SELECT @id,Discipline,Hours,Cost FROM @expected;
END
ELSE
BEGIN
    IF NOT EXISTS(SELECT 1 FROM costs.PackageMeasuredHistorical WHERE Id=@id AND Project_ID_ACC=@project
                  AND ProfessionalName=N'Everton Eustaquio' AND ConfirmedHours=710 AND ConfirmedCost=71000)
      OR (SELECT COUNT(*) FROM costs.PackageMeasuredHistoricalDiscipline WHERE ConfirmationId=@id)<>4
      OR EXISTS(SELECT 1 FROM @expected e WHERE NOT EXISTS(SELECT 1 FROM costs.PackageMeasuredHistoricalDiscipline d
          WHERE d.ConfirmationId=@id AND d.Discipline=e.Discipline AND d.ConfirmedHours=e.Hours AND d.ConfirmedCost=e.Cost))
        THROW 51007,'Existing measurement differs: no automatic overwrite.',1;
END;
COMMIT;
SELECT @id ConfirmationId,N'CONFIRMADA' Status;
