// 진입점과 명령. 리본 'Buoy-RuleCheck' 탭의 버튼도 아래 명령을 실행한다. 명령줄 문구는 '[BRC] …'(명세 6-4).
//   BRC         검사 시작(팔레트 + 현재 도면 감시)        BRCCHECK  지금 다시 검사
//   BRCOFF     감시 중지 + 표시 지우기                    BRCHEALTH 엔진 연결 확인
//   BRCSETUP   GA_ 레이어·라벨 스타일·특성 틀 만들기      BRCSHIP   선박 정보 탭 열기
//   BRCEYE     조종위치(GA_EYE 점) 지정                   BRCPICK   AP/FP/기선을 도면에서 지정
//   BRCTOGGLE  부표 표시 켜기/끄기                        BRCREPORT HTML 검토 리포트 저장·열기
//   BRCMOTION  동작 줄이기 켜기/끄기(회전·물결 멈춤, 색은 그대로)
//   BRCLARGE   라벨 확대(라벨 글자·선 두께·표식 크기 2.5배 — 판정·도면은 그대로)
//   BRCVERSION 지금 상태를 버전으로 기록(저장할 때도 자동으로 묻는다)   BRCCOMPARE 버전 비교 창
//   BRCPANEL   떠 있는 로고의 수정 소요 목록 열기/닫기(Ctrl+.)         BRCLOGO   로고 보이기/숨기기
//   BRCLINK    실도면 연결 마법사(치수표·축척 → 측면도 영역 → AP/FP/기선 → 역할 지정 → 조종위치)   [LinkWizard.cs]
//   BRCROLE    고른 객체에 역할(선체·장애물·충돌격벽·구상선수·조종위치) 붙이기/해제   BRCROLESHOW 판정에 쓰인 객체 역할별 색 표시
//   옛 이름 GARC*도 한동안 별칭으로 된다(LegacyCommands.cs).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(GaRuleCheck.PluginApp))]
[assembly: CommandClass(typeof(GaRuleCheck.Commands))]

