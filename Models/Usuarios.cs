
#nullable disable
namespace APS.WebApp.Models {
public class Usuario
{
    public int UserID { get; set; }
    public string Nome { get; set; }
    public string Email { get; set; }
    public string RoleSistema { get; set; } = "Viewer"; // role padrão
    public bool Ativo { get; set; } = true;
    public DateTime DataCadastro { get; set; } = DateTime.UtcNow;
}
}