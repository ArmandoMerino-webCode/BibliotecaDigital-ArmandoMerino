using System.Net;
using System.Net.Http.Json;
using Biblioteca.Api.Contracts;
using Xunit;

namespace Biblioteca.E2ETests;

// These tests call a separately running API over real HTTP (Kestrel).
// Set E2E_BASE_URL to override the default address.
public class LibraryWorkflowE2ETests
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri(
            Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5080")
    };

    [Fact]
    public async Task CompleteBookLifecycle_CreateReadAndDelete()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var request = new CreateBookRequest
        {
            Title = $"Libro E2E {suffix}",
            Author = "Autor de pruebas",
            Isbn = $"978{Random.Shared.Next(1000000000, 1999999999)}",
            PublishedYear = 2022,
            AvailableCopies = 1
        };

        var create = await _client.PostAsJsonAsync("/api/books", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<BookDto>();
        Assert.NotNull(created);

        var get = await _client.GetAsync($"/api/books/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var delete = await _client.DeleteAsync($"/api/books/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var getAfterDelete = await _client.GetAsync($"/api/books/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task LoanLifecycle_CreateAndReturnLoan()
    {
        var request = new CreateLoanRequest
        {
            BookId = 2,
            Borrower = $"E2E {Guid.NewGuid():N}"[..12],
            LoanDate = DateOnly.FromDateTime(DateTime.Today),
            DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14))
        };

        var create = await _client.PostAsJsonAsync("/api/loans", request);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var loan = await create.Content.ReadFromJsonAsync<LoanDto>();
        Assert.NotNull(loan);

        var returned = await _client.PutAsync($"/api/loans/{loan.Id}/return", null);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
    }

    private sealed record BookDto(int Id, string Title, string Author, string Isbn, int PublishedYear, int AvailableCopies);
    private sealed record LoanDto(int Id, int BookId, string Borrower, DateOnly LoanDate, DateOnly DueDate, DateOnly? ReturnDate, string Status);

    [Fact]
    public async Task LoanLifecycle_PreventsDeletingBookUntilLoanIsReturned()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var bookRequest = new CreateBookRequest
        {
            Title = $"Libro préstamo E2E {suffix}",
            Author = "Autor de pruebas",
            Isbn = $"978{Random.Shared.Next(1000000000, 1999999999)}",
            PublishedYear = 2022,
            AvailableCopies = 1
        };

        var createBook = await _client.PostAsJsonAsync("/api/books", bookRequest);
        Assert.Equal(HttpStatusCode.Created, createBook.StatusCode);

        var book = await createBook.Content.ReadFromJsonAsync<BookDto>();
        Assert.NotNull(book);

        var loanRequest = new CreateLoanRequest
        {
            BookId = book.Id,
            Borrower = $"E2E {suffix}",
            LoanDate = DateOnly.FromDateTime(DateTime.Today),
            DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14))
        };

        var createLoan = await _client.PostAsJsonAsync("/api/loans", loanRequest);
        Assert.Equal(HttpStatusCode.Created, createLoan.StatusCode);

        var deleteWhileActive = await _client.DeleteAsync($"/api/books/{book.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteWhileActive.StatusCode);

        var loan = await createLoan.Content.ReadFromJsonAsync<LoanDto>();
        Assert.NotNull(loan);

        var returnLoan = await _client.PutAsync($"/api/loans/{loan.Id}/return", null);
        Assert.Equal(HttpStatusCode.OK, returnLoan.StatusCode);

        var deleteAfterReturn = await _client.DeleteAsync($"/api/books/{book.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteAfterReturn.StatusCode);

        var getAfterDelete = await _client.GetAsync($"/api/books/{book.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }


    [Fact]
    public async Task LoanLifecycle_UpdatesAvailableCopiesAfterLoanAndReturn()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var bookRequest = new CreateBookRequest
        {
            Title = $"Libro copias E2E {suffix}",
            Author = "Autor de pruebas",
            Isbn = $"978{Random.Shared.Next(1000000000, 1999999999)}",
            PublishedYear = 2022,
            AvailableCopies = 2
        };

        var createBook = await _client.PostAsJsonAsync("/api/books", bookRequest);
        Assert.Equal(HttpStatusCode.Created, createBook.StatusCode);

        var book = await createBook.Content.ReadFromJsonAsync<BookDto>();
        Assert.NotNull(book);
        Assert.Equal(2, book.AvailableCopies);

        var loanRequest = new CreateLoanRequest
        {
            BookId = book.Id,
            Borrower = $"E2E {suffix}",
            LoanDate = DateOnly.FromDateTime(DateTime.Today),
            DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14))
        };

        var createLoan = await _client.PostAsJsonAsync("/api/loans", loanRequest);
        Assert.Equal(HttpStatusCode.Created, createLoan.StatusCode);

        var bookAfterLoanResponse = await _client.GetAsync($"/api/books/{book.Id}");
        var bookAfterLoan = await bookAfterLoanResponse.Content.ReadFromJsonAsync<BookDto>();

        Assert.Equal(HttpStatusCode.OK, bookAfterLoanResponse.StatusCode);
        Assert.NotNull(bookAfterLoan);
        Assert.Equal(1, bookAfterLoan.AvailableCopies);

        var loan = await createLoan.Content.ReadFromJsonAsync<LoanDto>();
        Assert.NotNull(loan);

        var returnLoan = await _client.PutAsync($"/api/loans/{loan.Id}/return", null);
        Assert.Equal(HttpStatusCode.OK, returnLoan.StatusCode);

        var bookAfterReturnResponse = await _client.GetAsync($"/api/books/{book.Id}");
        var bookAfterReturn = await bookAfterReturnResponse.Content.ReadFromJsonAsync<BookDto>();

        Assert.Equal(HttpStatusCode.OK, bookAfterReturnResponse.StatusCode);
        Assert.NotNull(bookAfterReturn);
        Assert.Equal(2, bookAfterReturn.AvailableCopies);
    }

}
