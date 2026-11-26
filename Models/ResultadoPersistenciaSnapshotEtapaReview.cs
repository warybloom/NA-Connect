namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado da persistência transacional do snapshot de ACC_Review_Current_Stage (seção 11 da
    /// funcionalidade "ACC_REVIEW_STAGE"). Em caso de rollback, <see cref="RegistrosInseridos"/> reflete
    /// o que HAVIA SIDO preparado antes da falha — nada fica persistido quando <see cref="Sucesso"/> é
    /// false (o snapshot anterior do projeto é preservado intacto).
    /// </summary>
    public sealed class ResultadoPersistenciaSnapshotEtapaReview
    {
        public bool Sucesso { get; set; }
        public bool RollbackExecutado { get; set; }

        public int RegistrosAnteriores { get; set; }
        public int RegistrosInseridos { get; set; }

        public string Erro { get; set; }
    }
}
