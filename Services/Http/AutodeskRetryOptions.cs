namespace APS.WebApp.Services.Http
{
    /// <summary>
    /// Configuração do mecanismo de retry/backoff exponencial aplicado às chamadas HTTP
    /// contra a API da Autodesk (APS). Vinculado à seção "AutodeskApi:Retry" do appsettings.json.
    /// </summary>
    public class AutodeskRetryOptions
    {
        public const string SectionName = "APS:Retry";

        /// <summary>
        /// Número máximo de tentativas adicionais após a chamada original falhar.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 5;

        /// <summary>
        /// Atraso base (em segundos) usado no backoff exponencial quando a resposta
        /// NÃO trouxer o cabeçalho "Retry-After" (ex.: erros 5xx ou falhas de rede).
        /// </summary>
        public double BaseDelaySeconds { get; set; } = 1.0;

        /// <summary>
        /// Teto (em segundos) para o atraso calculado pelo backoff exponencial.
        /// Não se aplica ao valor vindo do cabeçalho "Retry-After", que é sempre respeitado
        /// integralmente, conforme recomendado pela documentação da Autodesk.
        /// </summary>
        public double MaxDelaySeconds { get; set; } = 64.0;
    }
}
