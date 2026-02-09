namespace APS.WebApp.Models
{
    public class TokenResponse
    {
        // Adicionamos "= string.Empty;" para inicializar as propriedades e remover os avisos.
        public string access_token { get; set; } = string.Empty;
        public string refresh_token { get; set; } = string.Empty;
        public string token_type { get; set; } = string.Empty;
        public int expires_in { get; set; }
    }
}