namespace GaRuleCheck
{
    public sealed class PluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                $"\n{Product.Tag} {Product.Name} 로드됨 — 리본 '{Product.Name}' 탭 또는 BRC 명령으로 시작하세요.\n");
            AcApp.DocumentManager.DocumentToBeDestroyed += (_, e) => Commands.Forget(e.Document);
            AcApp.DocumentManager.DocumentActivated += (_, e) => Commands.OnActivated(e.Document);
            try { RibbonUi.Install(); } catch { /* 리본 없는 환경(콘솔 등) */ }
            StatusTray.Install();
            StatusTray.Clicked += Commands.OpenFromTray;
            Commands.HookBuoys();
            // 떠 있는 로고는 메인 창이 준비된 뒤(첫 Idle)에 만든다
            AcApp.Idle += InitFloatingOnce;
        }

        private static void InitFloatingOnce(object? sender, EventArgs e)
        {
            AcApp.Idle -= InitFloatingOnce;
            try { Commands.InitFloating(); } catch { /* 로고는 부가 기능 */ }
        }

        public void Terminate()
        {
            BuoyLayer.Clear();
            Commands.DetachAll();
            StatusTray.Remove();
            FloatingUi.Shutdown();
        }
    }

    public sealed partial class Commands
    {
        private static readonly Dictionary<Document, DocWatcher> Watchers = new();
        private static readonly Dictionary<Document, CompareForm> CompareForms = new();
        private static PaletteSet? _palette;
        private static ResultPanel? _panel;

        /// 열려 있는 결과 패널(도킹 팔레트). 떠 있는 로고의 목록은 FloatingUi가 따로 갱신한다.
        private static IEnumerable<ResultPanel> Panels()
        {
            if (_panel != null && !_panel.IsDisposed) yield return _panel;
        }

        private static void ForPanels(Action<ResultPanel> act)
        {
            foreach (var p in Panels().ToList()) act(p);
        }

        private static Document? Active => AcApp.DocumentManager.MdiActiveDocument;
        private static string DocName(Document d) => Path.GetFileName(d.Name);

        // ── 검사 ────────────────────────────────────────────────────────────

        [CommandMethod("BRC")]
        public void Start()
        {
            var doc = Active;
            if (doc == null) return;
            EnsurePalette();
            DrawingProps.EnsureSetup(doc, props: false);   // 라벨 스타일·레이어가 없으면 만든다
            if (!EngineClient.EnsureRunning())
                doc.Editor.WriteMessage($"\n{Product.Tag} 검사 엔진을 켤 수 없어요 — engine 폴더에서 'python -m brc serve'를 실행하세요.\n");
            Attach(doc).MarkDirty();
            _panel!.Ship.Load(DrawingProps.ReadCanonical(doc.Database));
            if (!DrawingProps.IsConfigured(doc.Database))
            {
                _panel.ShowTab("ship");
                doc.Editor.WriteMessage($"\n{Product.Tag} 선박 정보(LOA 등)가 비어 있어요 — 빈 입력은 판정하지 않습니다. 팔레트 '선박 정보' 탭에서 입력하세요.\n");
            }
            doc.Editor.WriteMessage($"\n{Product.Tag} 감시 시작 — 도면이 바뀌면 바뀐 입력에 걸린 규칙만 다시 검사합니다.\n");
        }

        [CommandMethod("BRCCHECK")]
        public void CheckNow()
        {
            var doc = Active;
            if (doc == null) return;
            EnsurePalette();
            EngineClient.EnsureRunning();
            Attach(doc).RunCheck();
        }

        [CommandMethod("BRCOFF")]
        public void Stop()
        {
            var doc = Active;
            if (doc != null) Detach(doc);
            ForPanels(p => p.SetIdle("감시 중지됨", doc != null ? DocName(doc) : null));
            FloatingUi.SetState("idle");   // 로고 칩에 이전 개수가 남지 않게
        }

        [CommandMethod("BRCHEALTH")]
        public void Health()
        {
            var ed = Active?.Editor;
            try { ed?.WriteMessage($"\n{Product.Tag} 엔진 응답: " + EngineClient.Health() + "\n"); ForPanels(p => p.SetEngine(true)); }
            catch (EngineException ex) { ed?.WriteMessage($"\n{Product.Tag} " + ex.Message + "\n"); ForPanels(p => p.SetEngine(false)); }
        }

        // ── 입력 ────────────────────────────────────────────────────────────

        [CommandMethod("BRCSETUP")]
        public void Setup()
        {
            var doc = Active;
            if (doc == null) return;
            DrawingProps.EnsureSetup(doc, props: true);
            var props = DrawingProps.ReadCanonical(doc.Database);
            ForPanels(p => p.Ship.Load(props));
            doc.Editor.WriteMessage(
                $"\n{Product.Tag} GA_ 레이어 5개, 라벨 문자 스타일(BRC_LABEL), BRC_* 특성 틀을 만들었습니다." +
                "\n좌표 규약: 측면도, X = 선수 방향 +, Y = 위 +. 선박 정보는 팔레트 '선박 정보' 탭에서 입력.\n");
        }

        [CommandMethod("BRCSHIP")]
        public void Ship()
        {
            var doc = Active;
            if (doc == null) return;
            EnsurePalette();
            _panel!.Ship.Load(DrawingProps.ReadCanonical(doc.Database));
            _panel.ShowTab("ship");
        }

        [CommandMethod("BRCEYE")]
        public void Eye()
        {
            var doc = Active;
            if (doc == null) return;
            var ed = doc.Editor;
            var res = ed.GetPoint(new PromptPointOptions("\n조종위치(선교 갑판 높이)를 클릭: "));
            if (res.Status != PromptStatus.OK) return;
            PlaceEye(doc, res.Value.TransformBy(ed.CurrentUserCoordinateSystem));
        }

        /// GA_EYE 점 하나를 pt(WCS)에 둔다 — 1개면 이동(handle 유지), 없으면 만든다. 실도면 연결 마법사도 쓴다.
        internal static void PlaceEye(Document doc, Point3d pt)
        {
            var ed = doc.Editor;
            DrawingProps.EnsureSetup(doc, props: false);
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var eyes = new List<DBPoint>();
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is DBPoint p && p.Layer.Equals("GA_EYE", StringComparison.OrdinalIgnoreCase))
                        eyes.Add(p);
                if (eyes.Count == 1)
                {
                    eyes[0].UpgradeOpen();
                    eyes[0].Position = pt;   // 기존 점 이동 → handle 유지(이력에 '수정'으로 남음)
                }
                else
                {
                    var p = new DBPoint(pt) { Layer = "GA_EYE" };
                    ms.AppendEntity(p);
                    tr.AddNewlyCreatedDBObject(p, true);
                    if (eyes.Count > 1) ed.WriteMessage($"\n{Product.Tag} 주의: GA_EYE 점이 {eyes.Count + 1}개입니다. 하나만 남기세요(엔진이 '검토 불가'로 표시).");
                }
                tr.Commit();
            }
            if (db.Pdmode == 0) db.Pdmode = 35;   // 점이 보이도록 (도면 설정 변경, Undo 가능)
            ed.WriteMessage($"\n{Product.Tag} 조종위치를 지정했습니다.\n");
        }

        [CommandMethod("BRCPICK")]
        public void Pick()
        {
            var doc = Active;
            if (doc == null) return;
            var ed = doc.Editor;
            var kw = new PromptKeywordOptions("\n지정할 항목 [AP/FP/BASE]: ");
            kw.Keywords.Add("AP"); kw.Keywords.Add("FP"); kw.Keywords.Add("BASE");
            var k = ed.GetKeywords(kw);
            if (k.Status != PromptStatus.OK) return;
            double? scale = Watchers.TryGetValue(doc, out var w) ? w.Last?.UnitsScale : null;
            if (scale is not double s || s <= 0)
            {
                ed.WriteMessage($"\n{Product.Tag} 도면 단위를 아직 모릅니다. 먼저 BRC로 한 번 검사하세요(단위 해석은 엔진이 합니다).\n");
                return;
            }
            var label = k.StringResult switch { "AP" => "AP(선미수선)", "FP" => "FP(선수수선)", _ => "기선(baseline)" };
            var res = ed.GetPoint(new PromptPointOptions($"\n{label} 위치를 클릭: "));
            if (res.Status != PromptStatus.OK) return;
            var pt = res.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var (key, value) = k.StringResult switch
            {
                "AP" => ("BRC_X_AP", pt.X * s),
                "FP" => ("BRC_X_FP", pt.X * s),
                _ => ("BRC_Z_BASE", pt.Y * s),
            };
            DrawingProps.Write(doc.Database, new Dictionary<string, string> { [key] = DrawingProps.M(value) });
            ForPanels(p => p.Ship.SetField(key, DrawingProps.M(value)));
            w?.MarkDirty();
            ed.WriteMessage($"\n{Product.Tag} {key} = {DrawingProps.M(value)} m\n");
        }

        // ── 표시·기록 ───────────────────────────────────────────────────────

        /// 부표 표시 켜기/끄기(판정·검사는 계속, 도면은 그대로)
        [CommandMethod("BRCTOGGLE")]
        public void Toggle()
        {
            var doc = Active;
            bool on = !BuoyLayer.Visible;
            BuoyLayer.SetVisible(on);
            ForPanels(p => p.SetOverlayState(on));
            doc?.Editor.WriteMessage($"\n{Product.Tag} 부표 표시 {(on ? "켜짐" : "꺼짐")}\n");
        }

        /// 부표 ↔ 도면 근거·팔레트 연결(플러그인 로드 때 한 번)
        internal static void HookBuoys()
        {
            BuoyLayer.EvidenceHover += (rule, el, on) =>
            {
                var d = Active;
                if (d == null) return;
                try { if (on) Overlay.ShowEvidence(d, rule, el); else Overlay.ClearEvidence(); } catch { }
            };
            BuoyLayer.EvidenceRequested += (rule, el) =>
            {
                EnsurePalette();
                ForPanels(p => p.FocusRule(rule));
            };
        }

        /// 동작 줄이기(명세 5절 '공통 움직임'): 오브·부표·로고의 회전·물결·크기 변화를 멈춘다. 상태 색은 그대로.
        [CommandMethod("BRCMOTION")]
        public void ToggleMotion()
        {
            UiState.SetReduceMotion(!UiState.Current.ReduceMotion);
            bool on = UiState.Current.ReduceMotion;
            Active?.Editor.WriteMessage($"\n{Product.Tag} 동작 줄이기 {(on ? "켜짐 — 회전·물결을 멈추고 상태 색은 그대로" : Motion.Reduced ? "꺼짐(Windows '애니메이션 표시'가 꺼져 있어 계속 멈춰 있음)" : "꺼짐")}\n");
        }

        [CommandMethod("BRCLARGE")]
        public void LargeLabels()
        {
            var doc = Active;
            Overlay.Scale = Overlay.Scale > 1.0 ? 1.0 : 2.5;
            ForPanels(p => p.SetLargeLabelsState(Overlay.Scale > 1.0));
            Redraw(doc);
            doc?.Editor.WriteMessage($"\n{Product.Tag} 라벨 확대 {(Overlay.Scale > 1.0 ? "켜짐 (라벨 글자·선 두께·표식 크기 2.5배, 판정은 그대로)" : "꺼짐")}\n");
        }

        [CommandMethod("BRCREPORT")]
        public void Report()
        {
            var doc = Active;
            if (doc == null) return;
            if (!Watchers.TryGetValue(doc, out var w) || w.Last == null)
            {
                doc.Editor.WriteMessage($"\n{Product.Tag} 먼저 BRC로 검사하세요.\n");
                return;
            }
            try
            {
                var html = EngineClient.Report(Extractor.DocId(doc), DocName(doc));
                var dir = Path.GetDirectoryName(doc.Name);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var file = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(doc.Name)}_GA검토_{DateTime.Now:yyyyMMdd_HHmm}.html");
                File.WriteAllText(file, html, new System.Text.UTF8Encoding(false));
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                doc.Editor.WriteMessage($"\n{Product.Tag} 리포트 저장: {file}\n");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n{Product.Tag} 리포트 실패: " + ex.Message + "\n");
            }
        }

        // ── 버전 기록·비교 (기능 1) ─────────────────────────────────────────

        [CommandMethod("BRCVERSION")]
        public void Version()
        {
            var doc = Active;
            if (doc != null) RecordVersion(doc, fromSave: false);
        }

        /// 지금 도면 상태를 버전으로 기록. 저장 직후(fromSave)에는 GA_ 레이어·선박 정보가 그대로면 묻지 않는다.
        internal static void RecordVersion(Document doc, bool fromSave)
        {
            try
            {
                RawDrawing raw;
                using (doc.LockDocument()) raw = Extractor.Extract(doc);
                var folder = VersionStore.FolderFor(doc.Name, doc.IsNamedDrawing);
                if (VersionStore.SameAsLatest(folder, raw))
                {
                    if (!fromSave) doc.Editor.WriteMessage($"\n{Product.Tag} 마지막 버전과 같아(GA_ 레이어·선박 정보 변화 없음) 새 버전을 만들지 않았습니다.\n");
                    return;
                }
                var idx = VersionStore.Load(folder);
                int next = (VersionStore.Latest(idx)?.Version ?? 0) + 1;
                // 마지막 검사가 지금 도면과 다르면(바뀐 뒤 아직 검사 안 함·검사 실패·감시 밖에서 바뀐 선박 정보) 다시 검사해 기록에 쓴다.
                // 같으면 다시 검사하지 않는다(같은 입력 재검사는 결과가 같고 시간만 든다).
                Watchers.TryGetValue(doc, out var w);
                if (w is { Busy: false } && (w.Dirty || w.Last == null || w.LastRaw == null
                                             || VersionStore.Fingerprint(w.LastRaw) != VersionStore.Fingerprint(raw)))
                    w.RunCheck();
                var last = w?.Last;
                // 기본값은 '직전 검사 대비'가 아니라 '마지막 기록 버전 대비'로 정한다: 엔진 비교로 해결된 위반이 있으면 '규정 위반',
                // 관련 조항은 지금 위반 중이거나 이번에 해결된 규칙의 조항. 비교를 못 하면 검사 결과의 전환으로 대신한다.
                var resolvedIds = new HashSet<string>();
                var latest = VersionStore.Latest(idx);
                bool compared = false;
                if (latest != null)
                {
                    try
                    {
                        var cmp = EngineClient.Compare(VersionStore.LoadRaw(folder, latest), raw, VersionStore.Label(latest), "현재");
                        foreach (var x in cmp.ResolvedRules) resolvedIds.Add(x.RuleId);
                        foreach (var c in cmp.Changes) foreach (var x in c.Resolved) resolvedIds.Add(x.RuleId);
                        compared = true;
                    }
                    catch { /* 엔진이 안 되거나 이전 스냅샷을 못 읽으면 아래로 */ }
                }
                if (!compared && last != null)
                    foreach (var t in last.Transitions.Where(t => t.Kind == "resolved")) resolvedIds.Add(t.RuleId);
                var refs = last?.Results.Where(r => r.Status == "fail" || resolvedIds.Contains(r.RuleId))
                               .Select(r => r.Ref).Distinct().ToList() ?? new List<string>();
                string reason = resolvedIds.Count > 0 || (last?.Results.Any(r => r.Status == "fail") ?? false) ? "rule_violation" : "other";
                string author = (AcApp.GetSystemVariable("LOGINNAME") as string) ?? Environment.UserName;
                var (fail, unknown) = w?.ElementCounts() ?? (0, 0);
                string summary = last == null ? "아직 검사 결과 없음" : $"현재 판정: {last.OverallKo} · 미충족 {fail} · 확인 필요 {unknown}";
                using var dlg = new VersionDialog(next, DocName(doc), author, reason, string.Join(" · ", refs), summary);
                if (AcApp.ShowModalDialog(dlg) != System.Windows.Forms.DialogResult.OK) return;
                var entry = VersionStore.Add(folder, DocName(doc), raw, dlg.Result, last);
                doc.Editor.WriteMessage($"\n{Product.Tag} 버전 v{entry.Version} 기록 — {VersionStore.ReasonText(entry.Reason)} · {entry.Author} ({folder})\n");
                if (CompareForms.TryGetValue(doc, out var cf) && !cf.IsDisposed) cf.Close();   // 열린 비교 창은 다시 열 때 새 목록으로
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\n{Product.Tag} 버전 기록 실패: " + ex.Message + "\n");
            }
        }

        [CommandMethod("BRCCOMPARE")]
        public void Compare()
        {
            var doc = Active;
            if (doc == null) return;
            if (CompareForms.TryGetValue(doc, out var open) && !open.IsDisposed) { open.Activate(); return; }
            if (!EngineClient.EnsureRunning())
            {
                doc.Editor.WriteMessage($"\n{Product.Tag} 엔진 서버에 연결할 수 없어 비교할 수 없습니다. 'python -m brc serve'를 실행하세요.\n");
                return;
            }
            // 비교 창은 연 도면(doc)에 묶인다 — 다른 도면 탭이 활성일 때는 그 도면에 표시·이동하지 않는다
            var folder = VersionStore.FolderFor(doc.Name, doc.IsNamedDrawing);
            var src = new CompareSource
            {
                DrawingName = DocName(doc),
                SaveDir = Path.GetDirectoryName(doc.Name),
                LoadIndex = () => VersionStore.Load(folder),
                LoadRaw = v => VersionStore.LoadRaw(folder, v),
                ExtractLive = () => { using (doc.LockDocument()) return Extractor.Extract(doc); },
                DwgNumber = () =>
                {
                    try
                    {
                        var p = DrawingProps.ReadCanonical(doc.Database);
                        if (p.TryGetValue("BRC_DWG_NO", out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
                    }
                    catch { /* 특성 읽기 실패 → 파일 이름 */ }
                    return Path.GetFileNameWithoutExtension(doc.Name);
                },
            };
            var form = new CompareForm(src,
                d =>
                {
                    if (d == null) { Overlay.ClearDiff(); return true; }
                    if (Active != doc) return false;   // 비교 창이 '현재 도면 탭이 아님'을 알린다
                    Overlay.ShowDiff(doc, d);
                    return true;
                },
                (c, scale) =>
                {
                    if (Active != doc || c.Bbox == null) return;
                    using (doc.LockDocument()) Overlay.FocusBox(doc, c.Bbox, scale);
                });
            form.FormClosed += (_, _) => CompareForms.Remove(doc);
            CompareForms[doc] = form;
            AcApp.ShowModelessDialog(form);
        }

        // ── 떠 있는 로고 패널 (기능 2) ───────────────────────────────────────

        [CommandMethod("BRCPANEL")]
        public void TogglePanel() => FloatingUi.Toggle();

        [CommandMethod("BRCLOGO")]
        public void ToggleLogo() => FloatingUi.ToggleLogo();

        // ── 내부 ────────────────────────────────────────────────────────────

        internal static DocWatcher Attach(Document doc)
        {
            if (Watchers.TryGetValue(doc, out var w)) return w;
            w = new DocWatcher(doc);
            w.Checked += x =>
            {
                if (x.Last == null) return;
                if (x.Doc == Active)
                {
                    ForPanels(p => p.ShowResult(x.Last, DocName(x.Doc), seconds: x.LastSeconds));
                    FloatingUi.SetResult(x.Last);
                    // 해결이 생기면 로고 옆 토스트(명세 6-4) — 같은 입력으로 되돌린 경우(Undo)도 해결로 센다
                    int solved = x.Last.Transitions.Count(t => t.Kind == "resolved");
                    if (solved > 0) FloatingUi.Toast(UiText.ToastResolved(solved));
                    // 명령줄(명세 6-4): [BRC] rev 2 · 수정 소요 2 — 선교 가시거리, 충돌격벽 위치
                    try
                    {
                        var ed = x.Doc.Editor;
                        foreach (var line in UiText.CommandLines(x.Last, x.LastSeconds)) ed.WriteMessage($"\n{Product.Tag} {line}");
                        if (string.IsNullOrEmpty(x.Doc.CommandInProgress)) ed.PostCommandPrompt();
                    }
                    catch { /* 명령줄 문구는 부가 기능 */ }
                }
                StatusTray.Update(x.Last, DocName(x.Doc));
            };
            w.Failed += (_, msg) =>
            {
                ForPanels(p => p.ShowEngineDown(msg));
                if (doc == Active) FloatingUi.SetState("down");
                StatusTray.Set("down", "연결 끊김 — 판정하지 않음");
                doc.Editor.WriteMessage($"\n{Product.Tag} 연결 끊김 — 판정하지 않음: {msg}\n");
            };
            w.VersionRequested += x => RecordVersion(x.Doc, fromSave: true);
            Watchers[doc] = w;
            return w;
        }

        internal static void Detach(Document doc)
        {
            if (!Watchers.TryGetValue(doc, out var w)) return;
            w.Dispose();
            Watchers.Remove(doc);
            Overlay.Forget();
            BuoyLayer.Clear();
            try { EngineClient.Reset(Extractor.DocId(doc)); } catch { }
        }

        /// 도면이 닫힐 때: 감시를 끊고, 그 도면의 비교 창도 닫는다.
        internal static void Forget(Document doc)
        {
            Detach(doc);
            if (CompareForms.TryGetValue(doc, out var cf) && !cf.IsDisposed) cf.Close();
            CompareForms.Remove(doc);
        }

        internal static void DetachAll()
        {
            foreach (var doc in new List<Document>(Watchers.Keys)) Detach(doc);
        }

        /// 도면 탭을 바꾸면 그 도면의 결과·선박 정보로 팔레트를 바꾼다. 설정된 도면이면 자동 감시.
        internal static void OnActivated(Document? doc)
        {
            if (doc == null) return;
            // 부표·근거는 지금 도면의 마지막 판정으로(팔레트가 닫혀 있어도)
            BuoyLayer.Clear();
            if (Watchers.TryGetValue(doc, out var wb) && wb.Last != null)
            {
                try { using (doc.LockDocument()) Overlay.Show(doc, wb.Last); } catch { }
                BuoyLayer.Update(doc, wb.Last);
            }
            else Overlay.Forget();
            // 로고는 팔레트가 닫혀 있어도 보이므로, 팔레트 표시 여부와 상관없이 그 도면의 상태로 바꾼다
            if (Watchers.TryGetValue(doc, out var w0) && w0.Last != null) FloatingUi.SetResult(w0.Last);
            else FloatingUi.SetState(w0 is { LastFailed: true } ? "down" : "idle");
            bool shown = _palette != null && _palette.Visible;
            if (!Panels().Any() || !shown)
            {
                Overlay.ClearDiff();      // 다른 도면의 비교 표시가 남지 않게
                ReapplyCompare(doc);
                return;
            }
            Overlay.Clear();
            Overlay.ClearDiff();
            var props = DrawingProps.ReadCanonical(doc.Database);
            ForPanels(p => { p.Ship.Load(props); p.ClearHistory(); });
            if (Watchers.TryGetValue(doc, out var w) && w.Last != null)
            {
                ForPanels(p => p.ShowResult(w.Last, DocName(doc), record: false, seconds: w.LastSeconds));
                Redraw(doc);
            }
            else if (DrawingProps.IsConfigured(doc.Database))
            {
                Attach(doc).MarkDirty();
                ForPanels(p => p.SetIdle("검사 중…", DocName(doc)));
            }
            else
            {
                ForPanels(p => p.SetIdle("설정되지 않은 도면", DocName(doc), showSetup: true));
            }
            ReapplyCompare(doc);
        }

        /// 이 도면의 비교 창이 '도면에 표시' 중이면 다시 그린다(탭을 바꾸면 임시 표시를 지우므로).
        private static void ReapplyCompare(Document doc)
        {
            if (CompareForms.TryGetValue(doc, out var cf) && !cf.IsDisposed) cf.ReapplyDrawing();
        }

        private static void Redraw(Document? doc)
        {
            if (doc == null) return;
            if (Watchers.TryGetValue(doc, out var w) && w.Last != null)
                using (doc.LockDocument()) Overlay.Show(doc, w.Last);
            else Overlay.Clear();
        }

        /// 결과 패널 하나를 만들고 명령에 연결한다(도킹 팔레트·떠 있는 패널 공용).
        internal static ResultPanel CreatePanel()
        {
            var panel = new ResultPanel();
            panel.RecheckRequested += () => { var d = Active; if (d != null) { EngineClient.EnsureRunning(); Attach(d).RunCheck(); } };
            panel.StartRequested += () => Send("_BRCSETUP _BRC ");
            panel.LinkRequested += () => Send("_BRCLINK ");
            panel.ToggleOverlayRequested += () => Send("_BRCTOGGLE ");
            panel.ReportRequested += () => Send("_BRCREPORT ");
            // 항목 카드 hover → 도면에 근거(시선·허용선·대상)와 그 항목 부표 키우기, [부표로 이동] → 고칠 자리 + 카드 고정
            panel.RuleHoverRequested += (rr, el, on) =>
            {
                var d = Active;
                if (d == null) return;
                BuoyLayer.Highlight(rr.RuleId, on);
                try { if (on) Overlay.ShowEvidence(d, rr.RuleId, el); else Overlay.ClearEvidence(); } catch { }
            };
            panel.BuoyRequested += (rr, el) =>
            {
                var d = Active;
                if (d == null) return;
                double sc = Watchers.TryGetValue(d, out var ww) ? ww.Last?.UnitsScale ?? 0 : 0;
                if (!BuoyLayer.OpenFromList(d, sc, rr, el))
                    FocusTargets(el != null ? new List<string> { el.ElementId } : rr.Targets);
            };
            panel.LargeLabelsRequested += () => Send("_BRCLARGE ");
            // 점검 항목 hover → 도면에서 그 객체를 상태 색으로 강조, 클릭 → 그 객체로 확대·이동
            panel.ElementHoverRequested += (el, on) =>
            {
                var d = Active;
                if (d == null || !Overlay.Enabled) return;
                var shape = Watchers.TryGetValue(d, out var ww) ? ww.ShapeOf(el.ElementId) : null;
                Overlay.ElementHover(d, on ? shape?.pts : null, shape?.closed ?? false, el.Status, shape?.bulges);
            };
            panel.ElementFocusRequested += el => FocusTargets(new List<string> { el.ElementId });
            panel.Ship.PickRequested += what => Send($"_BRCPICK {what} ");
            panel.Ship.EyeRequested += () => Send("_BRCEYE ");
            panel.Ship.SetupRequested += () => Send("_BRCSETUP ");
            panel.Ship.ReloadRequested += () => { var d = Active; if (d != null) panel.Ship.Load(DrawingProps.ReadCanonical(d.Database)); };
            panel.Ship.SaveRequested += values =>
            {
                var d = Active;
                if (d == null) return;
                using (d.LockDocument()) DrawingProps.Write(d.Database, values);
                Attach(d).MarkDirty();
                ForPanels(p => p.ShowTab("review"));
            };
            // 새로 만든 패널도 지금 도면의 마지막 결과로 채운다
            var doc = Active;
            if (doc != null)
            {
                panel.Ship.Load(DrawingProps.ReadCanonical(doc.Database));
                if (Watchers.TryGetValue(doc, out var w) && w.Last != null) panel.ShowResult(w.Last, DocName(doc), record: false);
            }
            panel.SetOverlayState(Overlay.Enabled);
            panel.SetLargeLabelsState(Overlay.Scale > 1.0);
            return panel;
        }

        internal static void InitFloating()
        {
            // 로고 목록 한 줄 → 그 고칠 자리로 이동 + 부표 카드 고정, '팔레트에서 자세히' → 팔레트
            FloatingUi.RowPicked += r =>
            {
                var d = Active;
                if (d == null) return;
                double sc = Watchers.TryGetValue(d, out var ww) ? ww.Last?.UnitsScale ?? 0 : 0;
                if (!BuoyLayer.OpenFromList(d, sc, r, null))
                {
                    EnsurePalette();
                    ForPanels(p => p.FocusRule(r.RuleId));
                }
            };
            FloatingUi.PaletteRequested += EnsurePalette;
            FloatingUi.Init();
        }

        private static void EnsurePalette()
        {
            if (_palette != null)
            {
                _palette.Visible = true;
                return;
            }
            _panel = CreatePanel();

            _palette = new PaletteSet(Product.Name, new Guid("3f6c2a71-5b8e-4d0a-9c1e-7a2b4d6e8f10"))
            {
                Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
                MinimumSize = new System.Drawing.Size(380, 480),
            };
            _palette.Add("검토", _panel);
            _palette.Visible = true;
            _palette.Dock = DockSides.Right;
        }

        /// 상태 표시줄 아이콘 클릭: 팔레트가 있으면 보이게, 없으면 검사 시작.
        internal static void OpenFromTray()
        {
            if (_palette != null) { _palette.Visible = true; return; }
            Send("_BRC ");
        }

        /// 모달리스 팔레트에서 명령을 실행할 때는 명령 문자열로 보낸다(문서 잠금·Undo가 명령 단위로 정리됨).
        private static void Send(string cmd) => Active?.SendStringToExecute(cmd, true, false, true);

        /// 대상 객체를 선택하고 화면을 그 범위로 맞춘다(2D 평면 뷰 기준).
        private static void FocusTargets(List<string> handles)
        {
            var doc = Active;
            if (doc == null || handles.Count == 0) return;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ids = new List<ObjectId>();
                Extents3d? ext = null;
                foreach (var h in handles.Select(Extractor.BaseHandle).Distinct())   // 펼친 블록 하위 객체 → 블록
                {
                    var id = Extractor.HandleToId(db, h);
                    if (id.IsNull || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;
                    ids.Add(id);
                    try
                    {
                        var e = ent.GeometricExtents;
                        if (ext is Extents3d cur) { cur.AddExtents(e); ext = cur; } else ext = e;
                    }
                    catch { /* 범위 없는 객체 */ }
                }
                tr.Commit();
                if (ids.Count == 0) return;
                doc.Editor.SetImpliedSelection(ids.ToArray());
                if (ext is Extents3d x)
                {
                    using var view = doc.Editor.GetCurrentView();
                    view.CenterPoint = new Point2d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2);
                    view.Width = Math.Max(x.MaxPoint.X - x.MinPoint.X, 1) * 3;
                    view.Height = Math.Max(x.MaxPoint.Y - x.MinPoint.Y, 1) * 3;
                    doc.Editor.SetCurrentView(view);
                }
            }
        }
    }
}
