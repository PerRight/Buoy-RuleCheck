// 도면 위 부표(명세 5절 '도면 위 부표'): 위반 후보가 있는 '고칠 자리'(엔진 element.anchor)에 오브가 떠서 자전하고, 고치면 사라진다.
//   - Transient(임시 그래픽) 하나에 모든 부표를 그린다 — 도면 DB를 바꾸지 않는다(Undo·저장에 남지 않음).
//     그리는 방법(2026-10-26 사용자 PC에서 부표가 안 보여 바꿈): .NET에서 Drawable을 직접 상속한 객체는 AutoCAD 쪽 실체가 없어
//     TransientManager가 받지 못한다(예외를 삼켜 조용히 안 그려졌음). 그래서 도면에 넣지 않은 무한선(Xline) 하나를 Transient로 올리고,
//     그 객체에만 걸리는 DrawableOverrule(BuoyOverrule)이 무한선 대신 부표를 그린다. 무한선은 범위가 무한이라 화면 밖으로 잘리지 않는다.
//   - 화면 크기 고정: ViewportDraw에서 그 뷰의 '1 단위 = 몇 픽셀'을 받아 30 px 오브·3 px 고정점·16 px 지시선으로 그린다(줌과 무관).
//   - 28 px 안에 겹치면 숫자로 묶는다. 커서를 올리면 ×1.25 + 카드(BuoyCard), 근거 선(시선 등)은 이때만(Overlay.ShowEvidence).
//   - 명령 실행 중에는 멈추고 흐려지며, 끝나면 빠르게 돌다가(다시 검사) 새 판정으로. 문제없음은 파랑으로 멈추고, 확인(호버 후 이탈)하면 사라진다.
//   - 동작 줄이기: 멈춘 자세, 색은 그대로.
// 판정은 하지 않는다: 어떤 부표를 어디에 어떤 색으로 띄울지는 엔진 결과(status·anchor·fix)를 그대로 따른다.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Exception = System.Exception;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace BuoyRuleCheck
{
    public static class BuoyLayer
    {
        internal sealed class Buoy
        {
            public string Key = "";
            public string RuleId = "";
            public string? RuleCondition;
            public string ElementId = "";
            public Point3d Anchor;               // 도면 단위
            public double[] AnchorM = Array.Empty<double>();
            public BuoyAnim Anim = null!;
            public ElementCheckDto El = null!;
            // 마지막 그림 기준(도면 단위)
            public Point3d Center;
            public Buoy? Leader;                  // 묶였으면 대표
            public int ClusterSize = 1;
        }

        private static readonly Dictionary<string, Buoy> Buoys = new();
        private static Xline? _host;              // 부표를 그릴 자리(Transient). 실제 무한선은 그리지 않는다(BuoyOverrule)
        private static BuoyOverrule? _overrule;
        private static Document? _doc;
        private static double _scale;            // 1 도면 단위 = _scale m
        private static bool _ticking;
        private static readonly Action<double> TickAction = OnTick;
        private static Buoy? _hover;
        private static BuoyCard? _card;
        private static readonly List<BuoyCard> Pinned = new();
        private static readonly List<OrbDot> Dots = new(64);

        /// 마지막으로 그린 뷰의 1픽셀 = 몇 도면 단위(맞히기 판정에 같은 값을 쓴다)
        internal static double LastUpp { get; private set; }

        /// '근거 ›' → 팔레트에서 그 항목 열기(규칙 ID, 객체 handle)
        public static event Action<string, string>? EvidenceRequested;
        /// 커서가 부표에 올라감/떠남 → 근거 선·대상 강조(규칙 ID, 객체 handle, 켜짐)
        public static event Action<string, string, bool>? EvidenceHover;

        public static bool Visible => UiState.Current.BuoysVisible;
        public static int Count => Buoys.Values.Count(b => b.Anim.Phase != BuoyPhase.Vanish);

        // ── 결과 반영 ────────────────────────────────────────────────────────────

        /// 새 판정이 오면: 같은 자리(규칙+객체)의 부표는 색만 바꾸고, 새 위반은 떠오르고, 사라진 위반은 퍼지며 사라진다.
        public static void Update(Document doc, CheckResponse resp)
        {
            if (_doc != doc) Clear();
            if (_doc == null)
            {
                _doc = doc;
                try { doc.ViewChanged += OnViewChanged; } catch { }
            }
            _scale = resp.UnitsScale ?? 0;
            var now = new Dictionary<string, (RuleResultDto r, ElementCheckDto e)>();
            foreach (var r in resp.Results)
                foreach (var e in r.Elements)
                    now[r.RuleId + "|" + e.ElementId] = (r, e);

            foreach (var (key, (r, e)) in now)
            {
                string st = e.Status == "fail" ? "fail" : e.Status == "pass" ? "pass" : "warn";   // 모르는 요소 상태는 확인 필요(문제없음 아님)
                bool hasAnchor = e.Anchor is { Length: >= 2 } && _scale > 0;
                if (Buoys.TryGetValue(key, out var b))
                {
                    b.El = e;
                    b.RuleCondition = r.Condition;
                    if (hasAnchor) SetAnchor(b, e.Anchor!);
                    if (b.Anim.Phase == BuoyPhase.Vanish)
                    {
                        if (st != "pass") b.Anim = new BuoyAnim(st, appear: true, startTurn: Seed(key));
                        continue;
                    }
                    b.Anim.SetStatus(st);   // 위치가 없어진 '확인 필요'(입력이 빔)도 원래 자리에서 빈 조각으로 돈다
                }
                else if (st != "pass" && hasAnchor)
                {
                    var nb = new Buoy
                    {
                        Key = key, RuleId = r.RuleId, RuleCondition = r.Condition, ElementId = e.ElementId, El = e,
                        Anim = new BuoyAnim(st, appear: true, startTurn: Seed(key)),
                    };
                    SetAnchor(nb, e.Anchor!);
                    Buoys[key] = nb;
                }
            }
            // 객체가 지워졌거나 규칙이 더는 그 객체를 보지 않음(디자인 전달 5장 표):
            //   규칙을 판정할 수 있으면 파랑 '문제없음'(확인 대기 — 올렸다 떼거나 저장하면 사라짐, /v1/compare의 '해결'과 같은 기준)
            //   판정할 수 없게 됐으면(입력 누락·검토 불가) 주황 '확인 필요', 적용 제외·규칙 없음이면 사라짐
            foreach (var b in Buoys.Values)
            {
                if (now.ContainsKey(b.Key) || b.Anim.Phase is BuoyPhase.Vanish) continue;
                var rs = resp.Results.FirstOrDefault(x => x.RuleId == b.RuleId)?.Status;
                // SetStatus는 같은 판정이 다시 와도 안전하고, 보류 중인 옛 판정보다 이 최신 판정이 이긴다(현재 색과 비교해 건너뛰지 않는다)
                if (rs is "pass" or "fail") { b.Anim.SetStatus("pass"); b.El = Gone(b.El, "pass"); }
                else if (rs is "na" or "review") { b.Anim.SetStatus("warn"); b.El = Gone(b.El, "unknown"); }
                else b.Anim.Vanish();
            }
            // 커서를 올려 둔 채 새 판정이 오면 카드도 새 판정으로(고정한 카드는 그때 모습 그대로 둔다)
            if (_hover != null && _card is { Pinned: false })
            {
                if (Buoys.ContainsValue(_hover) && _hover.Anim.Phase != BuoyPhase.Vanish) Guard.Run("부표 카드", () => ShowCard(_hover));
                else Leave();
            }
            Ensure();
            Redraw();
        }

        /// 없어진 객체의 카드 내용: 옛 '고칠 내용·현재·기준·차이'를 보이지 않게(그 판정은 더는 맞지 않음). 자리·이름만 남긴다.
        private static ElementCheckDto Gone(ElementCheckDto e, string status) =>
            (status == "pass" ? e.Guide == UiText.ElementGonePass : e.Fix == null && e.Status == status) ? e : new ElementCheckDto
        {
            ElementId = e.ElementId, RuleId = e.RuleId, RuleName = e.RuleName, Role = e.Role, RoleKo = e.RoleKo,
            Status = status, Required = e.Required, Actual = "", Anchor = e.Anchor, Fix = null,
            Guide = status == "pass" ? UiText.ElementGonePass : UiText.ElementGoneWarn,
        };

        private static void SetAnchor(Buoy b, double[] a)
        {
            b.AnchorM = a;
            b.Anchor = new Point3d(a[0] / _scale, a[1] / _scale, 0);
        }

        /// 부표마다 자전 시작 자세를 다르게(같은 박자로 도는 어색함 방지)
        private static double Seed(string key) => (uint)key.GetHashCode() % 1000 / 1000.0;

        /// 검사 실패(엔진 연결 끊김 등): 판정하지 않으므로 부표를 모두 내린다(로고가 '연결 끊김'으로 알린다).
        public static void Clear()
        {
            // 부표부터 내린다 — 카드·근거 정리가 던져도 부표가 화면에 남지 않게(2026-10-26 검증 N8)
            var hover = _hover;
            _hover = null;
            _hoverFromObject = false;
            Buoys.Clear();
            RemoveHost();
            StopTick();
            if (_doc != null) { try { _doc.ViewChanged -= OnViewChanged; } catch { } }
            _doc = null;
            Guard.Run("부표 카드 닫기", () => CloseCard(force: true));
            foreach (var c in Pinned.ToList()) Guard.Run("부표 카드 닫기", c.Close);
            Pinned.Clear();
            if (hover != null) Guard.Run("근거 선 끄기", () => EvidenceHover?.Invoke(hover.RuleId, hover.ElementId, false));
        }

        /// 부표 표시 켜기/끄기(판정은 계속)
        public static void SetVisible(bool on)
        {
            UiState.Current.BuoysVisible = on;
            UiState.Current.Save();
            if (!on) CloseCard(force: true);
            Redraw();
        }

        // ── 명령·검사 흐름 ───────────────────────────────────────────────────────

        private static readonly HashSet<string> ViewCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "ZOOM", "PAN", "REGEN", "REGENALL", "REDRAW", "REDRAWALL", "3DORBIT", "VIEW", "-VIEW", "DSVIEWER", "VIEWRES",
            "QSAVE", "SAVE", "SAVEAS", "PROPERTIES", "PROPERTIESCLOSE", "QUICKPROPERTIES", "LIST", "DIST", "ID", "MEASUREGEOM", "HELP",
        };

        /// 명령 시작: 부표는 멈추고 흐려진다(04 수정 중). 화면 이동·저장·조회 명령과 이 플러그인 명령은 제외.
        public static void CommandStarted(Document doc, string globalName)
        {
            if (doc != _doc || Buoys.Count == 0) return;
            if (ViewCommands.Contains(globalName) || globalName.StartsWith("GARC", StringComparison.OrdinalIgnoreCase)
                || globalName.StartsWith("BRC", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var b in Buoys.Values) b.Anim.BeginEdit();
            CloseCard(force: false);
            Ensure();
            Redraw();
        }

        /// 명령 끝: 도면이 바뀌었으면 빠르게 돈다(05 다시 검사), 아니면 원래 상태로.
        public static void CommandFinished(Document doc, bool willRecheck)
        {
            if (doc != _doc || Buoys.Count == 0) return;
            foreach (var b in Buoys.Values)
            {
                if (b.Anim.Phase != BuoyPhase.Editing) continue;
                if (willRecheck) b.Anim.BeginRecheck();
                else b.Anim.SetStatus(b.Anim.Status);
            }
            Ensure();
            Redraw();
        }

        /// 검사를 시작함(명령 없이 특성 창 등으로 바뀐 경우 포함)
        public static void CheckStarting(Document doc)
        {
            if (doc != _doc) return;
            foreach (var b in Buoys.Values)
                if (b.Anim.Phase is BuoyPhase.Editing or BuoyPhase.Idle or BuoyPhase.Warn) b.Anim.BeginRecheck();
            Ensure();
            Redraw();
        }

        // ── 애니메이션 박자 ─────────────────────────────────────────────────────

        private static void Ensure()
        {
            if (_doc == null || Buoys.Count == 0) { StopTick(); return; }
            if (_host == null) AddHost();   // 못 올려도 박자는 돈다(카드 닫기·사라짐 정리는 계속)
            if (!_ticking) { OrbClock.Add(TickAction); _ticking = true; }
        }

        /// 부표 Transient를 올린다. 실패하면 명령줄·error.log로 알리고(조용히 안 보이지 않게) false.
        private static bool AddHost()
        {
            if (_hostFailed) return false;   // 같은 실패를 매 틱 되풀이하지 않게(도면이 바뀌면 다시 시도 — Clear)
            Xline? host = null;
            try
            {
                if (_overrule == null)
                {
                    var ov = new BuoyOverrule();
                    Overrule.AddOverrule(RXObject.GetClass(typeof(Xline)), ov, false);
                    _overrule = ov;           // 아래가 실패해도 두 번 등록하지 않게
                    ov.SetCustomFilter();     // IsApplicable로 이 플러그인의 무한선 하나에만 걸린다(그리기에서도 한 번 더 확인)
                }
                Overrule.Overruling = true;
                host = new Xline { BasePoint = Point3d.Origin, UnitDir = Vector3d.XAxis };
                BuoyOverrule.Host = host.UnmanagedObject;
                bool ok = TransientManager.CurrentTransientManager.AddTransient(host, TransientDrawingMode.DirectTopmost, 140, new IntegerCollection());
                if (!ok) throw new InvalidOperationException("TransientManager.AddTransient가 false를 돌려줌");   // catch가 포인터를 지우고 숨겨 둠
                _host = host;
                return true;
            }
            catch (Exception ex)
            {
                // 포인터를 남기면, 이 무한선이 해제된 뒤 같은 주소에 생긴 사용자 XLINE이 숨겨질 수 있다(2026-10-26 검증 M4)
                BuoyOverrule.Host = IntPtr.Zero;
                if (host != null) Retire(host);
                _hostFailed = true;
                Guard.Report("부표 표시 준비", ex);
                return false;
            }
        }

        /// 올렸는지 모르는(또는 지우지 못한) 무한선: Dispose하면 AutoCAD가 해제된 객체를 그릴 수 있어 살려 두고,
        /// 오버룰이 아무것도 그리지 않게 한다(무한선이 화면에 나타나지 않게).
        private static readonly List<Xline> Retired = new();
        private static void Retire(Xline x)
        {
            try { BuoyOverrule.Hidden.Add(x.UnmanagedObject); } catch { }
            Retired.Add(x);
        }

        private static bool _hostFailed;

        private static void RemoveHost()
        {
            _hostFailed = false;
            BuoyOverrule.Host = IntPtr.Zero;
            if (_host == null) return;
            var h = _host;
            _host = null;
            bool erased = false;
            try { erased = TransientManager.CurrentTransientManager.EraseTransient(h, new IntegerCollection()); }
            catch (Exception ex) { Guard.Report("부표 표시 지우기", ex); }
            if (erased) { try { h.Dispose(); } catch { } }
            else Retire(h);   // 지우지 못했으면 해제하지 않고 숨긴다(L3)
        }

        /// 플러그인 종료: 부표를 내리고 오버룰을 뗀다(다시 NETLOAD해도 두 번 걸리지 않게).
        public static void Shutdown()
        {
            Clear();
            if (_overrule == null) return;
            if (Retired.Count > 0) return;   // 지우지 못한 무한선이 남아 있으면 오버룰을 떼는 순간 화면에 나타난다(검증 N7)
            try { Overrule.RemoveOverrule(RXObject.GetClass(typeof(Xline)), _overrule); } catch { }
            _overrule = null;
        }

        private static void StopTick()
        {
            if (_ticking) { OrbClock.Remove(TickAction); _ticking = false; }
        }

        private static void OnTick(double dt) => Guard.Run("부표 움직임", () => Tick(dt));

        /// 그리기 콜백 안에서는 명령줄에 쓰지 않는다 — 다음 박자에서 알린다(2026-10-26 검증 L9)
        private static Exception? _drawError;
        internal static void DrawFailed(Exception ex) { _drawError ??= ex; }

        private static void Tick(double dt)
        {
            if (_drawError is { } de) { _drawError = null; Guard.Report("부표 그리기", de); }
            if (_doc == null || AcApp.DocumentManager.MdiActiveDocument != _doc) return;
            bool red = Motion.Reduced;
            bool animating = false;
            // 자전은 급한 순서로 12개까지만(디자인 전달 2장 '부담') — 나머지는 멈춘 자세(등장·사라짐·색은 그대로 진행)
            int n = 0;
            foreach (var b in Buoys.Values.OrderBy(x => Rank(x.Anim.Status)).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                bool still = red || (b.Anim.Phase is not (BuoyPhase.Appear or BuoyPhase.Vanish) && n >= MaxSpinning);
                if (!still && b.Anim.Phase is not BuoyPhase.Vanish) n++;
                b.Anim.Step(dt, still);
                animating |= b.Anim.IsAnimating(still);
            }
            var gone = Buoys.Values.Where(b => b.Anim.Done).Select(b => b.Key).ToList();
            foreach (var k in gone) Buoys.Remove(k);
            CheckHoverStillInside();
            if (animating || gone.Count > 0) Redraw();
            if (Buoys.Count == 0)
            {
                Redraw();
                StopTick();
            }
        }

        /// 줌·팬: 화면 크기 고정 부표를 그 배율로 다시 그린다(멈춰 있는 부표도). 카드는 자리가 어긋나므로 닫는다.
        private static void OnViewChanged(object? sender, EventArgs e) => Guard.Run("화면 이동", () =>
        {
            if (_card is { Pinned: false }) CloseCard(force: false);
            Redraw();
        });

        /// 저장: 확인 대기 중인 문제없음 부표를 퍼지며 사라지게(디자인 전달 5장)
        public static void ClearResolved(Document doc)
        {
            if (doc != _doc) return;
            foreach (var b in Buoys.Values) if (b.Anim.Phase == BuoyPhase.Pass) b.Anim.Vanish();
            Ensure();
            Redraw();
        }

        private static void Redraw()
        {
            if (_host == null) return;
            try { TransientManager.CurrentTransientManager.UpdateTransient(_host, new IntegerCollection()); }
            catch (Exception ex) { Guard.Report("부표 다시 그리기", ex); }
        }

        // ── 그리기(뷰마다) ───────────────────────────────────────────────────────

        private static long _drawCalls, _drawn;

        /// BRCHEALTH용 한 줄: 부표가 안 보일 때 어디서 멈췄는지(올림 실패·그리기 호출 없음·배율 못 구함·표시 꺼짐)
        public static string Diagnose() =>
            $"부표 {Count}개 · 표시 {(Visible ? "켜짐" : "꺼짐")} · 그릴 자리 {(_host != null ? "있음" : _hostFailed ? "올리지 못함" : "없음")}"
            + $" · 오버룰 적용 {BuoyOverrule.Applied}회 · 그리기 호출 {_drawCalls}회(그림 {_drawn}회)" + (LastUpp > 0 ? $" · 1 px = {LastUpp:g4} 도면 단위" : "");

        internal static void Draw(ViewportDraw vd)
        {
            _drawCalls++;
            if (!Visible || Buoys.Count == 0 || _doc == null) return;
            double upp;
            try
            {
                var any = Buoys.Values.First().Anchor;
                var px = vd.Viewport.GetNumPixelsInUnitSquare(any);
                upp = px.X > 1e-12 ? 1.0 / px.X : 0;
            }
            catch (Exception ex) { DrawFailed(ex); return; }
            if (upp <= 0 || double.IsNaN(upp)) return;
            LastUpp = upp;
            _drawn++;
            bool red = Motion.Reduced;
            foreach (var b in Layout(upp)) DrawBuoy(vd, b, upp, red);
        }

        /// 그 배율(1 px = upp 도면 단위)에서 오브 중심·묶음을 정한다. 뷰마다 배율이 다르므로 그리기와 커서 판정이 각자 부른다.
        /// 묶음: 위반 → 확인 필요 → 문제없음 순으로 대표를 정하고, 28 px 안의 부표를 그 대표에 묶는다(사라지는 부표는 묶지 않음).
        private static List<Buoy> Layout(double upp)
        {
            var order = Buoys.Values.OrderBy(b => b.Anim.Phase == BuoyPhase.Vanish ? 1 : 0)
                                    .ThenBy(b => Rank(b.Anim.Status)).ThenBy(b => b.Key, StringComparer.Ordinal).ToList();
            var leaders = new List<Buoy>();
            double off = BuoyPainter.CenterOffset * upp, near = OrbStates.ClusterPx * upp;
            foreach (var b in order)
            {
                b.Center = new Point3d(b.Anchor.X + off, b.Anchor.Y + off, 0);
                b.Leader = null;
                b.ClusterSize = 1;
                if (b.Anim.Phase != BuoyPhase.Vanish)
                {
                    var l = leaders.FirstOrDefault(x => x.Anim.Phase != BuoyPhase.Vanish && x.Center.DistanceTo(b.Center) <= near);
                    if (l != null) { b.Leader = l; l.ClusterSize++; continue; }
                }
                leaders.Add(b);
            }
            return leaders;
        }

        /// 커서가 있는 뷰(현재 뷰포트)의 배율. 타일 뷰포트가 여럿이면 마지막으로 그린 뷰의 배율(LastUpp)과 다를 수 있어
        /// 커서 판정은 현재 뷰로 한다(2026-10-26 검증 의심). 100 ms 동안 기억, 못 구하면 0.
        private static double _ptrUpp;
        private static DateTime _ptrUppAt;
        private static double PointerUpp(Document doc)
        {
            var now = DateTime.UtcNow;
            if (_ptrUpp > 0 && (now - _ptrUppAt).TotalMilliseconds < 100) return _ptrUpp;
            try
            {
                using var view = doc.Editor.GetCurrentView();
                var ss = (Point2d)AcApp.GetSystemVariable("SCREENSIZE");
                double upp = ss.Y >= 1 ? view.Height / ss.Y : 0;
                _ptrUpp = upp > 0 && !double.IsNaN(upp) && !double.IsInfinity(upp) ? upp : 0;
            }
            catch { _ptrUpp = 0; }
            _ptrUppAt = now;
            return _ptrUpp;
        }

        private const int MaxSpinning = 12;

        private static int Rank(string status) => status switch { "fail" => 0, "warn" => 1, _ => 2 };

        private static EntityColor Ec(System.Drawing.Color c) => new(c.R, c.G, c.B);
        private static Transparency Tr(double alpha) => new((byte)Math.Clamp((int)Math.Round(alpha * 255), 0, 255));

        private static void DrawBuoy(ViewportDraw vd, Buoy b, double upp, bool red)
        {
            var a = b.Anim;
            var col = a.FromStatus is string from ? BuoyPainter.Mix(BuoyPainter.ColorOf(from), BuoyPainter.ColorOf(a.Status), a.ColorK) : BuoyPainter.ColorOf(a.Status);
            var ec = Ec(col);
            var tr = vd.SubEntityTraits;
            var geo = vd.Geometry;
            var n = Vector3d.ZAxis;
            double lead = a.LeaderOpacity(red);

            tr.TrueColor = ec;
            // 고정점 3 px
            tr.FillType = FillType.FillAlways;
            tr.Transparency = Tr(Math.Min(1, lead / 0.75));
            geo.Circle(b.Anchor, OrbStates.AnchorDot / 2 * upp, n);
            // 지시선 16 px · 45°
            tr.FillType = FillType.FillNever;
            tr.Transparency = Tr(lead);
            double d = OrbStates.LeaderLen / Math.Sqrt(2) * upp;
            geo.WorldLine(new Point3d(b.Anchor.X + upp, b.Anchor.Y + upp, 0), new Point3d(b.Anchor.X + d, b.Anchor.Y + d, 0));

            double bob = (a.Hover || a.Phase != BuoyPhase.Idle ? 0 : a.Motion.Bob(red)) * upp;   // 떠 있음은 수정 소요 대기 중에만, 호버하면 멈춤(디자인 전달 2장)
            var c = new Point3d(b.Center.X, b.Center.Y - bob, 0);
            // 물결(처음 2번)
            foreach (var (s, al) in a.Ripples(red))
            {
                tr.Transparency = Tr(al);
                geo.Circle(c, 12 * s * upp, n);
            }
            // 오브(점 36개 · 판정 색 · 앞 점이 크고 밝게)
            var g = BuoyPainter.GeometryFor(a.Gap);
            double sc = a.Scale(red), op = a.Opacity(red), half = g.Size / 2;
            g.Frame(a.Motion.Turn, Dots);
            tr.FillType = FillType.FillAlways;
            if (g.HasBody)
            {
                // 몸통 그라데이션은 Transient로 못 그린다 → 옅은 원 두 겹으로 비슷하게
                tr.Transparency = Tr(g.BodyOut * 2 * op);
                geo.Circle(c, g.BodyR * sc * upp, n);
                tr.Transparency = Tr(g.BodyIn * 0.6 * op);
                geo.Circle(new Point3d(c.X - g.BodyR * 0.18 * sc * upp, c.Y + g.BodyR * 0.25 * sc * upp, 0), g.BodyR * 0.55 * sc * upp, n);
            }
            foreach (var dot in Dots)
            {
                double al = dot.Alpha * op;
                if (al <= 0.01) continue;
                tr.Transparency = Tr(al);
                geo.Circle(new Point3d(c.X + (dot.X - half) * sc * upp, c.Y - (dot.Y - half) * sc * upp, 0), Math.Max(dot.D * sc / 2, 0.35) * upp, n);
            }
            // 묶음 숫자
            if (b.ClusterSize > 1)
            {
                var cc = new Point3d(c.X + 17 * upp, c.Y + 15 * upp, 0);
                tr.Transparency = Tr(1);
                geo.Circle(cc, 9 * upp, n);
                tr.TrueColor = Ec(Theme.Bg);
                string t = b.ClusterSize > 99 ? "99+" : b.ClusterSize.ToString();
                double h = 8.5 * upp;
                geo.Text(new Point3d(cc.X - t.Length * h * 0.36, cc.Y - h / 2, 0), n, Vector3d.XAxis, h, 0.85, 0, t);
            }
        }

        // ── 커서(DocWatcher.PointMonitor에서) ────────────────────────────────────

        /// 커서 아래 부표를 찾고 호버 상태·카드를 맞춘다. 부표 위면 true(객체 툴팁과 겹치지 않게).
        public static bool Pointer(Document doc, Point3d world)
        {
            if (doc != _doc || !Visible || Buoys.Count == 0) { Leave(); return false; }
            double upp = PointerUpp(doc);
            if (upp <= 0) upp = LastUpp;
            // 배율을 못 구함(아직 한 번도 못 그림 등): 객체 위 호버 카드는 닫지 않는다 — 전에는 여기서 매번 닫혀 카드가 깜박였다
            if (upp <= 0) { if (!_hoverFromObject) Leave(); return false; }
            Layout(upp);   // 커서가 있는 뷰의 배율로 중심·묶음을 다시(다른 뷰포트를 마지막에 그렸어도 맞게)
            Buoy? hit = null;
            double best = double.MaxValue;
            foreach (var b in Buoys.Values)
            {
                if (b.Leader != null || b.Anim.Phase == BuoyPhase.Vanish) continue;
                double r = OrbStates.HitRadius * upp * (b.Anim.Hover && !Motion.Reduced ? OrbStates.HoverScale : 1);
                double dist = b.Center.DistanceTo(new Point3d(world.X, world.Y, 0));
                if (dist <= r && dist < best) { best = dist; hit = b; }
            }
            if (hit == _hover) { if (hit != null) _card?.StayOpen(); return hit != null; }
            if (hit == null && _hoverFromObject) return false;   // 객체 위 호버는 HoverObject가 끝낸다
            Leave();
            if (hit == null) return false;
            _hover = hit;
            _hoverFromObject = false;
            hit.Anim.SetHover(true);
            EvidenceHover?.Invoke(hit.RuleId, hit.ElementId, true);
            ShowCard(hit);
            Ensure();
            return true;
        }

        /// 도면 객체 위 호버(디자인 전달 3장): 그 객체에 부표가 있으면 부표에 올린 것과 같은 카드·근거를 연다.
        /// handle = 커서 아래 판정 대상 객체(없으면 null). 부표가 없는 객체(문제없음 등)는 카드 없음.
        public static void HoverObject(Document doc, string? handle)
        {
            Buoy? hit = null;
            if (doc == _doc && Visible && handle != null)
            {
                string bh = Extractor.BaseHandle(handle);
                hit = Buoys.Values
                    .Where(b => b.Anim.Phase != BuoyPhase.Vanish && (b.ElementId == handle || Extractor.BaseHandle(b.ElementId) == bh))
                    .OrderBy(b => Rank(b.Anim.Status)).FirstOrDefault();
                if (hit?.Leader != null) hit = hit.Leader;
            }
            if (hit == null)
            {
                if (_hoverFromObject) Leave();
                return;
            }
            if (hit == _hover) { _card?.StayOpen(); return; }
            Leave();
            _hover = hit;
            _hoverFromObject = true;
            hit.Anim.SetHover(true);
            EvidenceHover?.Invoke(hit.RuleId, hit.ElementId, true);
            ShowCard(hit);
            Ensure();
        }

        private static bool _hoverFromObject;

        private static void Leave()
        {
            _hoverFromObject = false;
            if (_hover == null) return;
            var b = _hover;
            _hover = null;
            b.Anim.SetHover(false);
            EvidenceHover?.Invoke(b.RuleId, b.ElementId, false);
            _card?.LeaveSoon();
            // 문제없음을 확인했으면(올렸다 뗌) 퍼지며 사라진다(07)
            if (b.Anim.Phase == BuoyPhase.Pass)
                foreach (var m in Members(b)) if (m.Anim.Phase == BuoyPhase.Pass) m.Anim.Vanish();
            Ensure();
        }

        /// 커서가 도면 창 밖으로 나가면(PointMonitor가 더 오지 않음) 호버를 끝낸다.
        private static void CheckHoverStillInside()
        {
            if (_hover == null || _doc == null) return;
            try
            {
                if (_card is { MouseInside: true }) return;
                if (!GetWindowRect(_doc.Window.Handle, out var r)) return;
                var p = System.Windows.Forms.Cursor.Position;
                if (p.X < r.Left || p.X > r.Right || p.Y < r.Top || p.Y > r.Bottom) Leave();
            }
            catch { }
        }

        private static IEnumerable<Buoy> Members(Buoy leader) =>
            Buoys.Values.Where(x => x == leader || x.Leader == leader);

        private static void ShowCard(Buoy b)
        {
            // 카드는 '최종' 판정(Target)으로 — 다시 검사 0.6 s 동안 부표 색은 옛 색이어도 카드 내용(El)은 이미 새 판정이다(검증 N1)
            var items = Members(b).OrderBy(x => Rank(x.Anim.Target))
                .Select(x => new BuoyCardItem(x.Anim.Target, x.RuleId, x.RuleCondition, x.El)).ToList();
            CloseCard(force: false);
            var card = new BuoyCard(items);
            card.EvidenceRequested += it => EvidenceRequested?.Invoke(it.RuleId, it.Element.ElementId);
            card.ZoomRequested += list => ZoomTo(list);
            card.Closed2 += c =>
            {
                Pinned.Remove(c);
                if (_card == c) _card = null;
            };
            _card = card;
            try { card.ShowNear(System.Windows.Forms.Cursor.Position, new Owner(AcApp.MainWindow.Handle)); } catch { }
        }

        /// 지금 카드를 닫는다. 핀으로 고정한 카드는 남겨 두고(목록에 옮김) 새 카드를 띄울 수 있게.
        private static void CloseCard(bool force)
        {
            if (_card == null) return;
            if (_card.Pinned && !force) { Pinned.Add(_card); _card = null; return; }
            try { _card.Close(); } catch { }
            _card = null;
        }

        /// 묶음 [맞춰 확대]·목록 한 줄: 부표 자리가 모두 들어오게 화면 이동(도면은 그대로).
        public static void ZoomTo(IReadOnlyList<BuoyCardItem> items)
        {
            if (_doc == null || _scale <= 0) return;
            var pts = items.Select(i => i.Element.Anchor).Where(a => a is { Length: >= 2 }).Select(a => a!).ToList();
            if (pts.Count == 0) return;
            double pad = 4;   // m
            var box = new[] { pts.Min(p => p[0]) - pad, pts.Min(p => p[1]) - pad, pts.Max(p => p[0]) + pad, pts.Max(p => p[1]) + pad };
            try { using (_doc.LockDocument()) Overlay.FocusBox(_doc, box, _scale); } catch { }
        }

        /// 팔레트·로고 목록에서 연다(명세 6-1): 그 항목의 고칠 자리로 화면을 옮기고 부표 카드를 고정해 띄운다.
        /// 자리(anchor)가 없으면 false — 호출 쪽이 대상 객체로 이동한다.
        public static bool OpenFromList(Document doc, double scale, RuleResultDto r, ElementCheckDto? e)
        {
            e ??= UiText.Decisive(r);
            if (e?.Anchor is not { Length: >= 2 } a || scale <= 0) return false;
            const double pad = 8;   // m — 고칠 자리 주변이 함께 보이게
            try { using (doc.LockDocument()) Overlay.FocusBox(doc, new[] { a[0] - pad, a[1] - pad, a[0] + pad, a[1] + pad }, scale); } catch { }
            string st = e.Status == "fail" ? "fail" : e.Status == "pass" ? "pass" : "warn";   // 모르는 요소 상태는 확인 필요(문제없음 아님)
            var card = new BuoyCard(new List<BuoyCardItem> { new(st, r.RuleId, r.Condition, e) });
            card.EvidenceRequested += it => EvidenceRequested?.Invoke(it.RuleId, it.Element.ElementId);
            card.ZoomRequested += list => ZoomTo(list);
            card.Closed2 += c => { Pinned.Remove(c); if (_card == c) _card = null; };
            card.Pinned2();
            Pinned.Add(card);
            try { card.ShowNear(System.Windows.Forms.Cursor.Position, new Owner(AcApp.MainWindow.Handle)); } catch { }
            return true;
        }

        /// 팔레트 카드에 커서를 올린 동안 그 항목의 부표를 키운다(×1.25, 카드는 띄우지 않음).
        public static void Highlight(string ruleId, bool on)
        {
            bool any = false;
            foreach (var b in Buoys.Values)
            {
                if (b.RuleId != ruleId || b == _hover || b.Anim.Phase == BuoyPhase.Vanish) continue;
                b.Anim.SetHover(on);
                any = true;
            }
            if (any) Ensure();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        private sealed class Owner : System.Windows.Forms.IWin32Window
        {
            public Owner(IntPtr h) { Handle = h; }
            public IntPtr Handle { get; }
        }
    }

    /// 부표 전체를 그린다: BuoyLayer의 무한선(Transient) 하나에만 걸려서, 무한선 대신 뷰마다(ViewportDraw) 픽셀 크기를 받아 화면 크기 고정으로 그린다.
    /// 다른 무한선(도면의 XLINE·다른 플러그인)은 IsApplicable이 false라 그대로 그려진다.
    internal sealed class BuoyOverrule : DrawableOverrule
    {
        /// 부표용 무한선의 AutoCAD 쪽 포인터(없으면 0)
        internal static IntPtr Host;
        /// 지우지 못해 살려 둔 옛 무한선 — 아무것도 그리지 않는다
        internal static readonly HashSet<IntPtr> Hidden = new();
        /// 진단(BRCHEALTH): 부표 무한선에 오버룰이 걸린 횟수
        internal static long Applied;

        public override bool IsApplicable(RXObject overruledSubject)
        {
            try
            {
                var p = overruledSubject.UnmanagedObject;
                if (p == IntPtr.Zero) return false;
                if (p == Host) { Applied++; return true; }
                return Hidden.Contains(p);
            }
            catch { return false; }
        }

        public override int SetAttributes(Drawable drawable, DrawableTraits traits)
        {
            int a = 0;
            try { a = base.SetAttributes(drawable, traits); } catch { }
            return IsHost(drawable) ? a | (int)DrawableAttributes.ViewDependentViewportDraw : a;   // 줌마다 다시 그림(화면 크기 고정)
        }

        /// 무한선 자체는 그리지 않는다. false → 뷰마다 ViewportDraw.
        /// 필터가 안 걸린 경우(다른 무한선이 들어옴)에도 도면의 XLINE이 사라지지 않게 한 번 더 확인하고 원래대로 그린다.
        public override bool WorldDraw(Drawable drawable, WorldDraw wd)
        {
            if (IsHidden(drawable)) return true;   // 옛 무한선: 아무것도 그리지 않음
            return !IsHost(drawable) && base.WorldDraw(drawable, wd);
        }

        public override void ViewportDraw(Drawable drawable, ViewportDraw vd)
        {
            if (IsHidden(drawable)) return;
            if (!IsHost(drawable)) { base.ViewportDraw(drawable, vd); return; }
            try { BuoyLayer.Draw(vd); }
            catch (Exception ex) { BuoyLayer.DrawFailed(ex); }   // 그리기 실패가 AutoCAD를 멈추게 하지 않게, 대신 다음 박자에서 기록
        }

        private static bool IsHost(Drawable d)
        {
            try { return Host != IntPtr.Zero && d.UnmanagedObject == Host; }
            catch { return false; }
        }

        private static bool IsHidden(Drawable d)
        {
            if (Hidden.Count == 0) return false;
            try { return Hidden.Contains(d.UnmanagedObject); }
            catch { return false; }
        }
    }
}
