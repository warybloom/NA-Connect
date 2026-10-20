using System.Net;
using APS.WebApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

public class WeeklyScheduleListingTests
{
    [Fact]
    public async Task SharedPlanningListingPreservesXlsDiagnosticAndIgnoresOfficeLockFiles()
    {
        using var http = new HttpClient(new Pages(
            Page("""[{"id":"1","type":"items","attributes":{"displayName":"~$cap.xlsx"}},{"id":"2","type":"items","attributes":{"displayName":"legacy.xls"}}]""", "\"/page2\"", 3),
            Page("""[{"id":"3","type":"items","attributes":{"displayName":"hold.xlsm"}}]""", total: 3)));
        var result = await Api(http).ListarArquivosPlanejamentoAsync("project", "folder", "test", includeLegacyXls: true);
        Assert.Equal(new[] { "legacy.xls", "hold.xlsm" }, result.Select(c => c.FileName));
        Assert.True(result[0].EhXls);
        Assert.True(result[1].EhXlsxOuXlsm);
    }
    private sealed class Pages(params HttpResponseMessage[] pages) : HttpMessageHandler
    {
        private int _index;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(pages[_index++]);
        }
    }

    private static HttpResponseMessage Page(string data, string next = "null", int total = 2) => new(HttpStatusCode.OK)
    { Content = new StringContent("{\"data\":" + data + ",\"links\":{\"next\":" + next + "},\"meta\":{\"totalResults\":" + total + "}}") };

    private static AutodeskApiService Api(HttpClient http) => new(http, null!, new ConfigurationBuilder().Build(),
        NullLogger<AutodeskApiService>.Instance, null!);

    [Fact]
    public async Task WeeklyListingIncludesExcelOnSecondPage()
    {
        using var http = new HttpClient(new Pages(
            Page("""[{"id":"1","type":"items","attributes":{"displayName":"weekly.xlsx"}}]""", """{"href":"/page2"}"""),
            Page("""[{"id":"2","type":"items","attributes":{"displayName":"other.xlsm"}}]""")));
        var result = await Api(http).ListarArquivosWeeklyAsync("project", "folder", "test");
        Assert.Equal(new[] { "weekly.xlsx", "other.xlsm" }, result.Select(f => f.FileName));
    }

    [Fact]
    public async Task FailedSecondPageCannotReturnPartialCandidates()
    {
        using var http = new HttpClient(new Pages(
            Page("""[{"id":"1","type":"items","attributes":{"displayName":"weekly.xlsx"}}]""", "\"/page2\""),
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => Api(http).ListarArquivosWeeklyAsync("project", "folder", "test"));
    }

    [Fact]
    public async Task MissingNextLinkWithIncompleteTotalBlocks()
    {
        using var http = new HttpClient(new Pages(Page("[]")));
        await Assert.ThrowsAsync<InvalidDataException>(() => Api(http).ListarArquivosWeeklyAsync("project", "folder", "test"));
    }
}
