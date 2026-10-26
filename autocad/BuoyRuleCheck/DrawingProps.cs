// 도면 사용자 정의 특성(BRC_*, 옛 이름 GARC_*도 읽음) 읽기/쓰기와 초기 설정(레이어·라벨 문자 스타일).
// 값의 해석(단위 변환·검증)은 엔진이 한다. 여기서는 문자열 그대로 저장만 한다.
// 이름 변경(2026-10-26): 쓸 때는 새 이름으로 쓰고 같은 뜻의 옛 이름 특성은 지운다(옮겨 쓰기). 엔진에는 도면에 있는 그대로 보내
// 새·옛 이름이 둘 다 있고 값이 다르면 엔진이 '확인 필요'로 멈추게 한다(여기서 한쪽을 고르지 않는다).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace BuoyRuleCheck
{
    public static partial class DrawingProps
    {
        public const string LabelStyle = "BRC_LABEL";
        private const string LegacyLabelStyle = "GARC_LABEL";

        public static readonly Dictionary<string, short> LayerColors = new()
        {
            ["GA_HULL"] = 7, ["GA_EYE"] = 3, ["GA_OBST"] = 30, ["GA_BHD_COLL"] = 1, ["GA_BULB"] = 5,
        };

        public static Dictionary<string, string> Read(Database db)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IDictionaryEnumerator en = db.SummaryInfo.CustomProperties;
            while (en.MoveNext())
                if (en.Key is string k) d[k] = en.Value?.ToString() ?? "";
            return d;
        }

        /// 화면용: 옛 이름(GARC_*)을 새 이름(BRC_*)으로 맞춘 특성. 둘 다 있으면 새 이름 값.
        public static Dictionary<string, string> ReadCanonical(Database db) => Canonical(Read(db));

        public static bool IsConfigured(Database db)
        {
            var d = ReadCanonical(db);
            return d.TryGetValue(Key("LOA"), out var v) && !string.IsNullOrWhiteSpace(v);
        }

        /// 호출자가 문서 잠금(LockDocument)을 잡고 있어야 한다. 빈 값은 키를 지운다.
        /// 새 이름(BRC_*)으로 쓰면서 같은 뜻의 옛 이름(GARC_*) 특성은 지운다 — 대소문자가 다른 같은 키도.
        public static void Write(Database db, IDictionary<string, string> values)
        {
            var b = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
            var table = b.CustomPropertyTable;
            var existing = new List<string>();
            foreach (DictionaryEntry e in table) if (e.Key is string k) existing.Add(k);
            foreach (var kv in values)
            {
                string key = ToNewName(kv.Key);
                foreach (var old in existing)
                    if (table.Contains(old) && ToNewName(old).Equals(key, StringComparison.OrdinalIgnoreCase)) table.Remove(old);
                if (!string.IsNullOrWhiteSpace(kv.Value)) table.Add(key, kv.Value.Trim());
            }
            db.SummaryInfo = b.ToDatabaseSummaryInfo();
        }

        /// 레이어 하나가 없으면 만든다(색은 LayerColors). 사용자가 명시적으로 고른 동작(조종위치 지정)에서만 부른다.
        public static void EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci,
                                                                     LayerColors.TryGetValue(name, out var aci) ? aci : (short)7),
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        /// BRCSETUP(초기 설정) 전용: GA_ 레이어, 라벨용 문자 스타일, 빈 특성 틀을 만든다. 이미 있으면 건드리지 않는다.
        /// 검사·표시·연결 마법사는 이것을 부르지 않는다(도면을 바꾸지 않음).
        public static void EnsureSetup(Document doc, bool props)
        {
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (var kv in LayerColors)
                {
                    if (lt.Has(kv.Key)) continue;
                    lt.UpgradeOpen();
                    var rec = new LayerTableRecord
                    {
                        Name = kv.Key,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, kv.Value),
                    };
                    lt.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                EnsureLabelStyle(tr, db);
                tr.Commit();

                if (props)
                {
                    var cur = ReadCanonical(db);
                    var add = new Dictionary<string, string>();
                    foreach (var (key, value) in new[] { (Key("X_AP"), "0"), (Key("Z_BASE"), "0"), (Key("EYE_HEIGHT"), "1.8"),
                                                         (Key("CONDITIONS"), "만재 출항:/; 밸러스트 도착:/") })
                        if (!cur.ContainsKey(key)) add[key] = value;
                    if (add.Count > 0) Write(db, add);
                }
            }
        }

        /// 한글 라벨이 ???로 보이지 않도록 맑은 고딕 TrueType 문자 스타일.
        public static ObjectId EnsureLabelStyle(Transaction tr, Database db)
        {
            var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (st.Has(LabelStyle)) return st[LabelStyle];
            if (st.Has(LegacyLabelStyle)) return st[LegacyLabelStyle];   // 옛 도면의 라벨 스타일을 그대로 쓴다(새로 만들지 않음)
            st.UpgradeOpen();
            var rec = new TextStyleTableRecord
            {
                Name = LabelStyle,
                FileName = "malgun.ttf",
                Font = new Autodesk.AutoCAD.GraphicsInterface.FontDescriptor("Malgun Gothic", false, false, 129, 34),
            };
            var id = st.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        public static ObjectId LabelStyleId(Transaction tr, Database db)
        {
            var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            return st.Has(LabelStyle) ? st[LabelStyle] : st.Has(LegacyLabelStyle) ? st[LegacyLabelStyle] : db.Textstyle;
        }
    }
}
