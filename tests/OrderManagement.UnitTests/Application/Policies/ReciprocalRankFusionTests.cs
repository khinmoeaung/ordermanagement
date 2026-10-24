using OrderManagement.Application.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Application.Policies;

public class ReciprocalRankFusionTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    [Fact]
    public void Rrf_scores_match_a_worked_example()
    {
        var fused = ReciprocalRankFusion.Fuse([[A, B, C], [B, C, D]], 60);

        fused.Select(f => f.Id).ShouldBe([B, C, A, D]);
        fused[0].Score.ShouldBe(1.0 / 62 + 1.0 / 61, 1e-12);
        fused[1].Score.ShouldBe(1.0 / 63 + 1.0 / 62, 1e-12);
        fused[2].Score.ShouldBe(1.0 / 61, 1e-12);
        fused[3].Score.ShouldBe(1.0 / 63, 1e-12);
    }

    [Fact]
    public void Rrf_with_one_empty_list_keeps_the_other_order() =>
        ReciprocalRankFusion.Fuse([[A, B, C], []], 60).Select(f => f.Id).ShouldBe([A, B, C]);

    [Fact]
    public void Rrf_with_no_lists_returns_nothing() =>
        ReciprocalRankFusion.Fuse([], 60).ShouldBeEmpty();
}
