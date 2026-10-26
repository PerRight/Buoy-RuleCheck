// 오브(부표) 기하 — 그리기와 무관한 순수 계산(System.Drawing·AutoCAD 의존 없음). 디자인 보드의 globe()를 그대로 옮겼다.
//   점 N개를 해바라기씨(피보나치) 배치로 구 위에 고르게, 축 기울기 18°.
//   앞으로 돌아 나오는 점은 크고 밝게, 뒤로 넘어가는 점은 작고 흐리게. 빛은 왼쪽 위에서. 아래(수선 아래)는 조금 흐리게.
//   자전은 왼쪽 → 오른쪽: 점의 경도 λ가 위상 θ = λ + 2π·turn 으로 돌고, x = a·sinθ, y = b·cosθ.
//   크기별 점 수: 20px 이하 24 · 40px 이하 36 · 96px 이하 80 · 그 위 160. 수선 고리는 48px 이상에서만(Sea).
// 같은 계산을 팔레트·로고(GDI+, OrbRenderer)와 도면 위 부표(Transient, BuoyLayer)가 함께 쓴다.
using System;
using System.Collections.Generic;

namespace GaRuleCheck
{
    /// 점 하나(오브 상자 왼쪽 위 기준 px). D = 지름, Alpha = 0..1.
    public readonly record struct OrbDot(double X, double Y, double D, double Alpha, double Depth);

    public sealed class OrbSpec
    {
        public double Size = 30;
        /// 점 수(없으면 크기별 기본)
        public int? Count;
        /// 점 지름 비율(없으면 크기별 기본)
        public double? Dot;
        /// 판정 색 점(도면 위 부표): 앞뒤 대비를 줄이고 몸통 그라데이션을 켠다
        public bool Vivid;
        /// 평면(깊이 없음) — 디자인 보드 01 '점으로 된 구'
        public bool Flat;
        /// 수선 고리(48px 이상 권장)
        public bool Sea;
        /// 몸통 그라데이션: null이면 Vivid일 때만
        public bool? Body;
        /// 부표 등(극점의 켜진 점) — 있으면 극점 근처 점(y > 0.93)은 뺀다
        public bool Lamp;
        public bool LampHollow;
        /// 빈 조각(경도 범위, 도): 확인 필요 = 280°–359°
        public double GapFrom = double.NaN, GapTo = double.NaN;

        public static int DefaultCount(double size) => size <= 20 ? 24 : size <= 40 ? 36 : size <= 96 ? 80 : 160;
        public static double DefaultDot(double size) => size <= 20 ? 0.13 : size <= 40 ? 0.105 : size <= 96 ? 0.066 : 0.037;
    }

    public sealed class OrbGeometry
    {
        private const double Tilt = 18 * Math.PI / 180;
        private readonly struct Seed
        {
            public readonly double Lam, A, B, Yc, S0, S1, O0, O1;
            public Seed(double lam, double a, double b, double yc, double s0, double s1, double o0, double o1)
            { Lam = lam; A = a; B = b; Yc = yc; S0 = s0; S1 = s1; O0 = o0; O1 = o1; }
        }

        private readonly Seed[] _seeds;
        private readonly double _l1;
        public OrbSpec Spec { get; }
        public double Size => Spec.Size;
        public double Center { get; }
        public double Radius { get; }
        /// 점 기본 지름(px)
        public double DotD { get; }
        public int Count => _seeds.Length;

        // 부표 등
        public double LampX { get; }
        public double LampY { get; }
        public double LampD { get; }
        // 수선 고리(타원 중심·반지름)
        public double SeaY { get; }
        public double SeaA { get; }
        public double SeaB { get; }
        public bool HasSea => Spec.Sea;
        // 몸통(방사 그라데이션): 반지름, 안쪽·바깥 알파, 빛 중심(상자 비율 36%·30%)
        public bool HasBody { get; }
        public double BodyR { get; }
        public double BodyIn { get; } = 0.12;
        public double BodyOut { get; } = 0.03;

        public OrbGeometry(OrbSpec spec)
        {
            Spec = spec;
            double size = spec.Size;
            int n = spec.Count ?? OrbSpec.DefaultCount(size);
            double c = size / 2, R = size * 0.4;
            double D = size * (spec.Dot ?? OrbSpec.DefaultDot(size));
            Center = c; Radius = R; DotD = D;
            bool vivid = spec.Vivid, flat = spec.Flat;
            double smin = flat ? 1 : vivid ? 0.45 : 0.3, omin = flat ? 0.6 : vivid ? 0.28 : 0.08;
            _l1 = flat ? 1 : vivid ? 0.86 : 0.76;
            double ga = Math.PI * (3 - Math.Sqrt(5));
            var seeds = new List<Seed>(n);
            for (int k = 0; k < n; k++)
            {
                double yv = 1 - 2 * (k + 0.5) / n;
                if (spec.Lamp && yv > 0.93) continue;
                double phi = Math.Asin(yv);
                double lamDeg = (k * ga * 180 / Math.PI) % 360;
                if (!double.IsNaN(spec.GapFrom) && lamDeg >= spec.GapFrom && lamDeg <= spec.GapTo) continue;
                double a = R * Math.Cos(phi), b = a * Math.Sin(Tilt), yc = -R * yv * Math.Cos(Tilt);
                double fF = (yv * Math.Sin(Tilt) + Math.Cos(phi) * Math.Cos(Tilt) + 1) / 2;
                double fB = (yv * Math.Sin(Tilt) - Math.Cos(phi) * Math.Cos(Tilt) + 1) / 2;
                double lightY = vivid ? 1 : 0.95 + 0.05 * yv;
                double below = yv < -0.3 ? (vivid ? 0.85 : 0.76) : 1;
                double s1 = smin + (1 - smin) * fF, s0 = smin + (1 - smin) * fB;
                double o1 = (omin + (1 - omin) * Math.Pow(fF, 1.2)) * lightY * below;
                double o0 = (omin + (1 - omin) * Math.Pow(fB, 1.2)) * lightY * below;
                seeds.Add(new Seed(lamDeg * Math.PI / 180, a, b, yc, s0, s1, o0, o1));
            }
            _seeds = seeds.ToArray();

            LampD = D * (size <= 40 ? 1.7 : 1.5);
            LampX = c;
            LampY = c - R * Math.Cos(Tilt);
            SeaY = c + 0.3 * R * Math.Cos(Tilt);
            SeaA = 0.954 * R;
            SeaB = SeaA * Math.Sin(Tilt);
            HasBody = spec.Body ?? vivid;
            BodyR = R + D * 0.35;
        }

