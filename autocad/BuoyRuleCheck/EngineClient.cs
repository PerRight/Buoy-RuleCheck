// 로컬 엔진 서버(python -m brc serve) 호출. 연결 실패는 예외로 올려 '검토 불가'로 표시한다(초록 금지).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BuoyRuleCheck
{
    public sealed class EngineException : Exception
    {
        public EngineException(string message, Exception? inner = null) : base(message, inner) { }
    }

    public static class EngineClient
    {
        private const string DefaultUrl = "http://127.0.0.1:8765";

        /// 환경변수: 새 이름 BRC_*, 없으면 옛 이름 GARC_*(2026-10-26 이전 설치)
        private static string? Env(string name)
        {
            var v = Environment.GetEnvironmentVariable("BRC_" + name);
            return string.IsNullOrEmpty(v) ? Environment.GetEnvironmentVariable("GARC_" + name) : v;
        }

        /// 엔진 주소. BRC_ENGINE_URL(옛 GARC_ENGINE_URL)은 루프백(127.0.0.1·localhost·::1) http 주소일 때만 쓴다 —
        /// 도면 데이터가 PC 밖 서버로 나가지 않게 하는 원칙(엔진은 127.0.0.1 전용)을 설정으로 깨지 못하게 한다.
        public static readonly string BaseUrl = ResolveBaseUrl();

        private static string ResolveBaseUrl()
        {
            var env = Env("ENGINE_URL");
            if (string.IsNullOrWhiteSpace(env)) return DefaultUrl;
            // Uri.IsLoopback은 127.x 전체 등 범위가 넓어서 이름을 직접 고정한다
            if (Uri.TryCreate(env.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttp
                && (u.Host == "127.0.0.1" || u.Host == "localhost" || u.Host == "[::1]"))
                return "http://" + u.Authority;   // Authority = 호스트:포트 (사용자 정보·경로 제외)
            return DefaultUrl;
        }

        // 시스템 프록시를 쓰지 않는다 — 루프백 요청이 프록시 설정 때문에 PC 밖으로 나가는 일이 없게
        // 리다이렉트도 따라가지 않는다(로컬 포트를 가로챈 다른 프로세스가 요청을 밖으로 돌리는 경로 차단)
        private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30),   // 요청마다 CancellationToken으로 짧게 끊는다(검사 3초, 비교 30초)
        };

        private static bool _startTried;

        /// 이 플러그인이 기대하는 엔진의 최소 버전 — 표시용 필드(anchor·fix·비교 제목)와 새 특성 이름 BRC_*를 아는 엔진.
        /// 업그레이드 뒤 옛 엔진(python -m garc serve)이 떠 있으면 BRC_ 특성을 못 읽어 '입력 누락'으로 잘못 안내하므로
        /// 처음 연결할 때 /v1/health의 version을 보고, 옛 엔진이면 판정하지 않고 엔진을 다시 켜라고 알린다.
        public const string MinEngineVersion = "0.5.0";
        private static volatile bool _compatible;

        public static CheckResponse Check(string docId, RawDrawing raw)
        {
            var body = JsonSerializer.Serialize(new CheckRequest { DocId = docId, Raw = raw }, Contract.Json);
            // 실도면은 장애물 좌표가 수만 점이라 보내고 읽는 데 시간이 더 든다 → 좌표 2만 점마다 1초 더(최대 20초)
            int pts = 0;
            foreach (var e in raw.Entities) pts += e.Points.Count;
            var text = Post("/v1/check", body, TimeSpan.FromSeconds(Math.Min(3 + pts / 20000.0, 20)));
            return JsonSerializer.Deserialize<CheckResponse>(text, Contract.Json)
                   ?? throw new EngineException("엔진 응답을 읽을 수 없습니다.");
        }

        /// 두 버전 스냅샷 비교 — 변경 항목·해결된 위반·형상(엔진이 계산, 플러그인은 그리기만).
        public static CompareResponse Compare(RawDrawing a, RawDrawing b, string labelA, string labelB)
        {
            var body = JsonSerializer.Serialize(new CompareRequest { A = a, B = b, Labels = new() { labelA, labelB } }, Contract.Json);
            var text = Post("/v1/compare", body, TimeSpan.FromSeconds(30));
            return JsonSerializer.Deserialize<CompareResponse>(text, Contract.Json)
                   ?? throw new EngineException("비교 응답을 읽을 수 없습니다.");
        }

        /// 도면 문자(TEXT/MTEXT)에서 주요 치수·축척 후보 — 실도면 연결 마법사용(엔진이 읽고, 사람이 확인).
        public static ParticularsResponse Particulars(List<TextDto> texts)
        {
            var body = JsonSerializer.Serialize(new ParticularsRequest { Texts = texts }, Contract.Json);
            var text = Post("/v1/particulars", body, TimeSpan.FromSeconds(10));
            return JsonSerializer.Deserialize<ParticularsResponse>(text, Contract.Json)
                   ?? throw new EngineException("치수표 응답을 읽을 수 없습니다.");
        }

        /// 마지막 검사 결과와 이력으로 만든 HTML 리포트.
        public static string Report(string docId, string title)
        {
            var text = Post("/v1/report", JsonSerializer.Serialize(new { doc_id = docId, title }), TimeSpan.FromSeconds(10));
            return JsonSerializer.Deserialize<ReportResponse>(text, Contract.Json)?.Html
                   ?? throw new EngineException("리포트 응답을 읽을 수 없습니다.");
        }

        public static void Reset(string docId)
        {
            try { Post("/v1/reset", JsonSerializer.Serialize(new { doc_id = docId }), TimeSpan.FromSeconds(3)); } catch { /* 무시 */ }
        }

        public static string Health()
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resp = Task.Run(() => Http.GetAsync("/v1/health", cts.Token)).GetAwaiter().GetResult();
                return Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new EngineException($"엔진 서버({BaseUrl})에 연결할 수 없습니다.", ex);
            }
        }

        /// 엔진 버전 확인(한 번 맞으면 기억, 연결이 끊기면 다시 확인). 연결 실패는 여기서 다루지 않는다(Post가 같은 방식으로 처리).
        private static void EnsureCompatible()
        {
            if (_compatible) return;
            string text;
            try { text = Health(); } catch (EngineException) { return; }
            var ver = EngineVersionOf(text);
            if (ver != null && ver >= Version.Parse(MinEngineVersion)) { _compatible = true; return; }
            throw new EngineException(
                $"옛 엔진({(ver?.ToString() ?? "버전 모름")})이 켜져 있어 판정하지 않습니다 — 이 플러그인은 엔진 {MinEngineVersion} 이상이 필요합니다. " +
                "엔진 창(python -m garc serve)을 닫고 [다시 검사]를 누르세요. 새 엔진(python -m brc serve)이 자동으로 켜집니다.");
        }

        /// /v1/health 응답의 version("0.5.0") → Version. 읽을 수 없으면 null(= 옛 엔진으로 본다).
        public static Version? EngineVersionOf(string healthJson)
        {
            try
            {
                using var d = JsonDocument.Parse(healthJson);
                if (!d.RootElement.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) return null;
                var s = v.GetString() ?? "";
                int end = 0;
                while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;   // "0.5.0rc1" → "0.5.0"
                return Version.TryParse(s[..end].TrimEnd('.'), out var got) ? got : null;
            }
            catch (JsonException) { return null; }
        }

        private static string Post(string path, string json, TimeSpan? timeout = null)
        {
            EnsureCompatible();
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var cts = new System.Threading.CancellationTokenSource(timeout ?? Http.Timeout);
                var resp = Task.Run(() => Http.PostAsync(path, content, cts.Token)).GetAwaiter().GetResult();
                var text = Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode)
                    throw new EngineException($"엔진 오류 {(int)resp.StatusCode}: {text}");
                return text;
            }
            catch (EngineException) { throw; }
            catch (Exception ex)
            {
                _compatible = false;   // 다시 켜진 엔진의 버전을 다음에 다시 본다
                TryStartEngine();
                throw new EngineException(
                    $"엔진 서버({BaseUrl})에 연결할 수 없습니다. 'python -m brc serve'를 실행하세요.", ex);
            }
        }

        /// 엔진이 꺼져 있으면 BRC_ENGINE_DIR(engine 폴더, 옛 GARC_ENGINE_DIR)에서 자동 실행하고 최대 ~5초 기다린다.
        /// install.ps1이 BRC_ENGINE_DIR·BRC_PYTHON 환경변수를 설정한다.
        public static bool EnsureRunning()
        {
            if (IsUp()) return true;
            _startTried = false;
            TryStartEngine();
            for (int i = 0; i < 25; i++)
            {
                System.Threading.Thread.Sleep(200);
                if (IsUp()) return true;
            }
            return false;
        }

        private static bool IsUp()
        {
            try { Health(); return true; } catch { return false; }
        }

        private static void TryStartEngine()
        {
            if (_startTried) return;
            _startTried = true;
            var dir = Env("ENGINE_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            var python = Env("PYTHON");
            if (string.IsNullOrEmpty(python)) python = "python";
            // 새 패키지 brc가 있으면 그것을, 옛 엔진 폴더면 garc(같은 서버)
            string pkg = System.IO.Directory.Exists(System.IO.Path.Combine(dir, "brc")) ? "brc" : "garc";
            try
            {
                Process.Start(new ProcessStartInfo(python, $"-m {pkg} serve")
                {
                    WorkingDirectory = dir,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                });
            }
            catch { /* 수동 실행 안내로 대체 */ }
        }
    }
}
