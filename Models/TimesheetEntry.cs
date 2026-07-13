using System;
using System.ComponentModel.DataAnnotations;

namespace AccIntegration.WebApp.Data
{
    // Módulo: Data/TimesheetEntry.cs
    public class TimesheetEntry
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string ProjectId { get; set; }
        
        public string UserName { get; set; }
        
        public decimal Hours { get; set; }
        
        public DateTime Date { get; set; }
        
        // Adicione outras propriedades caso existam no seu Excel (ex: Role, TaskName, etc.)
    }
}