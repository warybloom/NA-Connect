using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AccIntegration.WebApp.Models
{
    [Table("Metadados_Projetos")]
    public class MetadadosProjeto
    {
        [Key]
        [Column("MetadadoID")]
        public int Id { get; set; }

        // 🎯 CORREÇÃO CRÍTICA AQUI: Adicionado o '?' para tornar a string anulável (string?).
        // Como o banco de dados pode ter essa coluna vazia ou nula, a falta do '?' fazia o 
        // Entity Framework Core quebrar com o erro "Data is Null" ao tentar mapear o registro.
        public string? CR_OS { get; set; }
        
        public string? Project_ID_ACC { get; set; } 
        public string? Account_ID_ACC { get; set; }
        public string? Descricao_Corporativa { get; set; }
        public string? Nome_LDP { get; set; }
        public string? HorariosSync { get; set; }
        public DateTime? Data_Ultima_Atualizacao { get; set; }

        [Column("NormaID")]
        public int? NormaID { get; set; }

        [Column("PacoteOCRID")]
        public int? PacoteOCRID { get; set; }
        
        public string? Idioma { get; set; }
        public string? Numero_NA { get; set; }

        // 🎯 ADICIONADO PARA COMPLETUDE: Incluído o mapeamento explícito da coluna de cache da LDP.
        // Isso garante que se o Entity Framework precisar ler ou atualizar o LDP_File_ID diretamente,
        // ele já possui a propriedade mapeada e totalmente protegida contra nulos.
        [Column("LDP_File_ID")]
        public string? LdpFileId { get; set; }
    }
}