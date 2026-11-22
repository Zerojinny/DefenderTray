using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DefenderTray
{
    enum Glyph { Check, Cross, Question, Dots }

    static class IconFactory
    {
        // 트레이·작업 표시줄에 쓰는 크기들
        static readonly int[] TraySizes = { 16, 20, 24, 32, 48, 64 };
        // exe/탐색기/UAC 리소스용 크기들
        static readonly int[] AppSizes = { 16, 20, 24, 32, 48, 64, 128, 256 };

        public static Icon CreateTrayIcon(RtpState state)
        {
            Color fill = state == RtpState.On ? Color.FromArgb(46, 160, 67)
                       : state == RtpState.Off ? Color.FromArgb(218, 54, 51)
                       : Color.FromArgb(128, 128, 128);
            Glyph glyph = state == RtpState.On ? Glyph.Check
                        : state == RtpState.Off ? Glyph.Cross
                        : Glyph.Question;
            return BestFit(BuildIco(TraySizes, fill, glyph));
        }

        public static Icon CreateBusyIcon()
        {
            return BestFit(BuildIco(TraySizes, Color.FromArgb(120, 120, 120), Glyph.Dots));
        }

        // 실행 파일 아이콘 색: 청록. Windows 기본 파랑(#0078D4)과 구분해 Microsoft 공식 제품처럼 보이지 않게 함
        public static readonly Color BrandColor = Color.FromArgb(13, 138, 120);

        // exe/UAC/탐색기용 아이콘(청록 방패 + 체크)을 .ico 파일로 저장
        public static void WriteAppIco(string path)
        {
            File.WriteAllBytes(path, BuildIco(AppSizes, BrandColor, Glyph.Check));
        }

        // 멀티 사이즈 ICO 바이트에서 현재 DPI의 작은 아이콘 크기에 맞는 프레임을 뽑아 반환
        static Icon BestFit(byte[] ico)
        {
            using (var ms = new MemoryStream(ico))
            using (var full = new Icon(ms))
            {
                Size want;
                try { want = SystemInformation.SmallIconSize; } catch { want = new Size(16, 16); }
                return new Icon(full, want);
            }
        }

        static byte[] BuildIco(int[] sizes, Color fill, Glyph glyph)
        {
            var images = new List<byte[]>();
            foreach (int sz in sizes)
                using (Bitmap bmp = Render(sz, fill, glyph))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }

            using (var ms = new MemoryStream())
            {
                var bw = new BinaryWriter(ms);
                bw.Write((short)0);            // reserved
                bw.Write((short)1);            // type = icon
                bw.Write((short)sizes.Length); // count
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int sz = sizes[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz)); // width
                    bw.Write((byte)(sz >= 256 ? 0 : sz)); // height
                    bw.Write((byte)0);  // palette
                    bw.Write((byte)0);  // reserved
                    bw.Write((short)1); // planes
                    bw.Write((short)32);// bpp
                    bw.Write(images[i].Length);
                    bw.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) bw.Write(img);
                bw.Flush();
                return ms.ToArray();
            }
        }

        static Bitmap Render(int size, Color fill, Glyph glyph)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                float s = size / 32f;
                g.ScaleTransform(s, s); // 32px 좌표계로 그린 뒤 실제 크기로 직접 렌더링(축소 뭉개짐 방지)

                // 방패: 위쪽은 반듯한 어깨, 아래는 둥글게 모이는 모양
                using (var path = new GraphicsPath())
                {
                    path.AddLine(16, 1, 29, 5.5f);
                    path.AddLine(29, 5.5f, 29, 15);
                    path.AddBezier(29, 15, 29, 23, 23, 28, 16, 31);
                    path.AddBezier(16, 31, 9, 28, 3, 23, 3, 15);
                    path.AddLine(3, 15, 3, 5.5f);
                    path.CloseFigure();
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    float outline = size <= 20 ? 1.8f : 1.4f;
                    using (var p = new Pen(Color.FromArgb(220, 255, 255, 255), outline) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
                }

                // 가운데 표시: 방패 안쪽 중심(16, 15)에 맞춤. 작은 크기에서는 조금 굵게
                const float cx = 16f, cy = 15f, k = 0.92f;
                float gw = size <= 20 ? 3.8f : 3.4f;
                using (var pen = new Pen(Color.White, gw)
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (glyph == Glyph.Check)
                        g.DrawLines(pen, new[] { new PointF(cx - 6 * k, cy), new PointF(cx - 1.5f * k, cy + 5 * k), new PointF(cx + 6 * k, cy - 5 * k) });
                    else if (glyph == Glyph.Cross)
                    { g.DrawLine(pen, cx - 5 * k, cy - 5 * k, cx + 5 * k, cy + 5 * k); g.DrawLine(pen, cx + 5 * k, cy - 5 * k, cx - 5 * k, cy + 5 * k); }
                    else if (glyph == Glyph.Dots)
                        using (var b = new SolidBrush(Color.White))
                            for (int i = -1; i <= 1; i++)
                                g.FillEllipse(b, cx + i * 6.2f * k - 1.8f * k, cy - 1.8f * k, 3.6f * k, 3.6f * k);
                    else // Question: 글꼴 대신 선으로 그림
                    {
                        using (var q = new GraphicsPath())
                        {
                            q.AddArc(cx - 4.5f * k, cy - 8 * k, 9 * k, 8 * k, 200, 250);
                            q.AddLine(cx + 0.5f * k, cy + 0.2f * k, cx, cy + 2.6f * k);
                            g.DrawPath(pen, q);
                        }
                        using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, cx - 1.9f * k, cy + 5 * k, 3.8f * k, 3.8f * k);
                    }
                }
            }
            return bmp;
        }
    }
}
