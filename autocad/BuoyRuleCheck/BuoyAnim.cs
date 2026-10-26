// 도면 위 부표 하나의 상태 흐름(명세 5절 '부표 상태 흐름') — 그리기와 무관한 순수 계산.
//   01 등장   700 ms · cubic-bezier(.32,.72,0,1) · 1.7배에서 모여들며, 물결은 처음 2번만(3.2 s · 2.4배)
//   02 대기   고칠 자리 위에서 천천히 자전 · 한 바퀴 5 s
//   03 호버   ×1.25 · 160 ms ease-out · 근거 선은 이때만(호출 쪽)
//   04 수정 중 명령 실행 동안 정지 · 투명도 35%
//   05 다시 검사 1.2 s 한 바퀴 · 최소 0.6 s 보여 줌
//   06 문제없음 파랑으로 바뀌며 정지 · 확인 대기
//   07 사라짐 확인(호버 후 이탈)하면 700 ms · 1.8배로 퍼지며
//   08 확인 필요 #FFB340 · 한 바퀴 8 s · 280°–359° 빈 조각
// 동작 줄이기: 회전·물결·크기 변화 없이 멈춘 자세, 상태 색·투명도는 유지.
using System;
using System.Collections.Generic;

namespace BuoyRuleCheck
{
    public enum BuoyPhase { Appear, Idle, Editing, Recheck, Pass, Vanish, Warn }

    public sealed class BuoyAnim
    {
        public BuoyPhase Phase { get; private set; }
        /// 판정 색 키: fail / warn / pass
        public string Status { get; private set; }
        public OrbMotion Motion { get; }
        public bool Hover { get; private set; }

        private double _t;               // 현재 단계에 들어온 뒤 시간(초)
        private double _hoverK, _hoverFrom, _hoverT = 1;
        private double _rippleClock;
        private double _recheckShown;    // 다시 검사를 보여 준 시간
        private string? _pendingStatus;  // 다시 검사 0.6 s를 채운 뒤 적용할 판정
        private double _colorT = 1;      // 상태 색 바뀜 진행(0 → 1, 0.3 s — 디자인 전달 '색 바뀜')
        /// 색이 바뀌는 중이면 바뀌기 전 상태(빨강 → 파랑을 0.3 s에 섞어 그린다)
        public string? FromStatus { get; private set; }
        /// 0 = 바뀌기 전 색, 1 = 지금 색(ease-out)
        public double ColorK => FromStatus == null ? 1 : 1 - Math.Pow(1 - _colorT, 3);
        public const double ColorSeconds = 0.3;

        private void ChangeStatus(string next)
        {
            if (next != Status) { FromStatus = Status; _colorT = 0; }
            Status = next;
        }

        public BuoyAnim(string status, bool appear = true, double startTurn = 0)
        {
            Status = status;
            Motion = new OrbMotion(PeriodFor(status), startTurn);
            Phase = appear ? BuoyPhase.Appear : Base(status);
            if (!appear) _rippleClock = OrbStates.RippleSeconds * OrbStates.RippleCount;
        }

        private static BuoyPhase Base(string status) => status switch
        {
            "pass" => BuoyPhase.Pass,
            "fail" => BuoyPhase.Idle,
            _ => BuoyPhase.Warn,
        };

        private static double PeriodFor(string status) => status switch
        {
            "pass" => 0,
            "fail" => OrbStates.BuoyIdle,
            _ => OrbStates.BuoyWarn,
        };

        public bool Gap => Status is not ("fail" or "pass");

        /// 다시 그려야 하는 중인가(돌고 있거나, 등장·사라짐·호버 전환·물결 중). 아니면 그리기 갱신을 건너뛴다.
        public bool IsAnimating(bool reduced)
        {
            if (Phase is BuoyPhase.Appear or BuoyPhase.Vanish || _hoverT < 1 || _colorT < 1) return true;
            if (_pendingStatus != null) return true;
            if (reduced) return false;
            return !Motion.Stopped || _rippleClock < OrbStates.RippleSeconds * OrbStates.RippleCount;
        }
        public bool Done => Phase == BuoyPhase.Vanish && _t * 1000 >= OrbStates.VanishMs;

        /// 명령 실행 시작(도면을 고치는 중): 멈추고 흐려진다
        public void BeginEdit()
        {
            if (Phase is BuoyPhase.Vanish) return;
            Enter(BuoyPhase.Editing);
            Motion.SetPeriod(0);
        }

        /// 명령이 끝나 다시 검사하는 동안: 빠르게 돈다(최소 0.6 s)
        public void BeginRecheck()
        {
            if (Phase is BuoyPhase.Vanish) return;
            Enter(BuoyPhase.Recheck);
            _recheckShown = 0;
            _pendingStatus = null;
            Motion.SetPeriod(OrbStates.BuoyRecheck);
        }

