using System.Net;
using System.Net.Sockets;
using OrderManagement.Infrastructure.Assistant;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class ProviderErrorsTests
{
    private static readonly CancellationToken NotCancelled = CancellationToken.None;

    private static HttpRequestException Http(HttpStatusCode? status) => new("provider error", null, status);

    [Fact]
    public void Cancelled_token_is_never_transient()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ProviderErrors.IsTransient(Http(null), cts.Token).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Http_request_exception_transient_statuses(int? status) =>
        ProviderErrors.IsTransient(Http((HttpStatusCode?)status), NotCancelled).ShouldBeTrue();

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public void Http_request_exception_client_errors_are_not_transient(int status) =>
        ProviderErrors.IsTransient(Http((HttpStatusCode)status), NotCancelled).ShouldBeFalse();

    [Fact]
    public void Timeout_without_caller_cancel_is_transient()
    {
        ProviderErrors.IsTransient(new TaskCanceledException("timed out"), NotCancelled).ShouldBeTrue();
        ProviderErrors.IsTransient(new TimeoutException(), NotCancelled).ShouldBeTrue();
    }

    [Fact]
    public void Plain_operation_cancelled_without_caller_cancel_is_transient() =>
        ProviderErrors.IsTransient(new OperationCanceledException(), NotCancelled).ShouldBeTrue();

    [Fact]
    public void Socket_and_io_errors_are_transient()
    {
        ProviderErrors.IsTransient(new SocketException((int)SocketError.ConnectionRefused), NotCancelled).ShouldBeTrue();
        ProviderErrors.IsTransient(new IOException("connection reset"), NotCancelled).ShouldBeTrue();
    }

    [Fact]
    public void Inner_exception_is_checked() =>
        ProviderErrors.IsTransient(new InvalidOperationException("wrapper", Http(null)), NotCancelled).ShouldBeTrue();

    [Fact]
    public void Inner_client_error_is_not_transient() =>
        ProviderErrors.IsTransient(new InvalidOperationException("wrapper", Http(HttpStatusCode.Unauthorized)), NotCancelled).ShouldBeFalse();

    [Fact]
    public void Unknown_exception_is_not_transient() =>
        ProviderErrors.IsTransient(new InvalidOperationException("boom"), NotCancelled).ShouldBeFalse();

    [Theory]
    [InlineData(Grpc.Core.StatusCode.Unavailable)]
    [InlineData(Grpc.Core.StatusCode.DeadlineExceeded)]
    [InlineData(Grpc.Core.StatusCode.ResourceExhausted)]
    public void Transient_grpc_statuses_are_transient(Grpc.Core.StatusCode status) =>
        ProviderErrors.IsTransient(new Grpc.Core.RpcException(new Grpc.Core.Status(status, "qdrant")), NotCancelled).ShouldBeTrue();

    [Theory]
    [InlineData(Grpc.Core.StatusCode.NotFound)]
    [InlineData(Grpc.Core.StatusCode.InvalidArgument)]
    [InlineData(Grpc.Core.StatusCode.Unauthenticated)]
    public void Other_grpc_statuses_are_not_transient(Grpc.Core.StatusCode status) =>
        ProviderErrors.IsTransient(new Grpc.Core.RpcException(new Grpc.Core.Status(status, "qdrant")), NotCancelled).ShouldBeFalse();
}
