using System;

namespace PDFQFZ.Library
{
    internal sealed class PageRange
    {
        private PageRange(int startPage, int endPage)
        {
            StartPage = startPage;
            EndPage = endPage;
        }

        public int StartPage { get; }
        public int EndPage { get; }

        public bool Contains(int page)
        {
            return page >= StartPage && page <= EndPage;
        }

        public static bool TryCreate(string startText, string endText, int pageCount, out PageRange range, out string error)
        {
            range = null;
            error = string.Empty;
            if (pageCount < 1)
            {
                error = "当前 PDF 没有可用页面。";
                return false;
            }

            if (!int.TryParse((startText ?? string.Empty).Trim(), out int startPage) ||
                !int.TryParse((endText ?? string.Empty).Trim(), out int endPage))
            {
                error = "起始页和结尾页必须是整数。";
                return false;
            }

            if (startPage < 1 || endPage < 1 || startPage > pageCount || endPage > pageCount)
            {
                error = "页码必须在 1 到当前 PDF 总页数之间。";
                return false;
            }

            if (startPage > endPage)
            {
                error = "起始页不能大于结尾页。";
                return false;
            }

            range = new PageRange(startPage, endPage);
            return true;
        }
    }
}
