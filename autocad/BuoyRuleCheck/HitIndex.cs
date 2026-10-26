// 마우스 위치 → 객체 찾기용 공간 인덱스(균일 격자). 객체가 수만 개여도 커서 근처 칸만 본다.
//   선분에서 tol 안이면 가장 가까운 객체, 아니면 커서를 감싸는 닫힌 도형 중 가장 작은 것(영역 hover).
// 좌표 단위는 쓰는 쪽 마음(비교 창은 m, 도면 hover는 도면 단위).
using System;
using System.Collections.Generic;

namespace BuoyRuleCheck
{
    public sealed class HitIndex
    {
        private readonly struct Seg
        {
            public readonly string Id;
            public readonly double X0, Y0, X1, Y1;
            public Seg(string id, double x0, double y0, double x1, double y1) { Id = id; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        }

        private sealed class Poly
        {
            public string Id = "";
            public double[][] Pts = Array.Empty<double[]>();
            public double MinX, MinY, MaxX, MaxY, Area;
        }

        private readonly double _x0, _y0, _cell;
        private readonly int _nx, _ny;
        private readonly List<Seg>[] _segCells;
        private readonly List<Poly>[] _polyCells;
        public int Count { get; private set; }

        /// shapes: (id, 점 목록, 닫힘). 점이 1개면 그 점 자체가 대상(조종위치 점 등).
        public HitIndex(IEnumerable<(string id, IReadOnlyList<double[]> pts, bool closed)> shapes, int maxCellsPerSide = 96)
        {
            var list = new List<(string id, IReadOnlyList<double[]> pts, bool closed)>();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in shapes)
            {
                if (s.pts.Count == 0) continue;
                list.Add(s);
                foreach (var p in s.pts)
                {
                    minX = Math.Min(minX, p[0]); maxX = Math.Max(maxX, p[0]);
                    minY = Math.Min(minY, p[1]); maxY = Math.Max(maxY, p[1]);
                }
            }
            if (list.Count == 0) { minX = minY = 0; maxX = maxY = 1; }
            double span = Math.Max(Math.Max(maxX - minX, maxY - minY), 1e-9);
            _cell = span / maxCellsPerSide;
            _x0 = minX; _y0 = minY;
            _nx = Math.Max(1, (int)Math.Ceiling((maxX - minX) / _cell) + 1);
            _ny = Math.Max(1, (int)Math.Ceiling((maxY - minY) / _cell) + 1);
            _segCells = new List<Seg>[_nx * _ny];
            _polyCells = new List<Poly>[_nx * _ny];

            foreach (var (id, pts, closed) in list)
            {
                Count++;
                if (pts.Count == 1) { AddSeg(new Seg(id, pts[0][0], pts[0][1], pts[0][0], pts[0][1])); continue; }
                for (int i = 0; i + 1 < pts.Count; i++)
                    AddSeg(new Seg(id, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1]));
                bool isClosed = closed || (pts.Count > 3 && pts[0][0] == pts[^1][0] && pts[0][1] == pts[^1][1]);
                if (closed && pts.Count > 2)
                    AddSeg(new Seg(id, pts[^1][0], pts[^1][1], pts[0][0], pts[0][1]));
                if (isClosed && pts.Count > 2) AddPoly(id, pts);
            }
        }

        private int Ix(double x) => Math.Clamp((int)((x - _x0) / _cell), 0, _nx - 1);
        private int Iy(double y) => Math.Clamp((int)((y - _y0) / _cell), 0, _ny - 1);

        private void AddSeg(Seg s)
        {
            int ix0 = Ix(Math.Min(s.X0, s.X1)), ix1 = Ix(Math.Max(s.X0, s.X1));
            int iy0 = Iy(Math.Min(s.Y0, s.Y1)), iy1 = Iy(Math.Max(s.Y0, s.Y1));
            for (int iy = iy0; iy <= iy1; iy++)
                for (int ix = ix0; ix <= ix1; ix++)
                    (_segCells[iy * _nx + ix] ??= new List<Seg>()).Add(s);
        }

        private void AddPoly(string id, IReadOnlyList<double[]> pts)
        {
            var p = new Poly { Id = id, Pts = new double[pts.Count][], MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue };
            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                p.Pts[i] = pts[i];
                p.MinX = Math.Min(p.MinX, pts[i][0]); p.MaxX = Math.Max(p.MaxX, pts[i][0]);
                p.MinY = Math.Min(p.MinY, pts[i][1]); p.MaxY = Math.Max(p.MaxY, pts[i][1]);
                var q = pts[(i + 1) % pts.Count];
                area += pts[i][0] * q[1] - q[0] * pts[i][1];
            }
            p.Area = Math.Abs(area) / 2;
            for (int iy = Iy(p.MinY); iy <= Iy(p.MaxY); iy++)
                for (int ix = Ix(p.MinX); ix <= Ix(p.MaxX); ix++)
                    (_polyCells[iy * _nx + ix] ??= new List<Poly>()).Add(p);
        }

        /// (x, y)에서 tol 안의 가장 가까운 선분의 객체, 없으면 그 점을 감싸는 가장 작은 닫힌 도형의 객체.
        public string? Hit(double x, double y, double tol)
        {
            string? best = null;
            double bestD = tol;
            int ix0 = Ix(x - tol), ix1 = Ix(x + tol), iy0 = Iy(y - tol), iy1 = Iy(y + tol);
            for (int iy = iy0; iy <= iy1; iy++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    var cell = _segCells[iy * _nx + ix];
                    if (cell == null) continue;
                    foreach (var s in cell)
                    {
                        double d = DistToSeg(x, y, s.X0, s.Y0, s.X1, s.Y1);
                        if (d <= bestD) { bestD = d; best = s.Id; }
                    }
                }
            if (best != null) return best;
            var polys = _polyCells[Iy(y) * _nx + Ix(x)];
            if (polys == null) return null;
            Poly? inside = null;
            foreach (var p in polys)
                if (x >= p.MinX && x <= p.MaxX && y >= p.MinY && y <= p.MaxY && Contains(p.Pts, x, y)
                    && (inside == null || p.Area < inside.Area))
                    inside = p;
            return inside?.Id;
        }

        private static double DistToSeg(double px, double py, double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
            double t = len2 <= 0 ? 0 : Math.Clamp(((px - x0) * dx + (py - y0) * dy) / len2, 0, 1);
            double cx = x0 + t * dx - px, cy = y0 + t * dy - py;
            return Math.Sqrt(cx * cx + cy * cy);
        }

        private static bool Contains(double[][] pts, double x, double y)
        {
            bool c = false;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                if ((pts[i][1] > y) != (pts[j][1] > y) &&
                    x < (pts[j][0] - pts[i][0]) * (y - pts[i][1]) / (pts[j][1] - pts[i][1]) + pts[i][0])
                    c = !c;
            return c;
        }
    }
}
