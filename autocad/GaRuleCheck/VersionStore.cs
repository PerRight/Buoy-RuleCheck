// 도면 버전 기록: 도면 옆 <도면이름>.brc\ 폴더(옛 이름 .garc는 옮겨 씀)에 versions.json(목록) + v{n}.raw.json(GA_ 레이어 스냅샷).
// 스냅샷은 엔진에 보내는 raw와 같은 형식이라, 비교는 엔진(/v1/compare)이 두 스냅샷으로 계산한다.
// 도면 내용(GA_ 레이어 좌표)이 들어 있으므로 PC 밖으로 보내지 않는다(.gitignore가 *.brc/·*.garc/를 막음).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GaRuleCheck
{
    public static class VersionStore
    {
        public static readonly IReadOnlyDictionary<string, string> ReasonKo = new Dictionary<string, string>
        {
            ["rule_violation"] = "규정 위반",
            ["feedback"] = "피드백",
            ["other"] = "기타",
        };

        public static string ReasonText(string code) => ReasonKo.TryGetValue(code, out var k) ? k : code;

        /// 저장된 도면: 도면 폴더\<이름>.brc. 아직 저장 안 된 도면: 내 문서\BuoyRuleCheck\<이름>.brc
        /// drawingPath = Document.Name, named = Document.IsNamedDrawing (AutoCAD 없이도 쓰게 값으로 받는다)
        /// 이름 변경(2026-10-26) 전 폴더 <이름>.garc(내 문서\GaRuleCheck)만 있으면 새 이름으로 옮긴다 — 버전 기록이 두 곳으로
        /// 갈라지지 않게. 옮기지 못하면(권한·사용 중) 옛 폴더를 그대로 쓴다.
        public static string FolderFor(string drawingPath, bool named)
        {
            var name = Path.GetFileNameWithoutExtension(drawingPath);
            var dir = Path.GetDirectoryName(drawingPath);
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            bool local = !named || string.IsNullOrEmpty(dir) || !Directory.Exists(dir);
            var fresh = Path.Combine(local ? Path.Combine(docs, "BuoyRuleCheck") : dir!, name + ".brc");
            var legacy = Path.Combine(local ? Path.Combine(docs, "GaRuleCheck") : dir!, name + ".garc");
            if (Directory.Exists(fresh) || !Directory.Exists(legacy)) return fresh;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);
                Directory.Move(legacy, fresh);
                return fresh;
            }
            catch { return legacy; }
        }

        public static VersionIndex Load(string folder)
        {
            var f = Path.Combine(folder, "versions.json");
            if (!File.Exists(f)) return new VersionIndex();
            try
            {
                return JsonSerializer.Deserialize<VersionIndex>(File.ReadAllText(f, Encoding.UTF8), Contract.Json) ?? new VersionIndex();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"버전 기록을 읽을 수 없습니다({f}): {ex.Message}", ex);
            }
        }

        public static RawDrawing LoadRaw(string folder, VersionEntry v)
        {
            var f = Path.Combine(folder, Path.GetFileName(v.File));   // 목록에 경로가 섞여 있어도 폴더 밖은 읽지 않는다
            return JsonSerializer.Deserialize<RawDrawing>(File.ReadAllText(f, Encoding.UTF8), Contract.Json)
                   ?? throw new InvalidOperationException($"v{v.Version} 스냅샷이 비어 있습니다.");
        }

        /// 스냅샷 지문: 객체(handle·레이어·종류·좌표·호·닫힘) + 도면 특성. 시각·출처는 넣지 않는다.
        public static string Fingerprint(RawDrawing raw)
        {
            var canon = new
            {
                raw.Insunits,
                props = raw.Props.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new[] { kv.Key, kv.Value }),
                ents = raw.Entities.OrderBy(e => e.Handle, StringComparer.Ordinal)
                    .Select(e => new { e.Handle, e.Layer, e.Type, e.Points, e.Bulges, e.Closed }),
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(canon, Contract.Json);
            return Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
        }

        public static VersionEntry? Latest(VersionIndex idx) => idx.Versions.OrderBy(v => v.Version).LastOrDefault();

        /// 마지막 버전과 내용이 같으면 true(새 버전 불필요).
        public static bool SameAsLatest(string folder, RawDrawing raw)
        {
            var idx = Load(folder);
            var last = Latest(idx);
            if (last == null) return false;
            var h = Fingerprint(raw);
            if (last.Hash != null) return last.Hash == h;
            try { return Fingerprint(LoadRaw(folder, last)) == h; } catch { return false; }
        }

        /// 새 버전 추가. 번호는 마지막 + 1. 목록은 임시 파일에 쓴 뒤 바꿔 끼운다(쓰다 끊겨도 목록이 깨지지 않게).
        public static VersionEntry Add(string folder, string drawingName, RawDrawing raw, VersionEntry meta, CheckResponse? check)
        {
            Directory.CreateDirectory(folder);
            var idx = Load(folder);
            int n = (Latest(idx)?.Version ?? 0) + 1;
            var entry = new VersionEntry
            {
                Version = n,
                File = $"v{n}.raw.json",
                Created = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                Author = meta.Author.Trim(),
                Reason = ReasonKo.ContainsKey(meta.Reason) ? meta.Reason : "other",
                Reference = meta.Reference.Trim(),
                Note = meta.Note.Trim(),
                Overall = check?.Overall,
                FailElements = check?.Results.Sum(r => r.Elements.Count(e => e.Status == "fail")),
                Hash = Fingerprint(raw),
            };
            WriteAtomic(Path.Combine(folder, entry.File), JsonSerializer.Serialize(raw, Contract.Json));
            idx.Drawing = drawingName;
            idx.Versions.Add(entry);
            WriteAtomic(Path.Combine(folder, "versions.json"), JsonSerializer.Serialize(idx, PrettyJson));
            return entry;
        }

        private static readonly JsonSerializerOptions PrettyJson = new(Contract.Json) { WriteIndented = true };

        private static void WriteAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }

        public static string Label(VersionEntry v) => $"v{v.Version}";

        /// 버전 고르기 칸(예: v1 · 10-18 손종민)
        public static string Describe(VersionEntry v)
        {
            var when = DateTime.TryParse(v.Created, out var t) ? t.ToString("MM-dd") : v.Created;
            return $"v{v.Version} · {when} {v.Author}";
        }

        /// 펼친 목록 한 줄(예: v1 · 10-18 14:32 · 손종민 · 규정 위반)
        public static string DescribeLong(VersionEntry v)
        {
            var when = DateTime.TryParse(v.Created, out var t) ? t.ToString("MM-dd HH:mm") : v.Created;
            return $"v{v.Version} · {when} · {v.Author} · {ReasonText(v.Reason)}";
        }
    }
}
