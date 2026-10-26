// 그림 한 장짜리 PDF 쓰기(외부 라이브러리 없이). 내보내기 시트를 JPEG로 넣은 A4 가로 1쪽.
// 글자도 그림 안에 그려 넣으므로 한글 글꼴을 PDF에 넣을 필요가 없다.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace BuoyRuleCheck
{
    public static class RasterPdf
    {
        /// pageW·pageH: 쪽 크기(pt, 1/72 inch). A4 가로 = 842 × 595.
        public static void WriteImagePage(string path, Bitmap image, string title, double pageW = 842, double pageH = 595)
        {
            byte[] jpeg;
            using (var ms = new MemoryStream())
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                image.Save(ms, codec, ep);
                jpeg = ms.ToArray();
            }
            WriteJpegPage(path, jpeg, image.Width, image.Height, title, pageW, pageH);
        }

        /// JPEG 바이트를 그대로(DCTDecode) 넣는다 — 다시 압축하지 않음.
        public static void WriteJpegPage(string path, byte[] jpeg, int imgW, int imgH, string title, double pageW = 842, double pageH = 595)
        {
            // 그림 비율을 지키며 쪽 안에 맞춘다
            double s = Math.Min(pageW / imgW, pageH / imgH);
            double w = imgW * s, h = imgH * s, x = (pageW - w) / 2, y = (pageH - h) / 2;
            var content = Encoding.ASCII.GetBytes(FormattableString.Invariant($"q {w:0.###} 0 0 {h:0.###} {x:0.###} {y:0.###} cm /Im0 Do Q\n"));

            using var f = new FileStream(path, FileMode.Create, FileAccess.Write);
            var offsets = new List<long>();
            void W(string t) { var b = Encoding.ASCII.GetBytes(t); f.Write(b, 0, b.Length); }
            void Obj(string body) { offsets.Add(f.Position); W($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }

            W("%PDF-1.4\n");
            f.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);   // 이진 파일 표시 줄

            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
            Obj(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageW:0.###} {pageH:0.###}] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>"));
            offsets.Add(f.Position);
            W($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {imgW} /Height {imgH} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            f.Write(jpeg, 0, jpeg.Length);
            W("\nendstream\nendobj\n");
            offsets.Add(f.Position);
            W($"5 0 obj\n<< /Length {content.Length} >>\nstream\n");
            f.Write(content, 0, content.Length);
            W("endstream\nendobj\n");
            Obj($"<< /Title <{Utf16Hex(title)}> /Producer ({Product.Name}) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>");

            long xref = f.Position;
            W($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
            foreach (var o in offsets) W($"{o:D10} 00000 n \n");
            W($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info {offsets.Count} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        }

        /// PDF 문자열(제목 등)에 한글을 넣는 표준 방법: UTF-16BE + BOM을 16진수로.
        private static string Utf16Hex(string s)
        {
            var sb = new StringBuilder("FEFF");
            foreach (var b in Encoding.BigEndianUnicode.GetBytes(s)) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }
}
