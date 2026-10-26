// 도면 위 부표(명세 5절 '도면 위 부표'): 위반 후보가 있는 '고칠 자리'(엔진 element.anchor)에 오브가 떠서 자전하고, 고치면 사라진다.
//   - Transient(임시 그래픽) 하나에 모든 부표를 그린다 — 도면 DB를 바꾸지 않는다(Undo·저장에 남지 않음).
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
        private static BuoyDrawable? _drawable;
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
                string st = e.Status == "fail" ? "fail" : e.Status == "unknown" ? "warn" : "pass";
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
            foreach (var b in Buoys.Values)
                if (!now.ContainsKey(b.Key)) b.Anim.Vanish();   // 객체가 지워졌거나 규칙이 더는 보지 않음
            Ensure();
            Redraw();
        }

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
            CloseCard(force: true);
            foreach (var c in Pinned.ToList()) c.Close();
            Pinned.Clear();
            if (_doc != null) { try { _doc.ViewChanged -= OnViewChanged; } catch { } }
            _doc = null;
            if (_hover != null) EvidenceHover?.Invoke(_hover.RuleId, _hover.ElementId, false);
            _hover = null;
            Buoys.Clear();
            if (_drawable != null)
            {
                try { TransientManager.CurrentTransientManager.EraseTransient(_drawable, new IntegerCollection()); } catch { }
                _drawable.Dispose();
                _drawable = null;
            }
            StopTick();
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
            if (_drawable == null)
            {
                _drawable = new BuoyDrawable();
                try
                {
                    TransientManager.CurrentTransientManager.AddTransient(_drawable, TransientDrawingMode.DirectTopmost, 140, new IntegerCollection());
                }
                catch { _drawable.Dispose(); _drawable = null; return; }
            }
            if (!_ticking) { OrbClock.Add(TickAction); _ticking = true; }
        }

        private static void StopTick()
        {
            if (_ticking) { OrbClock.Remove(TickAction); _ticking = false; }
        }

        private static void OnTick(double dt)
        {
            if (_doc == null || AcApp.DocumentManager.MdiActiveDocument != _doc) return;
            bool red = Motion.Reduced;
            bool animating = false;
            foreach (var b in Buoys.Values)
            {
                b.Anim.Step(dt, red);
                animating |= b.Anim.IsAnimating(red);
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
        private static void OnViewChanged(object? sender, EventArgs e)
        {
            if (_card is { Pinned: false }) CloseCard(force: false);
            Redraw();
        }

        private static void Redraw()
        {
            if (_drawable == null) return;
            try { TransientManager.CurrentTransientManager.UpdateTransient(_drawable, new IntegerCollection()); } catch { }
        }

        // ── 그리기(뷰마다) ───────────────────────────────────────────────────────

        internal static void Draw(ViewportDraw vd)
        {
            if (!Visible || Buoys.Count == 0 || _doc == null) return;
            double upp;
            try
            {
                var any = Buoys.Values.First().Anchor;
                var px = vd.Viewport.GetNumPixelsInUnitSquare(any);
                upp = px.X > 1e-12 ? 1.0 / px.X : 0;
            }
            catch { return; }
            if (upp <= 0 || double.IsNaN(upp)) return;
            LastUpp = upp;
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

        private static int Rank(string status) => status switch { "fail" => 0, "warn" => 1, _ => 2 };

        private static EntityColor Ec(System.Drawing.Color c) => new(c.R, c.G, c.B);
        private static Transparency Tr(double alpha) => new((byte)Math.Clamp((int)Math.Round(alpha * 255), 0, 255));

        private static void DrawBuoy(ViewportDraw vd, Buoy b, double upp, bool red)
        {
            var a = b.Anim;
            var col = BuoyPainter.ColorOf(a.Status);
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

            double bob = a.Motion.Bob(red) * upp;
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
            if (doc != _doc || !Visible || Buoys.Count == 0 || LastUpp <= 0) { Leave(); return false; }
            double upp = PointerUpp(doc);
            if (upp <= 0) upp = LastUpp;
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
            Leave();
            if (hit == null) return false;
            _hover = hit;
            hit.Anim.SetHover(true);
            EvidenceHover?.Invoke(hit.RuleId, hit.ElementId, true);
            ShowCard(hit);
            Ensure();
            return true;
        }

        private static void Leave()
        {
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
            var items = Members(b).OrderBy(x => Rank(x.Anim.Status))
                .Select(x => new BuoyCardItem(x.Anim.Status, x.RuleId, x.RuleCondition, x.El)).ToList();
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
            string st = e.Status == "fail" ? "fail" : e.Status == "unknown" ? "warn" : "pass";
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

    /// 부표 전체를 그리는 Transient 하나. 뷰마다(ViewportDraw) 픽셀 크기를 받아 화면 크기 고정으로 그린다.
    internal sealed class BuoyDrawable : Drawable
    {
        public BuoyDrawable() : base(IntPtr.Zero, false) { }
        public override bool IsPersistent => false;
        public override ObjectId Id => ObjectId.Null;
        protected override int SubSetAttributes(DrawableTraits traits) => (int)DrawableAttributes.ViewDependentViewportDraw;
        protected override bool SubWorldDraw(WorldDraw wd) => false;
        protected override void SubViewportDraw(ViewportDraw vd)
        {
            try { BuoyLayer.Draw(vd); } catch { /* 그리기 실패가 AutoCAD를 멈추게 하지 않게 */ }
        }
        protected override int SubViewportDrawLogicalFlags(ViewportDraw vd) => 0;
    }
}
