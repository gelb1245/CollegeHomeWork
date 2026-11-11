using System.Text;
using System.Web;
using Microsoft.AspNetCore.Http;
using TelegramBotApi.Services;
using Xunit;

namespace CollegeHomeWork.Tests;

public class AttachmentUrlSignerTests
{
    private const string Token = "123456:TEST-token";

    private static (int Id, long Exp, string Sig) Parse(string url)
    {
        var uri = new Uri("https://x" + url);
        var q = HttpUtility.ParseQueryString(uri.Query);
        var id = int.Parse(uri.AbsolutePath.Split('/').Last());
        return (id, long.Parse(q["exp"]!), q["sig"]!);
    }

    [Fact]
    public void CreatedLink_IsValid()
    {
        var signer = new AttachmentUrlSigner(Token);
        var (id, exp, sig) = Parse(signer.CreateUrl(42));

        Assert.Equal(42, id);
        Assert.True(signer.IsValid(id, exp, sig));
    }

    [Fact]
    public void LinkForAnotherFile_IsRejected()
    {
        // Signature of file 42 must not open file 43
        var signer = new AttachmentUrlSigner(Token);
        var (_, exp, sig) = Parse(signer.CreateUrl(42));

        Assert.False(signer.IsValid(43, exp, sig));
    }

    [Fact]
    public void ExtendedExpiry_IsRejected()
    {
        var signer = new AttachmentUrlSigner(Token);
        var (id, exp, sig) = Parse(signer.CreateUrl(42));

        Assert.False(signer.IsValid(id, exp + 86400, sig));
    }

    [Fact]
    public void ExpiredLink_IsRejected()
    {
        var signer = new AttachmentUrlSigner(Token);
        var past = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();

        Assert.False(signer.IsValid(42, past, "00000000000000000000000000000000"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("deadbeef")]
    public void MissingOrWrongSignature_IsRejected(string? sig)
    {
        var signer = new AttachmentUrlSigner(Token);
        var (id, exp, _) = Parse(signer.CreateUrl(42));

        Assert.False(signer.IsValid(id, exp, sig));
    }

    [Fact]
    public void LinkSignedWithAnotherToken_IsRejected()
    {
        var (id, exp, sig) = Parse(new AttachmentUrlSigner("999:other").CreateUrl(42));

        Assert.False(new AttachmentUrlSigner(Token).IsValid(id, exp, sig));
    }
}

/// <summary>Upload validation checks the real file content (magic bytes), not the name the client sent.</summary>
public class AttachmentValidationTests
{
    private static IFormFile File(byte[] content, string name, long? declaredLength = null) =>
        new FormFile(new MemoryStream(content), 0, declaredLength ?? content.Length, "files", name);

    public static IEnumerable<object[]> AllowedFiles => new[]
    {
        new object[] { new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 }, "photo.jpg" },
        new object[] { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "photo.png" },
        new object[] { Encoding.ASCII.GetBytes("GIF89a......"), "anim.gif" },
        new object[] { Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "), "photo.webp" },
        new object[] { Encoding.ASCII.GetBytes("%PDF-1.7\n..."), "task.pdf" },
    };

    [Theory]
    [MemberData(nameof(AllowedFiles))]
    public async Task PhotosAndPdf_AreAccepted(byte[] content, string name)
    {
        Assert.Null(await AttachmentStorage.ValidateAsync(File(content, name), CancellationToken.None));
    }

    [Theory]
    [InlineData("MZ\x90\0 windows exe", "homework.pdf")]       // executable renamed to .pdf
    [InlineData("<html><script>alert(1)</script>", "x.png")]    // HTML pretending to be an image
    [InlineData("PK\x03\x04 zip archive", "task.jpg")]
    public async Task OtherContent_IsRejected_EvenWithAllowedExtension(string content, string name)
    {
        var error = await AttachmentStorage.ValidateAsync(File(Encoding.Latin1.GetBytes(content), name), CancellationToken.None);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task EmptyFile_IsRejected()
    {
        Assert.NotNull(await AttachmentStorage.ValidateAsync(File(Array.Empty<byte>(), "a.pdf"), CancellationToken.None));
    }

    [Fact]
    public async Task FileOver20Mb_IsRejected()
    {
        var file = File(Encoding.ASCII.GetBytes("%PDF-1.7"), "big.pdf", declaredLength: AttachmentStorage.MaxFileSize + 1);
        var error = await AttachmentStorage.ValidateAsync(file, CancellationToken.None);
        Assert.Contains("20 МБ", error);
    }
}
