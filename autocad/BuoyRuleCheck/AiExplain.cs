// AI 설명 (2026-10-30 팀 결정, docs/ai-explain.md): 항목 카드 [설명] 창 · 생산팀 전달 '변경 설명' 창 · BRCAI 설정 창.
// 판정·수치는 엔진만 정한다. 이 파일은 엔진(/v1/explain)이 만든 문장을 보여 주고, AI 설명을 켜고 끄고, 키를 보관만 한다.
//   · AI 설명은 기본 꺼짐. 켜면 엔진이 숫자·이름을 뺀 본문(규칙 조항·판정 상태·고칠 방향·빈칸 이름)만 Claude API로 보내고,
//     돌아온 문장의 빈칸을 PC 안에서 엔진 값으로 채운다. [보낸 내용]으로 그 본문을 그대로 볼 수 있다.
//   · 키는 %APPDATA%\BuoyRuleCheck\ai.key 에 이 Windows 사용자 계정으로 암호화(DPAPI)해 둔다. 도면·ui.json에는 넣지 않는다.
//   · 엔진 호출은 다른 스레드에서(AI는 몇 초 걸림) — 창이 멈추지 않게, 결과는 창 스레드로 돌려 그린다.
using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    /// AI 설명 켜짐·키 보관
    public static class AiSettings
    {
        private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BuoyRuleCheck");
        private static string KeyPath => Path.Combine(Dir, "ai.key");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("BuoyRuleCheck.ai.key/1");

        public static bool Enabled => UiState.Current.AiEnabled;
        public static string Mode => Enabled ? "ai" : "template";

        public static void SetEnabled(bool on)
        {
            if (UiState.Current.AiEnabled == on) return;
            UiState.Current.AiEnabled = on;
            UiState.Current.Save();
        }

        /// 저장한 키(없거나 풀 수 없으면 null — 다른 사용자 계정·다른 PC에서 복사한 파일은 풀리지 않는다)
        public static string? LoadKey()
        {
            try
            {
                if (!File.Exists(KeyPath)) return null;
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), Entropy, DataProtectionScope.CurrentUser);
                var k = Encoding.UTF8.GetString(plain).Trim();
                return k.Length > 0 ? k : null;
            }
            catch { return null; }
        }

        public static void SaveKey(string key)
        {
            Directory.CreateDirectory(Dir);
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
            var tmp = KeyPath + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, KeyPath, overwrite: true);
        }

        public static void ClearKey()
        {
            try { if (File.Exists(KeyPath)) File.Delete(KeyPath); } catch { /* 지우기 실패는 다음에 다시 */ }
        }

        /// 키 모양만 확인(엔진 ai.py와 같은 규칙: 영문·숫자·-·_ 20~300자)
        public static bool LooksLikeKey(string k)
        {
            k = k.Trim();
            if (k.Length is < 20 or > 300) return false;
            foreach (var ch in k)
                if (!(ch is >= 'a' and <= 'z' || ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9' || ch == '-' || ch == '_')) return false;
            return true;
        }

        public static string Masked(string k) => k.Length <= 12 ? "•••" : k[..7] + "…" + k[^4..];

        /// 보낸(보낼) 본문을 읽기 좋게 — 한글을 \uXXXX로 바꾸지 않는다
        public static string Pretty(JsonElement? e)
        {
            if (e is not JsonElement el || el.ValueKind == JsonValueKind.Null) return "";
            var text = JsonSerializer.Serialize(el, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return text.Replace("\r\n", "\n").Replace("\n", "\r\n");   // .NET 9+는 줄바꿈이 이미 \r\n일 수 있다 — 한 번만
        }
    }

    /// 설명 창 공용 바탕(제목 · 상태 · 본문 · 출처 · 보낸 내용 · 고지)
    public abstract class AiFormBase : Form
    {
        protected readonly FlowLayoutPanel Flow = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(16, 14, 16, 8), BackColor = Theme.Bg,
        };
        protected readonly FlowLayoutPanel Buttons = new()
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46,
            Padding = new Padding(12, 8, 12, 8), BackColor = Theme.Card,
        };
        protected readonly Label SourceLine;
        protected readonly TextBox Json = new()
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Visible = false,
            BackColor = Theme.Input, ForeColor = Theme.Muted, BorderStyle = BorderStyle.FixedSingle, Font = Theme.MonoSmall,
            Height = 170,
        };
        protected readonly Label JsonCaption;
        protected readonly ToolTip Tips = new();
        protected const int W = 500;

        protected AiFormBase(string title, int height)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size(W + 32, height);
            SourceLine = Wrap("", Theme.Small, Theme.Faint);
            JsonCaption = Wrap("", Theme.Small, Theme.Muted);
            JsonCaption.Visible = false;
            Json.Width = W;
            Controls.Add(Flow);
            Controls.Add(Buttons);
        }

        protected static Label Wrap(string text, Font font, Color color) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(W, 0), Font = font, ForeColor = color,
            BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8),
            UseMnemonic = false,   // 흘수 조건 이름 등에 '&'가 있어도 그대로 보이게
        };

        /// 보이는 칸 크기가 바뀐 뒤(보낸 내용 펼침 등) — 창 높이를 맞추고 싶은 창이 덮어쓴다
        protected virtual void ContentChanged() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Tips.Dispose();
            base.Dispose(disposing);
        }

        /// [보낸 내용] 펼치기/접기. 응답에 본문이 없으면 preview(보내지 않음)를 다른 스레드에서 받아 보여 준다.
        protected void ShowJson(ExplainResponse? r, Func<ExplainResponse>? preview)
        {
            if (Json.Visible || JsonCaption.Visible) { Json.Visible = JsonCaption.Visible = false; ContentChanged(); return; }
            if (r?.Request != null || preview == null) { RenderJson(r); return; }
            JsonCaption.Text = "보낼 본문을 만드는 중…";
            JsonCaption.Visible = true;
            ContentChanged();
            RunAsync(preview, (res, err) =>
            {
                if (!JsonCaption.Visible) return;   // 그 사이 접음
                if (res == null) { JsonCaption.Text = "보낼 본문을 만들지 못했어요 — " + (err?.Message ?? "알 수 없는 오류"); ContentChanged(); return; }
                RenderJson(res);
            });
        }

        private void RenderJson(ExplainResponse? src)
        {
            JsonCaption.Text = src == null || src.Request == null ? "보낼 본문이 없습니다."
                : src.Sent ? $"Claude API로 보낸 본문 전체({src.Model}) — 숫자·도면 이름 없음, 키는 표시하지 않음(모델 이름·max_tokens는 고정값)"
                : $"AI를 켜면 보낼 본문 전체({src.Model}) — 아직 보내지 않았습니다(모델 이름·max_tokens는 고정값)";
            Json.Text = AiSettings.Pretty(src?.Request);
            JsonCaption.Visible = true;
            Json.Visible = src?.Request != null;
            ContentChanged();
        }

        /// [AI 설명 켜기…] — 이 창 위에 설정 창을 연다. 켜졌으면 true
        protected bool OpenSettings(Func<string, string?, ExplainResponse> fetch)
        {
            using var dlg = new AiSettingsDialog(fetch);
            dlg.ShowDialog(this);
            return AiSettings.Enabled;
        }

        /// 엔진 호출을 다른 스레드에서 하고 결과를 이 창 스레드로
        protected void RunAsync(Func<ExplainResponse> call, Action<ExplainResponse?, Exception?> done)
        {
            Task.Run(call).ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        var err = t.Exception?.GetBaseException();
                        done(err == null ? t.Result : null, err);
                    }));
                }
                catch (InvalidOperationException) { /* 창이 닫히는 중 */ }
            });
        }
    }

    /// 항목 카드 [설명]: 기본 설명을 바로, AI가 켜져 있으면 뒤에서 AI 설명을 받아 바꾼다.
    public sealed class ExplainDialog : AiFormBase
    {
        private readonly Func<string, string?, ExplainResponse> _fetchKey;   // (mode, 키) → 응답
        private readonly Label _status, _body;
        private readonly Font _bodyFont = new(Theme.FontName, 10f);
        private readonly Button _ai, _sent, _copy;
        private ExplainResponse? _cur;
        private bool _busy;

        public ExplainDialog(string ruleName, string status, Func<string, string?, ExplainResponse> fetch)
            : base($"판정 설명 — {ruleName}", 330)
        {
            _fetchKey = fetch;
            Flow.Controls.Add(Wrap(ruleName, Theme.Title, Theme.Text));
            _status = Wrap("● " + Theme.StatusName(status), Theme.Bold, Theme.Status(status));
            Flow.Controls.Add(_status);
            _body = Wrap("설명을 만드는 중…", _bodyFont, Theme.Text);
            _body.Margin = new Padding(0, 2, 0, 10);
            Flow.Controls.Add(_body);
            Flow.Controls.Add(SourceLine);
            Flow.Controls.Add(JsonCaption);
            Flow.Controls.Add(Json);
            Flow.Controls.Add(Wrap(UiText.Disclaimer, Theme.Small, Theme.Faint));

            var close = Theme.FlatButton("닫기");
            close.Click += (_, _) => Close();
            _copy = Theme.FlatButton("복사");
            _copy.Click += (_, _) => { try { Clipboard.SetText(_body.Text + "\r\n\r\n" + UiText.Disclaimer); } catch { /* 클립보드 잠김 */ } };
            _sent = Theme.FlatButton("보낸 내용");
            _sent.Click += (_, _) => ShowJson(_cur, () => Fetch("preview"));
            _ai = Theme.FlatButton(AiSettings.Enabled ? "AI로 다시 쓰기" : "AI 설명 켜기…");
            _ai.Click += (_, _) =>
            {
                if (!AiSettings.Enabled)
                {
                    if (!OpenSettings(_fetchKey)) return;
                    _ai.Text = "AI로 다시 쓰기";
                }
                StartAi();
            };
            Tips.SetToolTip(_sent, "AI에 보낸(또는 켜면 보낼) 본문을 그대로 보여 줍니다. 숫자·도면 이름은 들어 있지 않습니다.");
            Buttons.Controls.AddRange(new Control[] { close, _copy, _sent, _ai });
            CancelButton = close;

            Shown += (_, _) =>
            {
                FitHeight();
                // 기본 설명도 다른 스레드에서(엔진이 바쁘거나 막 켜지는 중이어도 AutoCAD가 멈추지 않게) → 끝나면 AI
                RunAsync(() => Fetch("template"), (r, err) =>
                {
                    if (r != null) Show(r);
                    else { _body.Text = "설명을 만들 수 없어요 — " + (err?.Message ?? "알 수 없는 오류"); _body.ForeColor = Theme.Warn; }
                    if (r != null && AiSettings.Enabled) StartAi();
                    FitHeight();
                });
            };
        }

        protected override void ContentChanged() => FitHeight();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bodyFont.Dispose();
            base.Dispose(disposing);
        }

        /// mode에 맞는 키(ai만 저장한 키)로 엔진 호출
        private ExplainResponse Fetch(string mode) => _fetchKey(mode, mode == "ai" ? AiSettings.LoadKey() : null);

        private void Show(ExplainResponse r)
        {
            _cur = r;
            _body.Text = r.Text;
            _body.ForeColor = Theme.Text;
            if (!string.IsNullOrEmpty(r.Status))   // 머리 상태도 엔진의 지금 결과로(카드를 연 뒤 도면이 바뀌었을 수 있음)
            {
                _status.Text = "● " + Theme.StatusName(r.Status);
                _status.ForeColor = Theme.Status(r.Status);
            }
            SourceLine.Text = UiText.ExplainSource(r, AiSettings.Enabled) + (r.Revision is int rev ? $" · rev {rev} 결과 기준" : "");
            SourceLine.ForeColor = r.Source == "ai" ? Theme.Muted : r.FallbackKo != null && r.Sent ? Theme.Warn : Theme.Faint;
            if (Json.Visible) { Json.Visible = JsonCaption.Visible = false; ShowJson(_cur, null); }
        }

        private void StartAi()
        {
            if (_busy) return;
            _busy = true;
            _ai.Enabled = false;
            SourceLine.Text = "AI 설명 쓰는 중… (보내는 것: 규칙 조항·판정 상태·고칠 방향·빈칸 이름)";
            SourceLine.ForeColor = Theme.Muted;
            RunAsync(() => Fetch("ai"), (r, err) =>
            {
                _busy = false;
                _ai.Enabled = true;
                if (r != null) Show(r);
                else { SourceLine.Text = "AI 설명을 받지 못했어요 — " + (err?.Message ?? "알 수 없는 오류"); SourceLine.ForeColor = Theme.Warn; }
                FitHeight();
            });
        }

        private void FitHeight()
        {
            int want = Flow.Padding.Vertical + Buttons.Height + 8;
            foreach (Control c in Flow.Controls)
                if (c.Visible) want += c.Height + c.Margin.Vertical;
            ClientSize = new Size(ClientSize.Width, Math.Clamp(want, 260, 720));
        }
    }

    /// 생산팀 전달 전에 A4 '변경 설명'을 고르고 고친다(선택). 기본 설명으로 채워 두고, AI가 켜져 있으면 [AI 초안].
    public sealed class ChangeNoteDialog : AiFormBase
    {
        private readonly Func<string, string?, ExplainResponse> _fetchKey;
        private readonly TextBox _box = new()
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = MaxNote, Height = 110, Width = W,
            BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body,
        };
        private readonly Button _ai;
        private ExplainResponse? _cur;
        private bool _settingText, _edited, _busy, _confirmed;
        private readonly Label _count;
        /// A4 상자(약 300자)에 맞춘 상한 — 넘치면 A4에서 글자를 줄여 넣는다(ExportSheet)
        public const int MaxNote = 300;

        public string Note { get; private set; } = "";
        public bool FromAi { get; private set; }

        public ChangeNoteDialog(string versions, string? previous, bool previousFromAi, Func<string, string?, ExplainResponse> fetch)
            : base($"변경 설명 — {versions}", 400)
        {
            _fetchKey = fetch;
            FromAi = previousFromAi;
            Flow.Controls.Add(Wrap("생산팀 전달 A4에 넣을 변경 설명", Theme.Title, Theme.Text));
            Flow.Controls.Add(Wrap("두세 문장. 고쳐 써도 되고 비워 두면 넣지 않습니다. 숫자는 엔진의 비교 결과 그대로입니다.", Theme.Small, Theme.Muted));
            Flow.Controls.Add(_box);
            _count = Wrap("", Theme.Small, Theme.Faint);
            _count.Margin = new Padding(0, 2, 0, 6);
            Flow.Controls.Add(_count);
            Flow.Controls.Add(SourceLine);
            Flow.Controls.Add(JsonCaption);
            Flow.Controls.Add(Json);
            _box.TextChanged += (_, _) =>
            {
                if (!_settingText) _edited = true;
                _count.Text = $"{_box.TextLength} / {MaxNote}자";
            };
            FormClosing += (_, e) =>
            {
                // 고쳐 쓴 글을 Esc·취소·닫기로 잃지 않게
                if (DialogResult == DialogResult.OK || _confirmed || !_edited || _box.Text.Trim().Length == 0) return;
                if (MessageBox.Show(this, "고쳐 쓴 변경 설명을 버리고 닫을까요?", "변경 설명", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    e.Cancel = true;
                else _confirmed = true;
            };

            var cancel = Theme.FlatButton("취소");
            cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; };
            var ok = Theme.FlatButton("A4에 넣고 저장", Theme.Info);
            ok.Click += (_, _) => { Note = _box.Text.Trim(); DialogResult = DialogResult.OK; };
            var none = Theme.FlatButton("설명 없이 저장");
            none.Click += (_, _) => { Note = ""; FromAi = false; DialogResult = DialogResult.OK; };
            var sent = Theme.FlatButton("보낸 내용");
            sent.Click += (_, _) => ShowJson(_cur, () => Fetch("preview"));
            Tips.SetToolTip(sent, "AI에 보낸(또는 켜면 보낼) 본문을 그대로 보여 줍니다. 숫자·도면·버전 이름은 들어 있지 않습니다.");
            _ai = Theme.FlatButton(AiSettings.Enabled ? "AI 초안" : "AI 설명 켜기…");
            _ai.Click += (_, _) =>
            {
                if (!AiSettings.Enabled)
                {
                    if (!OpenSettings(_fetchKey)) return;
                    _ai.Text = "AI 초안";
                }
                StartAi();
            };
            Buttons.Controls.AddRange(new Control[] { cancel, ok, none, sent, _ai });
            CancelButton = cancel;

            Shown += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(previous))
                {
                    SetText(previous);
                    SourceLine.Text = previousFromAi ? "앞서 고친 설명(AI 초안 바탕)" : "앞서 고친 설명";
                    return;
                }
                FromAi = false;
                // 엔진이 두 버전을 다시 비교해서 만든다(실도면은 몇 초) — 창이 멈추지 않게 다른 스레드에서
                SourceLine.Text = "기본 설명을 만드는 중…";
                _box.ReadOnly = true;
                RunAsync(() => Fetch("template"), (r, err) =>
                {
                    _box.ReadOnly = false;
                    if (r == null) { SourceLine.Text = "기본 설명을 만들 수 없어요 — " + (err?.Message ?? "알 수 없는 오류"); SourceLine.ForeColor = Theme.Warn; return; }
                    _cur = r;
                    if (!_edited) SetText(r.Text);
                    SourceLine.Text = UiText.ExplainSource(r, AiSettings.Enabled);
                });
            };
        }

        private ExplainResponse Fetch(string mode) => _fetchKey(mode, mode == "ai" ? AiSettings.LoadKey() : null);

        private void SetText(string t)
        {
            _settingText = true;
            _box.Text = t.Length > MaxNote ? t[..(MaxNote - 1)] + "…" : t;
            _settingText = false;
            _edited = false;
        }

        private void StartAi()
        {
            if (_busy) return;
            _busy = true;
            _ai.Enabled = false;
            SourceLine.Text = "AI 초안 쓰는 중… (보내는 것: 바뀐 항목 종류·해결/새 수정 소요 규칙 이름·빈칸 이름)";
            SourceLine.ForeColor = Theme.Muted;
            RunAsync(() => Fetch("ai"), (r, err) =>
            {
                _busy = false;
                _ai.Enabled = true;
                if (r == null)
                {
                    SourceLine.Text = "AI 초안을 받지 못했어요 — " + (err?.Message ?? "알 수 없는 오류");
                    SourceLine.ForeColor = Theme.Warn;
                    return;
                }
                _cur = r;
                SourceLine.Text = UiText.ExplainSource(r, true);
                SourceLine.ForeColor = r.Source == "ai" ? Theme.Muted : Theme.Warn;
                if (r.Source != "ai") return;
                if (_edited && _box.Text.Trim().Length > 0 &&
                    MessageBox.Show(this, "고쳐 쓴 설명을 AI 초안으로 바꿀까요?", "변경 설명", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                SetText(r.Text);
                FromAi = true;
            });
        }
    }

    /// BRCAI: AI 설명 켜기/끄기 · 키 저장 · 보낼 본문 미리보기 · 연결 시험
    public sealed class AiSettingsDialog : AiFormBase
    {
        private readonly Func<string, string?, ExplainResponse>? _fetch;   // (mode, key) — 검사한 도면이 없으면 null
        private readonly InkCheck _on = new("AI 설명 켜기");
        private readonly TextBox _key = new()
        {
            Width = W, UseSystemPasswordChar = true, BackColor = Theme.Input, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body,
        };
        private readonly Label _keyState;
        private readonly Button _test;
        private bool _busy;

        public AiSettingsDialog(Func<string, string?, ExplainResponse>? fetch) : base("AI 설명 설정", 470)
        {
            _fetch = fetch;
            Flow.Controls.Add(Wrap("AI 설명", Theme.Title, Theme.Text));
            Flow.Controls.Add(Wrap(UiText.AiAbout, Theme.Body, Theme.Text));
            Flow.Controls.Add(Wrap(UiText.AiSends, Theme.Small, Theme.Muted));
            Flow.Controls.Add(Wrap(UiText.AiKeeps, Theme.Small, Theme.Muted));
            _on.Checked = AiSettings.Enabled;
            _on.Toggled += () => _on.Checked = !_on.Checked;
            _on.Margin = new Padding(0, 4, 0, 6);
            Flow.Controls.Add(_on);
            Flow.Controls.Add(Wrap("API 키(Anthropic) — 이 Windows 사용자 계정으로 암호화해 이 PC에만 저장합니다.", Theme.Bold, Theme.Text));
            Flow.Controls.Add(_key);
            var saved = AiSettings.LoadKey();
            _keyState = Wrap(saved != null ? $"저장된 키: {AiSettings.Masked(saved)} (새로 넣으면 바꿉니다)"
                                            : "저장된 키 없음 — 엔진 환경변수 BRC_AI_KEY(없으면 ANTHROPIC_API_KEY)가 있으면 그것을 씁니다.", Theme.Small, Theme.Faint);
            Flow.Controls.Add(_keyState);
            Flow.Controls.Add(SourceLine);
            Flow.Controls.Add(JsonCaption);
            Flow.Controls.Add(Json);
            if (fetch == null)
                SourceLine.Text = "미리보기·연결 시험은 검사한 도면이 있을 때 할 수 있어요(BRC로 검사한 뒤 다시 BRCAI).";

            var cancel = Theme.FlatButton("취소");
            cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
            var save = Theme.FlatButton("저장", Theme.Info);
            save.Click += (_, _) => Save();
            var clear = Theme.FlatButton("키 지우기");
            clear.Click += (_, _) =>
            {
                AiSettings.ClearKey();
                _key.Text = "";
                _keyState.Text = "저장된 키를 지웠습니다.";
            };
            var preview = Theme.FlatButton("보낼 내용 미리보기");
            preview.Enabled = fetch != null;
            preview.Click += (_, _) => ShowJson(null, () => _fetch!("preview", null));
            Tips.SetToolTip(preview, "지금 도면의 첫 항목으로, AI를 켜면 보낼 본문 전체를 보여 줍니다(보내지 않음).");
            _test = Theme.FlatButton("연결 시험");
            _test.Enabled = fetch != null;
            _test.Click += (_, _) => Test();
            Tips.SetToolTip(_test, "지금 도면의 첫 항목으로 AI 설명을 한 번 받아 봅니다(위 본문만 보냅니다).");
            Buttons.Controls.AddRange(new Control[] { cancel, save, clear, preview, _test });
            CancelButton = cancel;
        }

        private string? TypedOrSavedKey()
        {
            var t = _key.Text.Trim();
            return t.Length > 0 ? t : AiSettings.LoadKey();
        }

        private void Test()
        {
            if (_busy || _fetch == null) return;
            var t = _key.Text.Trim();
            if (t.Length > 0 && !AiSettings.LooksLikeKey(t)) { SourceLine.Text = "키 모양이 맞지 않아요(영문·숫자·-·_ 20자 이상)."; SourceLine.ForeColor = Theme.Warn; return; }
            _busy = true;
            _test.Enabled = false;
            SourceLine.Text = "연결 시험 중…";
            SourceLine.ForeColor = Theme.Muted;
            var key = TypedOrSavedKey();
            RunAsync(() => _fetch("ai", key), (r, err) =>
            {
                _busy = false;
                _test.Enabled = true;
                if (r == null) { SourceLine.Text = "시험 실패 — " + (err?.Message ?? "알 수 없는 오류"); SourceLine.ForeColor = Theme.Warn; return; }
                SourceLine.Text = r.Source == "ai" ? $"연결됨 — AI 설명을 받았습니다({r.Model}). 예: {r.Text}" : "AI 설명을 받지 못함 — " + (r.FallbackKo ?? "기본 설명");
                SourceLine.ForeColor = r.Source == "ai" ? Theme.Pass : Theme.Warn;
            });
        }

        private void Save()
        {
            var t = _key.Text.Trim();
            if (t.Length > 0)
            {
                if (!AiSettings.LooksLikeKey(t))
                {
                    MessageBox.Show(this, "키 모양이 맞지 않아요(영문·숫자·-·_ 20자 이상).", "AI 설명", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { AiSettings.SaveKey(t); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "키를 저장하지 못했어요 — " + ex.Message, "AI 설명", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            AiSettings.SetEnabled(_on.Checked);
            DialogResult = DialogResult.OK;
        }
    }
}
