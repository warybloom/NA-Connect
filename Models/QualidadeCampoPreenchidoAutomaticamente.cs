namespace APS.WebApp.Models
{
    /// <summary>
    /// Rastreabilidade de UM campo textual obrigatório que estava vazio numa linha real e recebeu o
    /// preenchimento automático "FALTA INFORMAÇÃO" (seção 8 do pedido). Nunca vira coluna nova no banco —
    /// a rastreabilidade vive só no log ([QUALIDADE][CAMPO_PREENCHIDO_AUTOMATICAMENTE]) e neste objeto,
    /// devolvido no resultado da tabela para quem quiser auditar por código.
    /// </summary>
    public sealed class QualidadeCampoPreenchidoAutomaticamente
    {
        public string Aba { get; set; }
        public int NumeroLinha { get; set; }
        public string CampoLogico { get; set; }
        public string ColunaSql { get; set; }
        public string ValorAplicado { get; set; }
    }
}
