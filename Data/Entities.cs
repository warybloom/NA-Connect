// Local: AccIntegration.WebApp.Data/Entities.cs
#nullable disable
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AccIntegration.WebApp.Data
{
    [Table("Metadados_Projetos", Schema = "dbo")] // ✅ Adicionado Schema para evitar 'Invalid Object Name'
    public class TbProjeto
    {
        [Key] [Column("MetadadoID")] public int ProjetoId { get; set; }
        [Column("Project_ID_ACC")] public string ProjectIdAcc { get; set; }
        [Column("NormaID")] public int? NormaId { get; set; }
        [Column("Idioma")] public string ProjectLanguage { get; set; }
        [Column("LDP_File_ID")] public string LdpFileId { get; set; } 
    }

    [Table("Ldp_Processada", Schema = "dbo")] // ✅ Nova Entidade Mestre
    public class TbLdpProcessada
    {
        [Key] [Column("LdpID")] public int LdpId { get; set; }
        [Column("ProjetoID")] public int ProjetoId { get; set; } // FK com Metadados_Projetos
        public string Document_Code { get; set; }
        public string Discipline { get; set; }
        public string Document_Title { get; set; }
    }

    [Table("Normas_Mappings", Schema = "dbo")]
    public class TbNormasMapping
    {
        [Key] [Column("MappingID")] public int MappingId { get; set; }
        [Column("NormaID")] public int NormaId { get; set; }
        public string Sigla { get; set; }
        [Column("Pasta_Destino")] public string PastaDestino { get; set; }
        [Column("Tipo_Documento")] public string TipoDocumento { get; set; }
        public int Ignorar { get; set; }
    }
}