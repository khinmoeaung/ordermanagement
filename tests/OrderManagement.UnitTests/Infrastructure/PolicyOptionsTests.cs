using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class PolicyOptionsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        var o = new PolicyOptions();

        PolicyOptions.Validate(o).ShouldBeTrue();
        o.Directory.ShouldBe("policies");
        o.Embedding.Model.ShouldBe("qwen3-embedding:0.6b");
        o.Embedding.Endpoint.ShouldBe("");
        o.Embedding.QueryInstruction.ShouldNotBeNullOrWhiteSpace();
        o.Qdrant.Host.ShouldBe("qdrant");
        o.Qdrant.GrpcPort.ShouldBe(6334);
        o.Qdrant.Collection.ShouldBe("policy_chunks");
        o.Qdrant.ApiKey.ShouldBe("");
        o.Chunking.MaxChars.ShouldBe(1500);
        o.Retrieval.TopK.ShouldBe(8);
        o.Retrieval.FinalCount.ShouldBe(4);
        o.Retrieval.RrfK.ShouldBe(60);
    }

    [Fact]
    public void ToSettings_carries_the_chunking_and_retrieval_values() =>
        new PolicyOptions().ToSettings().ShouldBe(new PolicySettings(1500, 8, 4, 60));

    [Theory]
    [InlineData(0, 4, 1500, 60)]
    [InlineData(8, 0, 1500, 60)]
    [InlineData(8, 4, 0, 60)]
    [InlineData(8, 4, 1500, 0)]
    [InlineData(-1, -1, 1500, 60)]
    public void Non_positive_top_k_final_count_max_chars_or_rrf_k_invalid(int topK, int finalCount, int maxChars, int rrfK)
    {
        var o = new PolicyOptions();
        o.Retrieval.TopK = topK;
        o.Retrieval.FinalCount = finalCount;
        o.Retrieval.RrfK = rrfK;
        o.Chunking.MaxChars = maxChars;

        PolicyOptions.Validate(o).ShouldBeFalse();
    }

    [Fact]
    public void Final_count_above_top_k_invalid()
    {
        var o = new PolicyOptions();
        o.Retrieval.TopK = 3;
        o.Retrieval.FinalCount = 4;

        PolicyOptions.Validate(o).ShouldBeFalse();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative")]
    [InlineData("ftp://host/")]
    public void Bad_embedding_endpoint_invalid(string endpoint)
    {
        var o = new PolicyOptions();
        o.Embedding.Endpoint = endpoint;

        PolicyOptions.Validate(o).ShouldBeFalse();
    }

    [Fact]
    public void Valid_http_endpoint_accepted()
    {
        var o = new PolicyOptions();
        o.Embedding.Endpoint = "http://192.168.1.20:11434";

        PolicyOptions.Validate(o).ShouldBeTrue();
    }

    [Fact]
    public void Blank_host_collection_directory_or_model_invalid()
    {
        var host = new PolicyOptions();
        host.Qdrant.Host = " ";
        var collection = new PolicyOptions();
        collection.Qdrant.Collection = "";
        var directory = new PolicyOptions { Directory = "" };
        var model = new PolicyOptions();
        model.Embedding.Model = "";

        foreach (var o in new[] { host, collection, directory, model })
            PolicyOptions.Validate(o).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Port_out_of_range_invalid(int port)
    {
        var o = new PolicyOptions();
        o.Qdrant.GrpcPort = port;

        PolicyOptions.Validate(o).ShouldBeFalse();
    }
}
