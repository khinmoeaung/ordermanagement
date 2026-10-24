namespace OrderManagement.Application.Policies;

public static class ReciprocalRankFusion
{
    /// <summary>Merges ranked ID lists: an ID scores the sum of 1 / (k + rank) over the lists it appears in (rank starts at 1).</summary>
    public static IReadOnlyList<(Guid Id, double Score)> Fuse(IReadOnlyList<IReadOnlyList<Guid>> rankedLists, int k)
    {
        var scores = new Dictionary<Guid, double>();
        var firstSeen = new Dictionary<Guid, int>();

        foreach (var list in rankedLists)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Guid id = list[i];
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (k + i + 1);
                firstSeen.TryAdd(id, firstSeen.Count);
            }
        }

        return scores
            .OrderByDescending(s => s.Value)
            .ThenBy(s => firstSeen[s.Key])
            .Select(s => (s.Key, s.Value))
            .ToList();
    }
}
