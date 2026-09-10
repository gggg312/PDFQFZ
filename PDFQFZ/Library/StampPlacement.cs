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
            bool randomRotation,
            int batchId,
            bool centerRatio = false,
            float offsetXmm = 0f,
            float offsetYmm = 0f,
            bool textureEnabled = false,
            int textureSeed = 0,
            int textureBrightness = 0,
            int textureBlob = 0,
            int textureGradient = 0,
            int textureWhite = 0,
            int textureSpot = 0,
            int textureRadial = 0,
            int textureCast = 0,
            float textureKb = 0.5f,
            float textureKblob = 0.5f,
            float textureKgrad = 0.5f,
            float textureKwhite = 0.5f,
            float textureKspot = 0.5f,
            float textureKradial = 0.5f,
            float textureKcast = 0.5f)
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
            RandomRotation = randomRotation;
            BatchId = batchId;
            CenterRatio = centerRatio;
            OffsetXmm = offsetXmm;
            OffsetYmm = offsetYmm;
            TextureEnabled = textureEnabled;
            TextureSeed = textureSeed;
            TextureBrightness = textureBrightness;
            TextureBlob = textureBlob;
            TextureGradient = textureGradient;
            TextureWhite = textureWhite;
            TextureSpot = textureSpot;
            TextureRadial = textureRadial;
            TextureCast = textureCast;
            TextureKb = textureKb;
            TextureKblob = textureKblob;
            TextureKgrad = textureKgrad;
            TextureKwhite = textureKwhite;
            TextureKspot = textureKspot;
            TextureKradial = textureKradial;
            TextureKcast = textureKcast;
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
        /// <summary>true=该章角度由随机旋转生成（渲染时按不切边完整显示真实角度，避免大角度被“旋转切边”压缩裁剪）。</summary>
        public bool RandomRotation { get; }
        public int BatchId { get; }
        /// <summary>坐标语义：true=印章中心在页面内的比例（手动/范围页盖章，可超出页面被边界裁剪）；false=印章左上角在"页面减章宽"区间内的比例（按文字盖章，完整在页面内）。</summary>
        public bool CenterRatio { get; }
        /// <summary>随机位移（mm，可正负）：放置时随机生成并固定，预览与输出使用同一值。</summary>
        public float OffsetXmm { get; }
        public float OffsetYmm { get; }
        /// <summary>盖章渲染：true=按四维上限随机生成印泥质感（每次盖章独立种子）。</summary>
        public bool TextureEnabled { get; }
        /// <summary>质感随机种子：放置时生成并固定，预览刷新与输出使用同一纹理。</summary>
        public int TextureSeed { get; set; }
        /// <summary>明暗强度上限 0-100（每章在 0~上限 随机）。</summary>
        public int TextureBrightness { get; set; }
        /// <summary>斑块大小上限 0-100（每章在 0~上限 随机，越大斑块越大）。</summary>
        public int TextureBlob { get; set; }
        /// <summary>渐变上限 0-100（每章在 0~上限 随机，一侧重一侧轻）。</summary>
        public int TextureGradient { get; set; }
        /// <summary>局部露白上限 0-100（每章在 0~上限 随机，印泥薄处接近纸色）。</summary>
        public int TextureWhite { get; set; }
        /// <summary>内部斑点上限 0-100（每章在 0~上限 随机，空白处簇状印泥斑点）。</summary>
        public int TextureSpot { get; set; }
        public int TextureRadial { get; set; }
        public int TextureCast { get; set; }
        /// <summary>印泥浓淡不均强度系数 0~1（盖章时固化；实际强度 = 系数 × 参数上限，调参平滑、章章不同）。</summary>
        public float TextureKb { get; set; }
        /// <summary>斑点大小强度系数 0~1。</summary>
        public float TextureKblob { get; set; }
        /// <summary>渐变强度系数 0~1。</summary>
        public float TextureKgrad { get; set; }
        /// <summary>局部露白强度系数 0~1。</summary>
        public float TextureKwhite { get; set; }
        /// <summary>内部斑点密度系数 0~1。</summary>
        public float TextureKspot { get; set; }
        /// <summary>径向压印强度系数 0~1。</summary>
        public float TextureKradial { get; set; }
        /// <summary>整体色偏强度系数 0~1。</summary>
        public float TextureKcast { get; set; }
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
            bool randomRotation = false,
            int batchId = 0,
            bool centerRatio = false,
            float offsetXmm = 0f,
            float offsetYmm = 0f,
            bool textureEnabled = false,
            int textureSeed = 0,
            int textureBrightness = 0,
            int textureBlob = 0,
            int textureGradient = 0,
            int textureWhite = 0,
            int textureSpot = 0,
            int textureRadial = 0,
            int textureCast = 0,
            float textureKb = 0.5f,
            float textureKblob = 0.5f,
            float textureKgrad = 0.5f,
            float textureKwhite = 0.5f,
            float textureKspot = 0.5f,
            float textureKradial = 0.5f,
            float textureKcast = 0.5f)
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
                randomRotation,
                batchId,
                centerRatio,
                offsetXmm,
                offsetYmm,
                textureEnabled,
                textureSeed,
                textureBrightness,
                textureBlob,
                textureGradient,
                textureWhite,
                textureSpot,
                textureRadial,
                textureCast,
                textureKb,
                textureKblob,
                textureKgrad,
                textureKwhite,
                textureKspot,
                textureKradial,
                textureKcast);
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

        /// <summary>指定文档中该批次是否仍存在印章。</summary>
        public bool HasBatch(string documentPath, int batchId)
        {
            if (batchId <= 0)
            {
                return false;
            }

            return placements.Any(item =>
                string.Equals(item.DocumentPath, documentPath, StringComparison.OrdinalIgnoreCase) &&
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
