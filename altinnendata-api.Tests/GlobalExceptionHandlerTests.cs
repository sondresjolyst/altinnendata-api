using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Storage;
using Xunit;

namespace altinnendata_api.Tests;

public class GlobalExceptionHandlerTests
{
    private static (GlobalExceptionHandler handler, DefaultHttpContext ctx) Build()
    {
        var problemDetails = new Mock<IProblemDetailsService>();
        problemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>())).ReturnsAsync(true);
        var handler = new GlobalExceptionHandler(problemDetails.Object, NullLogger<GlobalExceptionHandler>.Instance);
        return (handler, new DefaultHttpContext());
    }

    [Fact]
    public async Task FileRefused_MapsTo400()
    {
        var (handler, ctx) = Build();
        await handler.TryHandleAsync(ctx, new AppKitFileRefusedException("bad file"), default);
        Assert.Equal(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task UnexpectedException_MapsTo500()
    {
        var (handler, ctx) = Build();
        await handler.TryHandleAsync(ctx, new InvalidOperationException("boom"), default);
        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
    }
}