        /// 회전 turn(바퀴, 0 = 쉬는 자세)의 점들. 뒤 → 앞 순서(앞 점이 위에 그려지게).
        public void Frame(double turn, List<OrbDot> dots)
        {
            dots.Clear();
            double c = Center, w = 2 * Math.PI * turn;
            foreach (var s in _seeds)
            {
                double th = s.Lam + w;
                double sin = Math.Sin(th), cos = Math.Cos(th);
                double kf = (cos + 1) / 2;
                double scale = s.S0 + (s.S1 - s.S0) * kf;
                double op = s.O0 + (s.O1 - s.O0) * kf;
                double lit = 1 - (1 - _l1) * (sin + 1) / 2;   // 빛은 왼쪽에서
                dots.Add(new OrbDot(c + s.A * sin, c + s.Yc + s.B * cos, DotD * scale, Math.Clamp(op * lit, 0, 1), kf));
            }
            dots.Sort((p, q) => p.Depth.CompareTo(q.Depth));
        }
    }

    /// 오브 움직임: 자전 위상(자세)을 유지한 채 속도만 바꾼다(0.4 s 가속 · 0.6 s 감속), 떠 있음(3.2 s · 위아래 1.5 px).
    public sealed class OrbMotion
    {
        /// 현재 자전 위상(바퀴)
        public double Turn { get; private set; }
        /// 한 바퀴 시간(초). 0이면 정지
        public double TargetPeriod { get; private set; }
        private double _speed;          // 바퀴/초
        private double _from, _to, _t0, _dur;
        private double _clock;

        public OrbMotion(double period, double startTurn = 0)
        {
            Turn = startTurn;
            TargetPeriod = period;
            _speed = _from = _to = period > 0 ? 1 / period : 0;
        }

        /// 속도 바꾸기. 빨라지면 0.4 s, 느려지면(멈춤 포함) 0.6 s에 걸쳐 — 자세는 그대로.
        public void SetPeriod(double period, bool immediate = false)
        {
            double target = period > 0 ? 1 / period : 0;
            TargetPeriod = period;
            if (immediate) { _speed = _from = _to = target; _dur = 0; return; }
            if (Math.Abs(target - _to) < 1e-9) return;
            _from = _speed;
            _to = target;
            _t0 = _clock;
            _dur = target > _speed ? 0.4 : 0.6;
        }

        public bool Stopped => _speed == 0 && _to == 0;

        /// dt초 진행. reducedMotion이면 위상을 움직이지 않는다(멈춘 자세, 색은 호출 쪽이 유지).
        public void Step(double dt, bool reducedMotion)
        {
            _clock += dt;
            if (_dur > 0)
            {
                double p = Math.Clamp((_clock - _t0) / _dur, 0, 1);
                double e = p * p * (3 - 2 * p);   // 부드러운 가감속(튀지 않게)
                _speed = _from + (_to - _from) * e;
                if (p >= 1) { _speed = _to; _dur = 0; }
            }
            if (!reducedMotion) Turn = (Turn + _speed * dt) % 1.0;
        }

        /// 떠 있음: 위아래 1.5 px, 3.2 s 반복(동작 줄이기면 0)
        public double Bob(bool reducedMotion) => reducedMotion ? 0 : 1.5 * Math.Sin(2 * Math.PI * _clock / 3.2);
        public double Clock => _clock;
    }

    /// CSS cubic-bezier 이징(등장·사라짐 (.32,.72,0,1), 호버 ease-out).
    public readonly struct CubicBezier
    {
        private readonly double _x1, _y1, _x2, _y2;
        public CubicBezier(double x1, double y1, double x2, double y2) { _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2; }
        public static readonly CubicBezier Appear = new(0.32, 0.72, 0, 1);
        public static readonly CubicBezier EaseOut = new(0, 0, 0.58, 1);

        private static double B(double a, double b, double t) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;

        public double Ease(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            double lo = 0, hi = 1, t = x;
            for (int i = 0; i < 40; i++)
            {
                t = (lo + hi) / 2;
                if (B(_x1, _x2, t) < x) lo = t; else hi = t;
            }
            return B(_y1, _y2, t);
        }
    }
}
