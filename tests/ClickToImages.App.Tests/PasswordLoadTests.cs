using ClickToImages.App.ViewModels;
using ClickToImages.Core.Tests;

namespace ClickToImages.App.Tests;

// "locked.pdf" needs the password "secret"; "plain.pdf" needs none. The fake prompt records every
// file it was asked about, so a second prompt for the same PDF shows up as a second entry.
public sealed class PasswordLoadTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ClickToImagesPasswordLoadTests").FullName;
    private readonly string _locked;
    private readonly string _plain;
    private readonly List<string> _asked = [];

    public PasswordLoadTests()
    {
        _locked = Path.Combine(_folder, "locked.pdf");
        _plain = Path.Combine(_folder, "plain.pdf");
        TestPdf.WriteEncrypted(_locked, "secret", "owner", (200, 300, 0), (200, 300, 0));
        TestPdf.Write(_plain, (200, 300, 0));
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task RightPassword_OpensTheDocumentAfterOnePrompt()
    {
        var viewModel = new MainViewModel(Answer("secret"));

        await viewModel.LoadAsync([_locked]);

        Assert.Equal(["locked.pdf"], _asked);
        Assert.Equal(2, viewModel.Documents.Single().PageCount);
        Assert.Empty(viewModel.SkippedDocuments);
    }

    [Fact]
    public async Task WrongPassword_SkipsThatDocumentOnly_WithoutASecondPrompt()
    {
        var viewModel = new MainViewModel(Answer("guess"));

        await viewModel.LoadAsync([_locked, _plain]);

        Assert.Equal(["locked.pdf"], _asked);
        Assert.Equal([new SkippedDocument("locked.pdf", SkipReason.WrongPassword)], viewModel.SkippedDocuments);
        Assert.Equal(["plain.pdf"], viewModel.Documents.Select(document => document.FileName));
        Assert.Equal([0], viewModel.Pages.Select(page => page.Index));
        Assert.False(viewModel.Documents.Single().HasFailed);
    }

    [Fact]
    public async Task DeclinedPrompt_SkipsAsNoPasswordGiven()
    {
        var viewModel = new MainViewModel(Answer(null));

        await viewModel.LoadAsync([_locked]);

        Assert.Equal([new SkippedDocument("locked.pdf", SkipReason.NoPasswordGiven)], viewModel.SkippedDocuments);
        Assert.Empty(viewModel.Documents);
        Assert.True(viewModel.ShowEmptyState);
    }

    [Fact]
    public async Task NoOneToAsk_SkipsAsNoPasswordGiven()
    {
        var viewModel = new MainViewModel();

        await viewModel.LoadAsync([_locked]);

        Assert.Equal([new SkippedDocument("locked.pdf", SkipReason.NoPasswordGiven)], viewModel.SkippedDocuments);
    }

    [Fact]
    public async Task PermissionsOnlyPdf_OpensWithoutAPrompt()
    {
        string permissionsOnly = Path.Combine(_folder, "permissions-only.pdf");
        TestPdf.WriteEncrypted(permissionsOnly, "", "owner", (200, 300, 0));
        var viewModel = new MainViewModel(Answer("unused"));

        await viewModel.LoadAsync([permissionsOnly]);

        Assert.Empty(_asked);
        Assert.Single(viewModel.Documents);
    }

    private Func<string, Task<string?>> Answer(string? password) => fileName =>
    {
        _asked.Add(fileName);
        return Task.FromResult(password);
    };
}
