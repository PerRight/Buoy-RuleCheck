// 도면 사용자 정의 특성(GARC_*) 읽기/쓰기와 초기 설정(레이어·라벨 문자 스타일).
// 값의 해석(단위 변환·검증)은 엔진이 한다. 여기서는 문자열 그대로 저장만 한다.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace GaRuleCheck
{
    public static partial class DrawingProps
    {
        public const string LabelStyle = "GARC_LABEL";

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

        public static bool IsConfigured(Database db)
        {
            var d = Read(db);
            return d.TryGetValue("GARC_LOA", out var v) && !string.IsNullOrWhiteSpace(v);
        }

        /// 호출자가 문서 잠금(LockDocument)을 잡고 있어야 한다. 빈 값은 키를 지운다.
        public static void Write(Database db, IDictionary<string, string> values)
        {
            var b = new DatabaseSummaryInfoBuilder(db.SummaryInfo);
            var table = b.CustomPropertyTable;
            foreach (var kv in values)
            {
                if (table.Contains(kv.Key)) table.Remove(kv.Key);
                if (!string.IsNullOrWhiteSpace(kv.Value)) table.Add(kv.Key, kv.Value.Trim());
            }
            db.SummaryInfo = b.ToDatabaseSummaryInfo();
        }


        /// GA_ 레이어, 라벨용 문자 스타일, 빈 특성 틀을 만든다. 이미 있으면 건드리지 않는다.
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
                    var cur = Read(db);
                    var add = new Dictionary<string, string>();
                    foreach (var (key, value) in new[] { ("GARC_X_AP", "0"), ("GARC_Z_BASE", "0"), ("GARC_EYE_HEIGHT", "1.8"),
                                                         ("GARC_CONDITIONS", "만재 출항:/; 밸러스트 도착:/") })
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
            return st.Has(LabelStyle) ? st[LabelStyle] : db.Textstyle;
        }
    }
}
