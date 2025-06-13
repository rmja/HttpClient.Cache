using System.Linq.Expressions;
using Moq;
using Moq.Protected;

namespace HttpClient.Cache.Tests;

public static class HttpMessageHandlerMockExtensions
{
    public static void SetupSendAsync(
        this Mock<HttpMessageHandler> mock,
        Expression<Func<HttpRequestMessage, bool>> request,
        HttpResponseMessage response
    )
    {
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is(request),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(response);
    }

    public static void SetupSendAsync(
        this Mock<HttpMessageHandler> mock,
        Expression<Func<HttpRequestMessage, bool>> request,
        Func<HttpResponseMessage> responseFactory
    )
    {
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is(request),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(responseFactory);
    }
}
