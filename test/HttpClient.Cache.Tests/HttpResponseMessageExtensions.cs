namespace HttpClient.Cache.Tests;

internal static class HttpResponseMessageExtensions
{
    public static Task<string> ReadAsStringAsync(this HttpResponseMessage response)
    {
        return response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
