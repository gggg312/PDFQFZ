using System.Linq;
using PDFQFZ.Library;
using Xunit;

namespace PDFQFZ.Tests;

public sealed class StampPlacementCollectionTests
{
    [Fact]
    public void Add_AllowsMultiplePlacementsOnTheSamePage()
    {
        var placements = new StampPlacementCollection();

        placements.Add("a.pdf", 1, 0.1f, 0.2f, "stamp.png", 40, 60, 0, 20, true, true);
        placements.Add("a.pdf", 1, 0.6f, 0.7f, "stamp.png", 40, 60, 0, 20, true, true);

        var pagePlacements = placements.ForPage("a.pdf", 1).ToList();

        Assert.Equal(2, pagePlacements.Count);
        Assert.Equal(0.1f, pagePlacements[0].X);
        Assert.Equal(0.6f, pagePlacements[1].X);
    }

    [Fact]
    public void Remove_DeletesOnlyTheRequestedPlacement()
    {
        var placements = new StampPlacementCollection();
        var first = placements.Add("a.pdf", 1, 0.1f, 0.2f, "stamp.png", 40, 60, 0, 20, true, true);
        var second = placements.Add("a.pdf", 1, 0.6f, 0.7f, "stamp.png", 40, 60, 0, 20, true, true);

        Assert.True(placements.Remove(first.Id));
        Assert.Null(placements.Find(first.Id));
        Assert.Same(second, placements.Find(second.Id));
    }

    [Fact]
    public void DistinctPages_ReturnsSortedUniquePages()
    {
        var placements = new StampPlacementCollection();
        placements.Add("a.pdf", 3, 0.1f, 0.2f, "stamp.png", 40, 60, 0, 20, true, true);
        placements.Add("a.pdf", 1, 0.2f, 0.3f, "stamp.png", 40, 60, 0, 20, true, true);
        placements.Add("a.pdf", 3, 0.4f, 0.5f, "stamp.png", 40, 60, 0, 20, true, true);

        Assert.Equal(new[] { 1, 3 }, placements.DistinctPages("a.pdf"));
    }

    [Fact]
    public void Add_BatchCanBeRemovedAsWholeOrForOnePage()
    {
        var placements = new StampPlacementCollection();
        int batchId = placements.CreateBatchId();
        placements.Add("doc.pdf", 2, 0.1f, 0.2f, "stamp.png", 40, 60, 0, 20, false, true, batchId);
        placements.Add("doc.pdf", 3, 0.1f, 0.2f, "stamp.png", 40, 60, 0, 20, false, true, batchId);

        Assert.Equal(1, placements.RemoveBatchOnPage("doc.pdf", 2, batchId));
        Assert.Single(placements.ForPage("doc.pdf", 3));
        Assert.Equal(1, placements.RemoveBatch("doc.pdf", batchId));
        Assert.Empty(placements.ForPage("doc.pdf", 3));
    }
}
