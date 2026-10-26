// 저장할 때 버전 정보 입력: 작성자, 수정 사유(규정 위반 / 피드백 / 기타), 관련 규정 조항 또는 피드백 내용, 메모.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BuoyRuleCheck
{
    public sealed class VersionDialog : Form
    {
        private readonly TextBox _author = Input();
        private readonly RadioButton _rule = Radio("규정 위반");
        private readonly RadioButton _feedback = Radio("피드백");
        private readonly RadioButton _other = Radio("기타");
        private readonly TextBox _reference = Input(multiline: true);
        private readonly TextBox _note = Input(multiline: true);
        private readonly Label _refLabel;

        public VersionEntry Result { get; private set; } = new();

        public VersionDialog(int nextVersion, string drawingName, string author, string reason, string reference, string summary)
        {
            Text = $"버전 기록 — v{nextVersion}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size(460, 430);
            KeyPreview = true;

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(16, 14, 16, 8), AutoScroll = true,
            };
            Controls.Add(flow);

            flow.Controls.Add(Theme.Label($"{drawingName} 저장 — 새 버전 v{nextVersion}", Theme.Title));
            flow.Controls.Add(Theme.Label(summary, Theme.Small, Theme.Muted));

            flow.Controls.Add(Caption("작성자"));
            _author.Text = author;
            flow.Controls.Add(_author);

            flow.Controls.Add(Caption("수정 사유"));
            var reasons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 4), BackColor = Color.Transparent };
            reasons.Controls.AddRange(new Control[] { _rule, _feedback, _other });
            flow.Controls.Add(reasons);
            (reason switch { "rule_violation" => _rule, "feedback" => _feedback, _ => _other }).Checked = true;

            _refLabel = Caption("관련 규정 조항");
            flow.Controls.Add(_refLabel);
            _reference.Text = reference;
            _reference.Height = 56;
            flow.Controls.Add(_reference);

            flow.Controls.Add(Caption("메모 (선택)"));
            _note.Height = 56;
            flow.Controls.Add(_note);

            void UpdateLabel() => _refLabel.Text = _feedback.Checked ? "피드백 내용" : _rule.Checked ? "관련 규정 조항" : "관련 규정 조항 또는 피드백 내용";
            _rule.CheckedChanged += (_, _) => UpdateLabel();
            _feedback.CheckedChanged += (_, _) => UpdateLabel();
            _other.CheckedChanged += (_, _) => UpdateLabel();
            UpdateLabel();

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46,
                Padding = new Padding(12, 8, 12, 8), BackColor = Theme.Card,
            };
            var ok = Theme.FlatButton("버전 기록", Theme.Info);
            var skip = Theme.FlatButton("이번엔 건너뛰기");
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_author.Text))
                {
                    _author.Focus();
                    MessageBox.Show(this, "작성자를 입력하세요.", "버전 기록", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Result = new VersionEntry
                {
                    Author = _author.Text,
                    Reason = _rule.Checked ? "rule_violation" : _feedback.Checked ? "feedback" : "other",
                    Reference = _reference.Text,
                    Note = _note.Text,
                };
                DialogResult = DialogResult.OK;
            };
            skip.Click += (_, _) => DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(skip);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = skip;
        }

        private static Label Caption(string text)
        {
            var l = Theme.Label(text, Theme.Bold);
            l.Margin = new Padding(0, 10, 0, 3);
            return l;
        }

        private static TextBox Input(bool multiline = false) => new()
        {
            Width = 420, Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            BackColor = Theme.Input, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body,
        };

        private static RadioButton Radio(string text) => new()
        {
            Text = text, AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Margin = new Padding(0, 2, 14, 2),
        };
    }
}
