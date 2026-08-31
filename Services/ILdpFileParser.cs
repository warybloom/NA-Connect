using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services
{
    /// <summary>
    /// Contrato para converter um arquivo (de qualquer formato suportado) em uma
    /// lista de "abas" com grade de texto, para que a validação de LDP possa
    /// ser feita de forma idêntica independente do formato original.
    /// </summary>
    public interface ILdpFileParser
    {
        /// <summary>
        /// Extensões de arquivo (com ponto, minúsculas) que este parser sabe tratar.
        /// Ex.: ".xlsx", ".xls"
        /// </summary>
        IReadOnlyCollection<string> ExtensoesSuportadas { get; }

        Task<List<LdpAba>> ExtrairAbasAsync(Stream stream);
    }
}