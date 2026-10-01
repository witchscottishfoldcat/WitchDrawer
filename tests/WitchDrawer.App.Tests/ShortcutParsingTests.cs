using WitchDrawer.Native.Files;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Tests;

/// <summary>
/// Covers the pure parsing logic that backs .url/.lnk icon resolution.
/// These tests were added alongside the fix for shortcuts (.url/.lnk)
/// whose icons failed to render.
/// </summary>
public sealed class ShortcutParsingTests
{
    public sealed class ParseUrlShortcut
    {
        [Fact]
        public void ReadsUrlAndIconFromFile()
        {
            var content = """
                [InternetShortcut]
                URL=https://example.com/
                IconFile=C:\Icons\web.ico
                IconIndex=2
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal("https://example.com/", result.TargetUrl);
            Assert.Equal(@"C:\Icons\web.ico,2", result.IconLocation);
        }

        [Fact]
        public void DefaultsIconIndexToZeroWhenOmitted()
        {
            var content = """
                [InternetShortcut]
                URL=https://example.com/
                IconFile=C:\Icons\web.ico
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal(@"C:\Icons\web.ico,0", result.IconLocation);
        }

        [Fact]
        public void UrlWithoutIconYieldsEmptyIconLocation()
        {
            var content = """
                [InternetShortcut]
                URL=https://example.com/
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal("https://example.com/", result.TargetUrl);
            Assert.Equal(string.Empty, result.IconLocation);
        }

        [Fact]
        public void KeysAreCaseInsensitive()
        {
            // Windows reads .url keys case-insensitively; some editors emit lowercase.
            var content = """
                [InternetShortcut]
                url=https://example.com/
                iconfile=C:\Icons\web.ico
                iconindex=5
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal("https://example.com/", result.TargetUrl);
            Assert.Equal(@"C:\Icons\web.ico,5", result.IconLocation);
        }

        [Theory]
        [InlineData('#')]
        [InlineData(';')]
        public void SkipsCommentLines(char commentChar)
        {
            var content = $"""
                [InternetShortcut]
                {commentChar}IconFile=C:\ShouldBeIgnored.ico
                URL=https://example.com/
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal(string.Empty, result.IconLocation);
        }

        [Fact]
        public void HandlesCrlfAndWindowsLineEndings()
        {
            var content = "[InternetShortcut]\r\nURL=https://example.com/\r\nIconFile=C:\\Icons\\web.ico\r\nIconIndex=3\r\n";

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal(@"C:\Icons\web.ico,3", result.IconLocation);
        }

        [Fact]
        public void WhitespaceAroundValuesIsTrimmed()
        {
            var content = """
                [InternetShortcut]
                URL =   https://example.com/
                IconFile =  C:\Icons\web.ico
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal("https://example.com/", result.TargetUrl);
            Assert.Equal(@"C:\Icons\web.ico,0", result.IconLocation);
        }

        [Fact]
        public void AcceptsIconPathAlias()
        {
            // Some authoring tools emit IconPath instead of IconFile.
            var content = """
                [InternetShortcut]
                URL=https://example.com/
                IconPath=C:\Icons\web.ico
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out var result);

            Assert.True(ok);
            Assert.Equal(@"C:\Icons\web.ico,0", result.IconLocation);
        }

        [Fact]
        public void ReturnsFalseForEmptyContent()
        {
            var ok = ShortcutParsing.TryParseUrlShortcut(string.Empty, out _);

            Assert.False(ok);
        }

        [Fact]
        public void ReturnsFalseForContentWithNoRecognizedKeys()
        {
            var content = """
                [SomeOtherSection]
                Foo=bar
                """;

            var ok = ShortcutParsing.TryParseUrlShortcut(content, out _);

            Assert.False(ok);
        }
    }

    public sealed class ParseIconLocation
    {
        [Fact]
        public void SplitsFileAndIndex()
        {
            var ok = ShortcutParsing.TryParseIconLocation(
                @"C:\Windows\System32\imageres.dll,109",
                out var file,
                out var index);

            Assert.True(ok);
            Assert.Equal(@"C:\Windows\System32\imageres.dll", file);
            Assert.Equal(109, index);
        }

        [Fact]
        public void BarePathDefaultsToIndexZero()
        {
            var ok = ShortcutParsing.TryParseIconLocation(
                @"C:\Icons\web.ico",
                out var file,
                out var index);

            Assert.True(ok);
            Assert.Equal(@"C:\Icons\web.ico", file);
            Assert.Equal(0, index);
        }

        [Fact]
        public void StripsSurroundingQuotes()
        {
            var ok = ShortcutParsing.TryParseIconLocation(
                @"""C:\Program Files\App\app.exe"",0",
                out var file,
                out var index);

            Assert.True(ok);
            Assert.Equal(@"C:\Program Files\App\app.exe", file);
            Assert.Equal(0, index);
        }

        [Fact]
        public void SupportsNegativeIndices()
        {
            // Windows allows negative IconIndex values (resource IDs).
            var ok = ShortcutParsing.TryParseIconLocation(
                @"C:\Windows\System32\shell32.dll,-238",
                out var file,
                out var index);

            Assert.True(ok);
            Assert.Equal(@"C:\Windows\System32\shell32.dll", file);
            Assert.Equal(-238, index);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        public void ReturnsFalseForMissingOrBlankInput(string? input)
        {
            var ok = ShortcutParsing.TryParseIconLocation(input, out var file, out var index);

            Assert.False(ok);
            Assert.Equal(string.Empty, file);
            Assert.Equal(-1, index);
        }
    }
}
