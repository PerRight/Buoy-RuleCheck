// 도면 변경 감지 → (디바운스) → 전체 스냅샷 재추출 → 엔진 검사 → 표시.
//
// 동기화 원칙: 이벤트는 '다시 검사해야 한다'는 신호(dirty)로만 쓴다. 무엇이 바뀌었는지는 엔진이
// 이전 스냅샷과 비교해서 계산한다. 그래서 Undo/Redo/복사/레이어 변경 어떤 경로로 바뀌어도
// 다음 검사의 스냅샷이 곧 진실이 되고, 결과 캐시도 엔진에서 입력 해시로 맞춰진다.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace GaRuleCheck
{
    public sealed class DocWatcher : IDisposable
    {
        private static readonly HashSet<string> RecheckCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "DWGPROPS", "UNDO", "U", "REDO", "MREDO", "LAYER", "-LAYER", "RENAME", "PROPERTIES",
        };
        private const int DebounceMs = 300;

        public Document Doc { get; }
        public CheckResponse? Last { get; private set; }
        public event Action<DocWatcher>? Checked;
        public event Action<DocWatcher, string>? Failed;

        private bool _dirty = true;
        private DateTime _lastChange = DateTime.MinValue;
        private bool _busy;
        private HashSet<string> _lastHandles = new();
        private Dictionary<string, string> _tips = new();   // handle → 마우스 툴팁 문구

        public DocWatcher(Document doc)
        {
            Doc = doc;
            var db = doc.Database;
            db.ObjectAppended += OnObject;
            db.ObjectModified += OnObject;
            db.ObjectErased += OnErased;
            doc.CommandEnded += OnCommandEnded;
            doc.Editor.PointMonitor += OnPointMonitor;
            AcApp.Idle += OnIdle;
        }

        public void Dispose()
        {
            AcApp.Idle -= OnIdle;
            try
            {
                var db = Doc.Database;
                db.ObjectAppended -= OnObject;
                db.ObjectModified -= OnObject;
                db.ObjectErased -= OnErased;
                Doc.CommandEnded -= OnCommandEnded;
                Doc.Editor.PointMonitor -= OnPointMonitor;
            }
            catch { /* 문서가 이미 닫힘 */ }
        }

        public void MarkDirty()
        {
            _dirty = true;
            _lastChange = DateTime.UtcNow;
        }

        private void OnObject(object sender, ObjectEventArgs e) => Consider(e.DBObject);
        private void OnErased(object sender, ObjectErasedEventArgs e) => Consider(e.DBObject);

        private void Consider(DBObject obj)
        {
            try
            {
                if (obj is not Entity ent) return;
                // 감시 레이어에 있거나, 직전 스냅샷에 있던 객체(= 다른 레이어로 옮겨진 경우)면 다시 검사.
                // 'GA'로 시작하는 다른 레이어(GA-OBST·전각 ＧＡ＿OBST 같은 비슷한 이름)도 신호로 쓴다 — 판단은 엔진(layer_counts)이 한다.
                if (Contract.IsWatchedLayer(ent.Layer) || _lastHandles.Contains(ent.Handle.ToString())
                    || StartsWithGa(ent.Layer))
                    MarkDirty();
            }
            catch
            {
                MarkDirty(); // 판단이 안 되면 보수적으로 다시 검사
            }
        }

        /// 엔진의 유사 레이어 비교와 같은 정규화(NFKC·대문자·ASCII 영숫자만) 후 'GA'로 시작하는지. 재검사 신호용(판단 아님).
        private static bool StartsWithGa(string layer)
        {
            var n = new string(layer.Normalize(NormalizationForm.FormKC).ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
            return n.StartsWith("GA", StringComparison.Ordinal);
        }

        /// 위반·검토 불가 객체 위에 커서를 올리면 규칙·수치·제안을 툴팁으로 보여준다.
        private void OnPointMonitor(object sender, PointMonitorEventArgs e)
        {
            if (_tips.Count == 0) return;
            try
            {
                var paths = e.Context.GetPickedEntities();
                if (paths == null) return;
                foreach (var path in paths)
                    foreach (var id in path.GetObjectIds())
                        if (_tips.TryGetValue(id.Handle.ToString(), out var tip))
                        {
                            e.AppendToolTipText(tip);
                            return;
                        }
            }
            catch { /* 툴팁은 부가 기능 — 실패해도 무시 */ }
        }

        private void BuildTips()
        {
            var tips = new Dictionary<string, string>();
            if (Last != null)
                foreach (var r in Last.Results)
                {
                    if (r.Status is not ("fail" or "review")) continue;
                    var text = $"[{r.StatusKo}] {r.TitleKo}\n{r.MessageKo}"
                               + (string.IsNullOrEmpty(r.SuggestionKo) ? "" : "\n제안: " + r.SuggestionKo);
                    foreach (var h in r.Targets)
                        tips[h] = tips.TryGetValue(h, out var prev) ? prev + "\n\n" + text : text;
                }
            _tips = tips;
        }

        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (RecheckCommands.Contains(e.GlobalCommandName)) MarkDirty();
        }

        private void OnIdle(object? sender, EventArgs e)
        {
            if (!_dirty || _busy) return;
            if (AcApp.DocumentManager.MdiActiveDocument != Doc) return;
            if (!string.IsNullOrEmpty(Doc.CommandInProgress)) return;
            if ((DateTime.UtcNow - _lastChange).TotalMilliseconds < DebounceMs) return;
            RunCheck();
        }

        public void RunCheck()
        {
            _busy = true;
            _dirty = false;
            try
            {
                RawDrawing raw;
                using (Doc.LockDocument())
                {
                    raw = Extractor.Extract(Doc);
                }
                _lastHandles = raw.Entities.Select(x => x.Handle).ToHashSet();
                Last = EngineClient.Check(Extractor.DocId(Doc), raw);
                BuildTips();
                using (Doc.LockDocument())
                {
                    Overlay.Show(Doc, Last);
                }
                Checked?.Invoke(this);
            }
            catch (EngineException ex)
            {
                Last = null;
                _tips = new();
                Overlay.Clear();
                Failed?.Invoke(this, ex.Message);
            }
            catch (Exception ex)
            {
                Last = null;
                _tips = new();   // 이전 결과의 툴팁이 남지 않게
                Overlay.Clear();
                Failed?.Invoke(this, "플러그인 오류: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
