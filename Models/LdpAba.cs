#nullable disable
using System.Collections.Generic;
using System.Linq;
 
namespace APS.WebApp.Models
{
    public class LdpAba
    {
        public string Nome {get; set;}

        public List<List<String>> Linhas {get; set;} = new();

        public int TotalLinhas => Linhas.Count;

        public int TotalColunas => Linhas.Count == 0 ? 0 : Linhas.Max(l => l.Count);

        public string Celula (int row, int col)
        {
             if (row < 0 || row >= Linhas.Count) return string.Empty;
            var linha = Linhas[row];
            if (col < 0 || col >= linha.Count) return string.Empty;
            return linha[col] ?? string.Empty;
        }

    }
}