        /// 새 판정 도착. 같은 자리의 부표가 계속 살아 있으면 상태 색만 바꾼다(다시 검사 중이면 0.6 s를 채운 뒤).
        public void SetStatus(string status)
        {
            if (Phase == BuoyPhase.Recheck && _recheckShown < OrbStates.MinCheckSeconds)
            {
                _pendingStatus = status;   // 색은 다시 검사를 다 보여 준 뒤에 바꾼다
                return;
            }
            ChangeStatus(status);
            Enter(Base(status));
            Motion.SetPeriod(PeriodFor(status));
        }

        /// 문제없음을 확인(호버 후 이탈)하면 사라진다. 판정이 사라진 부표도 사라진다.
        public void Vanish()
        {
            if (Phase == BuoyPhase.Vanish) return;
            Enter(BuoyPhase.Vanish);
            Motion.SetPeriod(0);
        }

        public void SetHover(bool on)
        {
            if (Hover == on) return;
            Hover = on;
            _hoverFrom = _hoverK;
            _hoverT = 0;
        }

        private void Enter(BuoyPhase p)
        {
            Phase = p;
            _t = 0;
        }

        public void Step(double dt, bool reduced)
        {
            _t += dt;
            _rippleClock += dt;
            if (_colorT < 1) { _colorT = reduced ? 1 : Math.Min(1, _colorT + dt / ColorSeconds); if (_colorT >= 1) FromStatus = null; }
            Motion.Step(dt, reduced);
            if (Phase == BuoyPhase.Appear && _t * 1000 >= OrbStates.AppearMs) Enter(Base(Status));
            if (Phase == BuoyPhase.Recheck)
            {
                _recheckShown += dt;
                if (_pendingStatus is string next && _recheckShown >= OrbStates.MinCheckSeconds)
                {
                    _pendingStatus = null;
                    ChangeStatus(next);
                    Enter(Base(next));
                    Motion.SetPeriod(PeriodFor(next));
                }
            }
            if (_hoverT < 1)
            {
                _hoverT = Math.Min(1, _hoverT + dt * 1000 / OrbStates.HoverMs);
                double e = CubicBezier.EaseOut.Ease(_hoverT);
                _hoverK = _hoverFrom + ((Hover ? 1 : 0) - _hoverFrom) * e;
            }
            if (reduced) _hoverK = Hover ? 1 : 0;
        }

        /// 등장·사라짐·호버를 합친 크기 배율
        public double Scale(bool reduced)
        {
            double s = 1;
            if (!reduced)
            {
                if (Phase == BuoyPhase.Appear)
                    s = OrbStates.AppearScale + (1 - OrbStates.AppearScale) * CubicBezier.Appear.Ease(_t * 1000 / OrbStates.AppearMs);
                else if (Phase == BuoyPhase.Vanish)
                    s = 1 + (OrbStates.VanishScale - 1) * CubicBezier.Appear.Ease(_t * 1000 / OrbStates.VanishMs);
            }
            // 동작 줄이기면 호버해도 크기를 바꾸지 않는다(멈춘 자세 — 호버는 카드로만 알린다)
            return reduced ? s : s * (1 + (OrbStates.HoverScale - 1) * _hoverK);
        }

        /// 오브 투명도(1 = 불투명)
        public double Opacity(bool reduced)
        {
            double o = Phase == BuoyPhase.Editing ? OrbStates.EditingOpacity : 1;
            if (Phase == BuoyPhase.Appear && !reduced) o *= CubicBezier.Appear.Ease(_t * 1000 / OrbStates.AppearMs);
            if (Phase == BuoyPhase.Vanish) o *= reduced ? Math.Max(0, 1 - _t * 1000 / OrbStates.VanishMs) : 1 - CubicBezier.Appear.Ease(_t * 1000 / OrbStates.VanishMs);
            return Math.Clamp(o, 0, 1);
        }

        /// 지시선·고정점 투명도: 호버 1, 평소 0.75, 수정 중 0.35
        public double LeaderOpacity(bool reduced) =>
            Phase == BuoyPhase.Editing ? OrbStates.EditingOpacity : (Hover ? 1 : 0.75) * (Phase == BuoyPhase.Vanish ? Opacity(reduced) : 1);

        /// 물결(처음 2번만): (배율, 투명도) 목록. 지름 24 px 고리 기준.
        public IEnumerable<(double scale, double alpha)> Ripples(bool reduced)
        {
            if (reduced || Hover || Phase is BuoyPhase.Vanish or BuoyPhase.Editing) yield break;   // 호버·명령 중에는 물결 없음(디자인 전달 2장)
            double total = OrbStates.RippleSeconds * OrbStates.RippleCount;
            if (_rippleClock >= total) yield break;
            double p = (_rippleClock % OrbStates.RippleSeconds) / OrbStates.RippleSeconds;   // 0..1
            if (p > 0.7) yield break;
            double k = p / 0.7;
            double e = 1 - Math.Pow(1 - k, 2);   // ease-out
            yield return (1 + (OrbStates.RippleScale - 1) * e, 0.7 * (1 - k));
        }
    }
}
