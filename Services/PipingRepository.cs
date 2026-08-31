#nullable disable
using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient; 
using ForgeSync.Modules.Piping.Models;
using APS.WebApp.Models;// Necessário para o Plant3D_Ativos_Model

namespace ForgeSync.Modules.Piping.Repositories
{
    public class PipingRepository
    {
        private readonly string _connectionString;

        public PipingRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Busca a configuração de mapeamento JSON salva para o projeto específico.
        /// </summary>
        public async Task<string> GetProjectMappingConfigAsync(int projectId)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                const string query = "SELECT Mapeamento_JSON FROM Plant3D_Configuracao WHERE ProjetoId = @projectId";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@projectId", projectId);
                    var result = await command.ExecuteScalarAsync();
                    return result?.ToString() ?? "{}";
                }
            }
        }

        /// <summary>
        /// Cria um registro na tabela de auditoria e retorna o ID gerado.
        /// </summary>
        public async Task<int> CreateAuditoriaHeaderAsync(int projectId)
        {
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                const string query = "INSERT INTO Plant3D_Auditoria (ProjetoID, DataExecucao) OUTPUT INSERTED.AuditoriaID VALUES (@pid, GETDATE())";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@pid", projectId);
                    return (int)await command.ExecuteScalarAsync();
                }
            }
        }

        public void BulkInsertPipingItems<T>(List<T> items, string destinationTableName)
        {
            if (items == null || items.Count == 0)
                return;

            DataTable dataTable = ConvertListToDataTable(items);

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction))
                        {
                            bulkCopy.DestinationTableName = destinationTableName;
                            bulkCopy.BatchSize = 5000;
                            bulkCopy.BulkCopyTimeout = 120;

                            foreach (DataColumn column in dataTable.Columns)
                            {
                                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                            }

                            bulkCopy.WriteToServer(dataTable);
                        }
                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"Erro crítico durante o Bulk Insert na tabela {destinationTableName}: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        private DataTable ConvertListToDataTable<T>(List<T> items)
        {
            DataTable dataTable = new DataTable(typeof(T).Name);
            PropertyInfo[] props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo prop in props)
            {
                Type type = (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>)) 
                            ? Nullable.GetUnderlyingType(prop.PropertyType) 
                            : prop.PropertyType;

                dataTable.Columns.Add(prop.Name, type);
            }

            foreach (T item in items)
            {
                var values = new object[props.Length];
                for (int i = 0; i < props.Length; i++)
                {
                    values[i] = props[i].GetValue(item, null) ?? DBNull.Value;
                }
                dataTable.Rows.Add(values);
            }

            return dataTable;
        }
    }
}