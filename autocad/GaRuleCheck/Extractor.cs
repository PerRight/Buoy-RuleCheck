// 도면 → raw 스냅샷. 판단은 하지 않고 GA_ 레이어 객체의 좌표만 도면 단위 그대로 옮긴다.
// 단위 변환·검증·호(bulge) 분할은 엔진(contract.py) 한 곳에서만 한다.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace GaRuleCheck
{
    public static class Extractor
    {
        // 엔진 세션 키. FingerprintGuid만 쓰면 같은 원본에서 '다른 이름으로 저장'한 도면(샘플 S140_rev0~4 포함)이
        // 모두 같은 값이라, 동시에 열면 이력이 섞이고 한쪽을 닫을 때 /v1/reset이 다른 쪽 이력까지 지운다.
        // → 열린 문서 인스턴스마다 고유 ID를 붙인다(문서가 닫히면 표에서 자동으로 사라짐).
        private static readonly ConditionalWeakTable<Document, string> DocIds = new();

        public static string DocId(Document doc) =>
            DocIds.GetValue(doc, d => $"{d.Database.FingerprintGuid}:{Guid.NewGuid():N}");

        public static RawDrawing Extract(Document doc)
        {
            var db = doc.Database;
            var raw = new RawDrawing
            {
                Insunits = (int)db.Insunits,
                Source = new Dictionary<string, string>
                {
                    ["app"] = "AutoCAD " + Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("ACADVER"),
                    ["file"] = System.IO.Path.GetFileName(doc.Name),
                    ["doc_id"] = DocId(doc),
                },
            };

            IDictionaryEnumerator en = db.SummaryInfo.CustomProperties;
            while (en.MoveNext())
            {
                if (en.Key is string key)
                    raw.Props[key] = en.Value?.ToString() ?? "";
            }

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent) continue;
                raw.LayerCounts[ent.Layer] = raw.LayerCounts.TryGetValue(ent.Layer, out var n) ? n + 1 : 1;
                var layer = ent.Layer.Trim().ToUpperInvariant();
                if (!Contract.LayerRoles.ContainsKey(layer)) continue;
                raw.Entities.Add(ToRaw(tr, ent, layer));
            }
            tr.Commit();
            return raw;
        }

        private static RawEntity ToRaw(Transaction tr, Entity ent, string layer)
        {
            var r = new RawEntity { Handle = ent.Handle.ToString(), Layer = layer };
            switch (ent)
            {
                case Line l:
                    r.Type = "LINE";
                    r.Points.Add(new[] { l.StartPoint.X, l.StartPoint.Y });
                    r.Points.Add(new[] { l.EndPoint.X, l.EndPoint.Y });
                    break;
                case Polyline p:   // LWPOLYLINE. 호(bulge)·닫힘을 함께 보내고 분할은 엔진이 한다
                {
                    r.Type = "LWPOLYLINE";
                    double sign = p.Normal.Z < 0 ? -1.0 : 1.0;   // 법선이 −Z면 WCS에서 회전 방향이 반대
                    var bulges = new List<double>();
                    for (int i = 0; i < p.NumberOfVertices; i++)
                    {
                        var pt = p.GetPoint3dAt(i);          // WCS
                        r.Points.Add(new[] { pt.X, pt.Y });
                        bulges.Add(sign * p.GetBulgeAt(i));
                    }
                    r.Closed = p.Closed;
                    if (bulges.Any(b => b != 0.0)) r.Bulges = bulges;
                    break;
                }
                case Polyline2d p2:
                {
                    r.Type = "POLYLINE";
                    double sign = p2.Normal.Z < 0 ? -1.0 : 1.0;
                    var bulges = new List<double>();
                    foreach (ObjectId vId in p2)
                    {
                        if (tr.GetObject(vId, OpenMode.ForRead) is not Vertex2d v) continue;
                        // 스플라인 맞춤 폴리선의 제어점은 곡선 위의 점이 아니다 → 제외 (DXF 어댑터와 같게)
                        if (v.VertexType == Vertex2dType.SplineControlVertex) continue;
                        // Vertex2d.Position은 OCS 좌표 → WCS는 VertexPosition으로 (LWPOLYLINE의 GetPoint3dAt과 같게)
                        var wp = p2.VertexPosition(v);
                        r.Points.Add(new[] { wp.X, wp.Y });
                        bulges.Add(sign * v.Bulge);
                    }
                    r.Closed = p2.Closed;
                    if (bulges.Any(b => b != 0.0)) r.Bulges = bulges;
                    break;
                }
                case Polyline3d p3:   // DXF에서는 POLYLINE(3D). 꼭짓점이 WCS, 호 없음
                    r.Type = "POLYLINE";
                    foreach (ObjectId vId in p3)
                    {
                        if (tr.GetObject(vId, OpenMode.ForRead) is not PolylineVertex3d v) continue;
                        if (v.VertexType == Vertex3dType.ControlVertex) continue;
                        r.Points.Add(new[] { v.Position.X, v.Position.Y });
                    }
                    r.Closed = p3.Closed;
                    break;
                case DBPoint dp:
                    r.Type = "POINT";
                    r.Points.Add(new[] { dp.Position.X, dp.Position.Y });
                    break;
                case Table:
                    // Table은 BlockReference의 하위 클래스 — 블록으로 오인하지 않게 먼저 거른다
                    r.Type = "TABLE";
                    break;
                case BlockReference br:
                    // 삽입점만 보낸다. 엔진은 GA_EYE 이외 레이어의 INSERT를 '검토 불가'로 멈춘다(형상 없음)
                    r.Type = "INSERT";
                    r.Points.Add(new[] { br.Position.X, br.Position.Y });
                    break;
                default:
                    // 지원하지 않는 객체도 보낸다 → 엔진이 해당 규칙을 '검토 불가'로 표시 (조용히 무시하지 않음).
                    // DXF 이름은 쓰지 않는다: 폴리페이스 메시 등도 DXF 이름이 "POLYLINE"이라 지원 타입으로 오인될 수 있음.
                    r.Type = ent.GetType().Name.ToUpperInvariant();
                    break;
            }
            return r;
        }

        public static ObjectId HandleToId(Database db, string handle)
        {
            try
            {
                var h = new Handle(Convert.ToInt64(handle, 16));
                return db.GetObjectId(false, h, 0);
            }
            catch
            {
                return ObjectId.Null;
            }
        }
    }
}
