using net.sf.mpxj;
using MpxjTask = net.sf.mpxj.Task;

namespace NA_Connect.Cronograma.Tests.TestHelpers
{
    /// <summary>
    /// Construtor mínimo de um ProjectFile MPXJ em memória, só para os testes de resolução de campo
    /// REALIZADO/PREVISTO — nunca lê nem escreve nenhum .mpp em disco.
    /// </summary>
    internal static class ProjectFileTestHelper
    {
        public static ProjectFile NovoProjeto() => new ProjectFile();

        public static MpxjTask AdicionarTarefa(ProjectFile project, string docNumber, string taskName = "Tarefa de teste")
        {
            var task = project.addTask();
            task.setName(taskName);
            task.set(TaskField.TEXT1, docNumber);
            return task;
        }

        public static void DefinirAlias(ProjectFile project, FieldType campo, string alias)
        {
            project.getCustomFields().getOrCreate(campo).setAlias(alias);
        }

        public static void DefinirTituloColuna(ProjectFile project, string nomeTabela, FieldType campo, string titulo)
        {
            var tabela = new Table();
            tabela.setName(nomeTabela);
            tabela.setResourceFlag(false);

            var coluna = new Column(project);
            coluna.setFieldType(campo);
            coluna.setTitle(titulo);

            tabela.getColumns().add(coluna);
            project.getTables().add(tabela);
        }
    }
}
