// 진입점과 명령. 리본 'GA RuleCheck' 탭의 버튼도 아래 명령을 실행한다.
//   GARC        검사 시작(팔레트 + 현재 도면 감시)        GARCCHECK  지금 다시 검사
//   GARCOFF     감시 중지 + 표시 지우기                    GARCHEALTH 엔진 연결 확인
//   GARCSETUP   GA_ 레이어·라벨 스타일·특성 틀 만들기      GARCSHIP   선박 정보 탭 열기
//   GARCEYE     조종위치(GA_EYE 점) 지정                   GARCPICK   AP/FP/기선을 도면에서 지정
//   GARCTOGGLE  도면 표시 켜기/끄기                        GARCREPORT HTML 검토 리포트 저장·열기
//   GARCLARGE   라벨 확대(라벨 글자·선 두께·표식 크기 2.5배 — 판정·도면은 그대로)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
                "\nGA-RuleCheck 로드됨 — 리본 'GA RuleCheck' 탭 또는 GARC 명령으로 시작하세요.\n");
            AcApp.DocumentManager.DocumentToBeDestroyed += (_, e) => Commands.Detach(e.Document);
            AcApp.DocumentManager.DocumentActivated += (_, e) => Commands.OnActivated(e.Document);
            try { RibbonUi.Install(); } catch { /* 리본 없는 환경(콘솔 등) */ }
            StatusTray.Install();
            StatusTray.Clicked += Commands.OpenFromTray;
        }

        public void Terminate()
        {
            Commands.DetachAll();
            StatusTray.Remove();
        }
    }

    public sealed class Commands
    {
        private static readonly Dictionary<Document, DocWatcher> Watchers = new();
        private static PaletteSet? _palette;
        private static ResultPanel? _panel;

        private static Document? Active => AcApp.DocumentManager.MdiActiveDocument;
        private static string DocName(Document d) => Path.GetFileName(d.Name);

        // ── 검사 ────────────────────────────────────────────────────────────

        [CommandMethod("GARC")]
        public void Start()
        {
            var doc = Active;
            if (doc == null) return;
            EnsurePalette();
            DrawingProps.EnsureSetup(doc, props: false);   // 라벨 스타일·레이어가 없으면 만든다
            if (!EngineClient.EnsureRunning())
                doc.Editor.WriteMessage("\nGA-RuleCheck: 엔진 서버를 켤 수 없습니다. engine 폴더에서 'python -m garc serve'를 실행하세요.\n");
            Attach(doc).MarkDirty();
            _panel!.Ship.Load(DrawingProps.Read(doc.Database));
            if (!DrawingProps.IsConfigured(doc.Database))
            {
                _panel.ShowTab("ship");
                doc.Editor.WriteMessage("\nGA-RuleCheck: 선박 정보(LOA 등)가 비어 있습니다. 팔레트 '선박 정보' 탭에서 입력하세요.\n");
            }
            doc.Editor.WriteMessage("\nGA-RuleCheck: 감시 시작. GA_ 레이어가 바뀌면 자동으로 다시 검사합니다.\n");
        }

        [CommandMethod("GARCCHECK")]
        public void CheckNow()
        {
            var doc = Active;
            if (doc == null) return;
            EnsurePalette();
            EngineClient.EnsureRunning();
            Attach(doc).RunCheck();
        }

        [CommandMethod("GARCOFF")]
        public void Stop()
        {
            var doc = Active;
            if (doc != null) Detach(doc);
            _panel?.SetIdle("감시 중지됨", doc != null ? DocName(doc) : null);
        }

        [CommandMethod("GARCHEALTH")]
        public void Health()
        {
            var ed = Active?.Editor;
            try { ed?.WriteMessage("\n엔진 응답: " + EngineClient.Health() + "\n"); _panel?.SetEngine(true); }
            catch (EngineException ex) { ed?.WriteMessage("\n" + ex.Message + "\n"); _panel?.SetEngine(false); }
        }

        // ── 입력 ────────────────────────────────────────────────────────────

        [CommandMethod("GARCSETUP")]
        public void Setup()
        {
            var doc = Active;
            if (doc == null) return;
            DrawingProps.EnsureSetup(doc, props: true);
            _panel?.Ship.Load(DrawingProps.Read(doc.Database));
            doc.Editor.WriteMessage(
                "\nGA_ 레이어 5개, 라벨 문자 스타일(GARC_LABEL), GARC_* 특성 틀을 만들었습니다." +
                "\n좌표 규약: 측면도, X = 선수 방향 +, Y = 위 +. 선박 정보는 팔레트 '선박 정보' 탭에서 입력.\n");
        }

        [CommandMethod("GARCSHIP")]
        public void Ship()
        {
            var doc = Active;
            if (doc == null) return;
            EnsurePalette();
            _panel!.Ship.Load(DrawingProps.Read(doc.Database));
            _panel.ShowTab("ship");
        }

        [CommandMethod("GARCEYE")]
        public void Eye()
        {
            var doc = Active;
            if (doc == null) return;
            var ed = doc.Editor;
            var res = ed.GetPoint(new PromptPointOptions("\n조종위치(선교 갑판 높이)를 클릭: "));
            if (res.Status != PromptStatus.OK) return;
            DrawingProps.EnsureSetup(doc, props: false);
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var eyes = new List<DBPoint>();
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is DBPoint p && p.Layer.Equals("GA_EYE", StringComparison.OrdinalIgnoreCase))
                        eyes.Add(p);
                var pt = res.Value.TransformBy(ed.CurrentUserCoordinateSystem);
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
                    if (eyes.Count > 1) ed.WriteMessage($"\n주의: GA_EYE 점이 {eyes.Count + 1}개입니다. 하나만 남기세요(엔진이 '검토 불가'로 표시).");
                }
                tr.Commit();
            }
            if (db.Pdmode == 0) db.Pdmode = 35;   // 점이 보이도록 (도면 설정 변경, Undo 가능)
            ed.WriteMessage("\n조종위치를 지정했습니다.\n");
        }

        [CommandMethod("GARCPICK")]
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
                ed.WriteMessage("\n도면 단위를 아직 모릅니다. 먼저 GARC로 한 번 검사하세요(단위 해석은 엔진이 합니다).\n");
                return;
            }
            var label = k.StringResult switch { "AP" => "AP(선미수선)", "FP" => "FP(선수수선)", _ => "기선(baseline)" };
            var res = ed.GetPoint(new PromptPointOptions($"\n{label} 위치를 클릭: "));
            if (res.Status != PromptStatus.OK) return;
            var pt = res.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var (key, value) = k.StringResult switch
            {
                "AP" => ("GARC_X_AP", pt.X * s),
                "FP" => ("GARC_X_FP", pt.X * s),
                _ => ("GARC_Z_BASE", pt.Y * s),
            };
            DrawingProps.Write(doc.Database, new Dictionary<string, string> { [key] = DrawingProps.M(value) });
            _panel?.Ship.SetField(key, DrawingProps.M(value));
            w?.MarkDirty();
            ed.WriteMessage($"\n{key} = {DrawingProps.M(value)} m\n");
        }

        // ── 표시·기록 ───────────────────────────────────────────────────────

        [CommandMethod("GARCTOGGLE")]
        public void Toggle()
        {
            var doc = Active;
            Overlay.Enabled = !Overlay.Enabled;
            _panel?.SetOverlayState(Overlay.Enabled);
            Redraw(doc);
            doc?.Editor.WriteMessage($"\n도면 표시 {(Overlay.Enabled ? "켜짐" : "꺼짐")}\n");
        }

        [CommandMethod("GARCLARGE")]
        public void LargeLabels()
        {
            var doc = Active;
            Overlay.Scale = Overlay.Scale > 1.0 ? 1.0 : 2.5;
            _panel?.SetLargeLabelsState(Overlay.Scale > 1.0);
            Redraw(doc);
            doc?.Editor.WriteMessage($"\n라벨 확대 {(Overlay.Scale > 1.0 ? "켜짐 (라벨 글자·선 두께·표식 크기 2.5배, 판정은 그대로)" : "꺼짐")}\n");
        }

        [CommandMethod("GARCREPORT")]
        public void Report()
        {
            var doc = Active;
            if (doc == null) return;
            if (!Watchers.TryGetValue(doc, out var w) || w.Last == null)
            {
                doc.Editor.WriteMessage("\n먼저 GARC로 검사하세요.\n");
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
                doc.Editor.WriteMessage($"\n리포트 저장: {file}\n");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n리포트 실패: " + ex.Message + "\n");
            }
        }

        // ── 내부 ────────────────────────────────────────────────────────────

        internal static DocWatcher Attach(Document doc)
        {
            if (Watchers.TryGetValue(doc, out var w)) return w;
            w = new DocWatcher(doc);
            w.Checked += x =>
            {
                if (x.Last == null) return;
                if (x.Doc == Active) _panel?.ShowResult(x.Last, DocName(x.Doc));
                StatusTray.Update(x.Last, DocName(x.Doc));
            };
            w.Failed += (_, msg) =>
            {
                _panel?.ShowEngineDown(msg);
                StatusTray.Set("down", "검토 불가 — 엔진 연결 안 됨");
                doc.Editor.WriteMessage("\nGA-RuleCheck: " + msg + "\n");
            };
            Watchers[doc] = w;
            return w;
        }

        internal static void Detach(Document doc)
        {
            if (!Watchers.TryGetValue(doc, out var w)) return;
            w.Dispose();
            Watchers.Remove(doc);
            Overlay.Clear();
            try { EngineClient.Reset(Extractor.DocId(doc)); } catch { }
        }

        internal static void DetachAll()
        {
            foreach (var doc in new List<Document>(Watchers.Keys)) Detach(doc);
        }

        /// 도면 탭을 바꾸면 그 도면의 결과·선박 정보로 팔레트를 바꾼다. 설정된 도면이면 자동 감시.
        internal static void OnActivated(Document? doc)
        {
            if (doc == null || _panel == null || _palette == null || !_palette.Visible) return;
            Overlay.Clear();
            _panel.Ship.Load(DrawingProps.Read(doc.Database));
            _panel.ClearHistory();
            if (Watchers.TryGetValue(doc, out var w) && w.Last != null)
            {
                _panel.ShowResult(w.Last, DocName(doc), record: false);
                Redraw(doc);
            }
            else if (DrawingProps.IsConfigured(doc.Database))
            {
                Attach(doc).MarkDirty();
                _panel.SetIdle("검사 중…", DocName(doc));
            }
            else
            {
                _panel.SetIdle("설정되지 않은 도면", DocName(doc), showSetup: true);
            }
        }

        private static void Redraw(Document? doc)
        {
            if (doc == null) return;
            if (Watchers.TryGetValue(doc, out var w) && w.Last != null)
                using (doc.LockDocument()) Overlay.Show(doc, w.Last);
            else Overlay.Clear();
        }

        private static void EnsurePalette()
        {
            if (_palette != null)
            {
                _palette.Visible = true;
                return;
            }
            _panel = new ResultPanel();
            _panel.RecheckRequested += () => { var d = Active; if (d != null) { EngineClient.EnsureRunning(); Attach(d).RunCheck(); } };
            _panel.StartRequested += () => Send("_GARCSETUP _GARC ");
            _panel.ToggleOverlayRequested += () => Send("_GARCTOGGLE ");
            _panel.ReportRequested += () => Send("_GARCREPORT ");
            _panel.FocusRequested += FocusTargets;
            _panel.HoverRequested += (h, on) => { var d = Active; if (d != null && Overlay.Enabled) Overlay.Hover(d, h, on); };
            _panel.FitRequested += rr =>
            {
                var d = Active;
                if (d != null && Watchers.TryGetValue(d, out var ww) && ww.Last?.UnitsScale is double sc) Overlay.Fit(d, rr, sc);
            };
            _panel.LargeLabelsRequested += () => Send("_GARCLARGE ");
            _panel.Ship.PickRequested += what => Send($"_GARCPICK {what} ");
            _panel.Ship.EyeRequested += () => Send("_GARCEYE ");
            _panel.Ship.SetupRequested += () => Send("_GARCSETUP ");
            _panel.Ship.ReloadRequested += () => { var d = Active; if (d != null) _panel.Ship.Load(DrawingProps.Read(d.Database)); };
            _panel.Ship.SaveRequested += values =>
            {
                var d = Active;
                if (d == null) return;
                using (d.LockDocument()) DrawingProps.Write(d.Database, values);
                Attach(d).MarkDirty();
                _panel.ShowTab("review");
            };

            _palette = new PaletteSet("GA RuleCheck", new Guid("3f6c2a71-5b8e-4d0a-9c1e-7a2b4d6e8f10"))
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
            Send("_GARC ");
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
                foreach (var h in handles)
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
