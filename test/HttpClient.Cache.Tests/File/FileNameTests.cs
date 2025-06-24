using HttpClientCache.Files;

namespace HttpClientCache.Tests.File;

public class FileNameTests
{
    [Fact]
    public void CanParseFileName_WithEtagHash()
    {
        var fileInfo = new FileInfo(
            "efed661293e37fe7141f28819252c3365aadae65_2025-06-24T102811Z_c6e5a96ed499f364089ebe92d16e13280ef750ce.response.json"
        );
        var fileName = FileName.FromFileInfo(fileInfo);

        Assert.Equal("efed661293e37fe7141f28819252c3365aadae65", fileName.KeyHash);
        Assert.Equal(new DateTime(2025, 6, 24, 10, 28, 11, DateTimeKind.Utc), fileName.ModifiedUtc);
        Assert.Equal("c6e5a96ed499f364089ebe92d16e13280ef750ce", fileName.EtagHash);
    }

    [Fact]
    public void CanParseFileName_WithoutEtag()
    {
        var fileInfo = new FileInfo(
            "efed661293e37fe7141f28819252c3365aadae65_2025-06-24T102811Z_.response.json"
        );
        var fileName = FileName.FromFileInfo(fileInfo);

        Assert.Equal("efed661293e37fe7141f28819252c3365aadae65", fileName.KeyHash);
        Assert.Equal(new DateTime(2025, 6, 24, 10, 28, 11, DateTimeKind.Utc), fileName.ModifiedUtc);
        Assert.Null(fileName.EtagHash);
    }
}
