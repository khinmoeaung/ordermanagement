using System.Text.Json;
using OrderManagement.Application.Assistant;
using OrderManagement.Domain.Enums;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Assistant;

public class ToolInputTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("null")]
    [InlineData("\"x\"")]
    [InlineData("[]")]
    public void RequireObject_rejects_non_object_inputs(string json) =>
        Should.Throw<ToolInputException>(() => ToolInput.RequireObject(Json(json), "orderNumber"));

    [Fact]
    public void RequireObject_rejects_unknown_property()
    {
        var ex = Should.Throw<ToolInputException>(() =>
            ToolInput.RequireObject(Json("""{"orderNumber":"ORD-20260101-ABCDEF","customerId":"x"}"""), "orderNumber"));
        ex.Message.ShouldContain("customerId");
    }

    [Fact]
    public void RequireObject_accepts_allowed_properties() =>
        ToolInput.RequireObject(Json("""{"orderNumber":"x"}"""), "orderNumber");

    [Fact]
    public void RequiredString_rejects_missing_and_non_string()
    {
        Should.Throw<ToolInputException>(() => ToolInput.RequiredString(Json("{}"), "orderNumber"));
        Should.Throw<ToolInputException>(() => ToolInput.RequiredString(Json("""{"orderNumber":5}"""), "orderNumber"));
        ToolInput.RequiredString(Json("""{"orderNumber":"abc"}"""), "orderNumber").ShouldBe("abc");
    }

    [Theory]
    [InlineData("""{"status":"pending"}""")]
    [InlineData("""{"status":1}""")]
    [InlineData("""{"status":"Unknown"}""")]
    public void OptionalEnum_rejects_wrong_case_and_numbers(string json) =>
        Should.Throw<ToolInputException>(() => ToolInput.OptionalEnum<OrderStatus>(Json(json), "status"));

    [Fact]
    public void OptionalEnum_reads_exact_name_or_absent()
    {
        ToolInput.OptionalEnum<OrderStatus>(Json("""{"status":"Shipped"}"""), "status").ShouldBe(OrderStatus.Shipped);
        ToolInput.OptionalEnum<OrderStatus>(Json("{}"), "status").ShouldBeNull();
    }
}
