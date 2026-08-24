using System;
using System.Collections.Generic;
using System.Linq;

namespace PDFQFZ.Library
{
    internal sealed class StampPlacement
    {
        public StampPlacement(
            int id,
            string documentPath,
            int page,
            float x,
            float y,
            string stampPath,
            int sizeMm,
            int opacity,
            int rotation,
            int whiteTransparencyTolerance,
            bool useWhiteTransparency,
            bool useOriginalRotationCrop,
            int batchId)
        {
            Id = id;
            DocumentPath = documentPath ?? string.Empty;
            Page = page;
            X = x;
            Y = y;
            StampPath = stampPath ?? string.Empty;
            SizeMm = sizeMm;
            Opacity = opacity;
            Rotation = rotation;
            WhiteTransparencyTolerance = whiteTransparencyTolerance;
            UseWhiteTransparency = useWhiteTransparency;
            UseOriginalRotationCrop = useOriginalRotationCrop;
            BatchId = batchId;
        }

        public int Id { get; }
        public string DocumentPath { get; }
        public int Page { get; }
        public float X { get; }
        public float Y { get; }
        public string StampPath { get; }
        public int SizeMm { get; }
        public int Opacity { get; }
        public int Rotation { get; }
        public int WhiteTransparencyTolerance { get; }
        public bool UseWhiteTransparency { get; }
        public bool UseOriginalRotationCrop { get; }
        public int BatchId { get; }
    }

    internal sealed class StampPlacementCollection
    {
        private readonly List<StampPlacement> placements = new List<StampPlacement>();
        private int nextId = 1;

        public int Count => placements.Count;

        public StampPlacement Add(
            string documentPath,
            int page,
            float x,
            float y,
            string stampPath,
            int sizeMm,
            int opacity,
            int rotation,
            int whiteTransparencyTolerance,
            bool useWhiteTransparency,
            bool useOriginalRotationCrop,
            int batchId = 0)
        {
            StampPlacement placement = new StampPlacement(
                nextId++,
                documentPath,
                page,
                x,
                y,
                stampPath,
                sizeMm,
                opacity,
                rotation,
                whiteTransparencyTolerance,
                useWhiteTransparency,
                useOriginalRotationCrop,
                batchId);
            placements.Add(placement);
            return placement;
        }

        public IEnumerable<StampPlacement> ForPage(string documentPath, int page)
        {
            return placements.Where(item =>
                string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                item.Page == page);
        }

        public StampPlacement Find(int id)
        {
            return placements.FirstOrDefault(item => item.Id == id);
        }

        public bool Remove(int id)
        {
            StampPlacement placement = Find(id);
            return placement != null && placements.Remove(placement);
        }

        public int RemovePage(string documentPath, int page)
        {
            return placements.RemoveAll(item =>
                string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                item.Page == page);
        }

        public int RemoveBatch(string documentPath, int batchId)
        {
            if (batchId <= 0)
            {
                return 0;
            }

            return placements.RemoveAll(item =>
                string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                item.BatchId == batchId);
        }

        public int RemoveBatchOnPage(string documentPath, int page, int batchId)
        {
            if (batchId <= 0)
            {
                return 0;
            }

            return placements.RemoveAll(item =>
                string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
                item.Page == page &&
                item.BatchId == batchId);
        }

        public int CreateBatchId()
        {
            return nextId++;
        }

        public IEnumerable<int> DistinctPages(string documentPath)
        {
            return placements
                .Where(item => string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Page)
                .Distinct()
                .OrderBy(page => page);
        }

        public void Clear()
        {
            placements.Clear();
        }
    }
}
