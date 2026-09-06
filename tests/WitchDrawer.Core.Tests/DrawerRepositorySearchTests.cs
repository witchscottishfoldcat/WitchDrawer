using WitchDrawer.Core;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Core.Storage;

namespace WitchDrawer.Core.Tests;

public sealed class DrawerRepositorySearchTests
{
    [Fact]
    public async Task SearchItemsAsync_TreatsPercentAsLiteralCharacter()
    {
        using var fixture = await SearchFixture.CreateAsync();
        await fixture.ImportToMappingBoxAsync("100%.txt");
        await fixture.ImportToMappingBoxAsync("plain.txt");

        var results = await fixture.Service.SearchItemsAsync("%");

        var match = Assert.Single(results);
        Assert.Equal("100%.txt", match.DisplayName);
    }

    [Fact]
    public async Task SearchItemsAsync_TreatsUnderscoreAsLiteralCharacter()
    {
        using var fixture = await SearchFixture.CreateAsync();
        await fixture.ImportToMappingBoxAsync("a_b.txt");
        await fixture.ImportToMappingBoxAsync("axb.txt");

        var results = await fixture.Service.SearchItemsAsync("a_b");

        var match = Assert.Single(results);
        Assert.Equal("a_b.txt", match.DisplayName);
    }

    [Fact]
    public async Task SearchItemsAsync_TreatsBackslashAsLiteralCharacter()
    {
        using var fixture = await SearchFixture.CreateAsync();
        await fixture.ImportToMappingBoxAsync("100%.txt");
        await fixture.ImportToMappingBoxAsync("plain.txt");

        var results = await fixture.Service.SearchItemsAsync(@"100\%");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchItemsAsync_StillMatchesPlainSubstrings()
    {
        using var fixture = await SearchFixture.CreateAsync();
        await fixture.ImportToMappingBoxAsync("季度报告.docx");
        await fixture.ImportToMappingBoxAsync("plain.txt");

        var results = await fixture.Service.SearchItemsAsync("报告");

        var match = Assert.Single(results);
        Assert.Equal("季度报告.docx", match.DisplayName);
    }

    [Fact]
    public async Task SearchItemsAsync_EmptyQueryReturnsAllItems()
    {
        using var fixture = await SearchFixture.CreateAsync();
        await fixture.ImportToMappingBoxAsync("100%.txt");
        await fixture.ImportToMappingBoxAsync("plain.txt");

        var results = await fixture.Service.SearchItemsAsync("");

        Assert.Equal(2, results.Count);
    }

    [Theory]
    [InlineData(@"100%.txt", @"100\%.txt")]
    [InlineData(@"a\b_c.txt", @"a\\b\_c.txt")]
    public void EscapeLikePattern_EscapesAllWildcardCharacters(string input, string expected)
    {
        Assert.Equal(expected, DrawerRepository.EscapeLikePattern(input));
    }

    private sealed class SearchFixture : IDisposable
    {
        private SearchFixture(string root, DrawerService service, Guid mappingBoxId)
        {
            Root = root;
            Service = service;
            MappingBoxId = mappingBoxId;
        }

        public string Root { get; }

        public DrawerService Service { get; }

        private Guid MappingBoxId { get; }

        public static async Task<SearchFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "WitchDrawer.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root);
            var repository = new DrawerRepository(paths.DatabasePath);
            var service = new DrawerService(paths, repository);
            await service.InitializeAsync();

            var mappingBox = (await service.GetBoxesAsync()).Single(box => box.Type == BoxType.Mapping);
            return new SearchFixture(root, service, mappingBox.Id);
        }

        public async Task ImportToMappingBoxAsync(string fileName)
        {
            var directory = Path.Combine(Root, "sources", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, "content");

            var item = await Service.ImportPathAsync(MappingBoxId, path);
            Assert.Equal(fileName, item.DisplayName);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch (IOException)
            {
                // SQLite 句柄释放有延迟时保留临时目录，不影响测试结果。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上。
            }
        }
    }
}
