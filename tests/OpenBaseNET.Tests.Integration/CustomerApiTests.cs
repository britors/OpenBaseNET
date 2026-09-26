using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenBaseNET.Application.Customers;

namespace OpenBaseNET.Tests.Integration;

public sealed class CustomerApiTests : TestDatabase
{
    [Fact]
    public async Task HTTP_CRUD_returns_expected_statuses_and_location()
    {
        await using var factory = new ApiFactory(ConnectionString);
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/customers", new { name = "  Ana  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var customer = await created.Content.ReadFromJsonAsync<CustomerResponse>();
        Assert.NotNull(customer);
        Assert.Equal("Ana", customer.Name);
        Assert.NotNull(created.Headers.Location);
        Assert.EndsWith($"/api/customers/{customer.Id}", created.Headers.Location.ToString());
        Assert.Equal(customer, await client.GetFromJsonAsync<CustomerResponse>(created.Headers.Location));
        var listed = await client.GetFromJsonAsync<CustomerResponse[]>("/api/customers?pageNumber=1&pageSize=1");
        Assert.Single(listed!);

        var updated = await client.PutAsJsonAsync(created.Headers.Location, new { name = "Bea" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Bea", (await updated.Content.ReadFromJsonAsync<CustomerResponse>())!.Name);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(created.Headers.Location)).StatusCode);
        await AssertProblem(await client.GetAsync(created.Headers.Location), 404, "CUSTOMER_NOT_FOUND");
        await AssertProblem(await client.DeleteAsync(created.Headers.Location), 404, "CUSTOMER_NOT_FOUND");
        await AssertProblem(await client.PutAsJsonAsync(created.Headers.Location, new { name = "Bea" }), 404, "CUSTOMER_NOT_FOUND");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Invalid_name_returns_422_without_accessing_missing_table(string? name)
    {
        await DropCustomerTable();
        await using var factory = new ApiFactory(ConnectionString);
        using var client = factory.CreateClient();
        await AssertProblem(await client.PostAsJsonAsync("/api/customers", new { name }), 422, "CUSTOMER_NAME_REQUIRED");
        await AssertProblem(await client.PutAsJsonAsync($"/api/customers/{Guid.NewGuid()}", new { name }), 422, "CUSTOMER_NAME_REQUIRED");
    }

    [Fact]
    public async Task Invalid_pagination_and_identity_return_422_before_database_access()
    {
        await DropCustomerTable();
        await using var factory = new ApiFactory(ConnectionString);
        using var client = factory.CreateClient();
        await AssertProblem(await client.GetAsync("/api/customers?pageSize=101"), 422, "PAGE_SIZE_OUT_OF_RANGE");
        await AssertProblem(await client.GetAsync("/api/customers?pageNumber=0"), 422, "PAGE_NUMBER_OUT_OF_RANGE");
        await AssertProblem(await client.GetAsync($"/api/customers/{Guid.Empty}"), 422, "CUSTOMER_ID_REQUIRED");
        await AssertProblem(await client.DeleteAsync($"/api/customers/{Guid.Empty}"), 422, "CUSTOMER_ID_REQUIRED");
    }

    [Fact]
    public async Task Malformed_JSON_returns_400()
    {
        await using var factory = new ApiFactory(ConnectionString);
        using var client = factory.CreateClient();
        using var body = new StringContent("{", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/customers", body)).StatusCode);
    }

    [Fact]
    public async Task Unexpected_database_error_is_sanitized()
    {
        await DropCustomerTable();
        await using var factory = new ApiFactory(ConnectionString);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/customers");
        await AssertProblem(response, 500, "INTERNAL_ERROR");
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("SqlClient", body);
        Assert.DoesNotContain("customers", body);
        Assert.DoesNotContain("SELECT", body);
        Assert.DoesNotContain(ConnectionString, body);
    }

    private static async Task AssertProblem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
        }
    }
}
