using System.Text.Json.Serialization;

namespace NA_Connect.Models
{
    public class ProjectAdminDTO
    {
        public int ProjetoId { get; set; }
        public string Nome_Projeto { get; set; }
        public string Project_ID_ACC { get; set; }
        public bool Is_Ativo { get; set; }
        public int? NormaID { get; set; }
        public int? PacoteOCRID { get; set; }
        public int? ConfigLDPID { get; set; }
        public string? LdpFolderId { get; set; }
        public string? CronFolderId { get; set; }
    }
}